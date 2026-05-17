from __future__ import annotations

import tempfile
from pathlib import Path

import pandas as pd
import pdfplumber
import pypdfium2 as pdfium
from docx import Document
from docx.enum.text import WD_ALIGN_PARAGRAPH
from docx.shared import Inches
from PySide6.QtCore import QRect, Qt
from PySide6.QtGui import QColor, QFont, QImage, QPainter, QPdfWriter
from PySide6.QtGui import QPageSize

from intellifill_ocr.models.template import TemplateTable
from intellifill_ocr.ui.barcode import barcode_png_bytes, code39_width, draw_code39


class ExportService:
    def to_dataframe(self, template: TemplateTable) -> pd.DataFrame:
        rows = [[cell.value for cell in row] for row in template.cells]
        return pd.DataFrame(rows)

    def export_csv(self, template: TemplateTable, path: Path) -> None:
        self.to_dataframe(template).to_csv(path, header=False, index=False)

    def export_excel(self, template: TemplateTable, path: Path) -> None:
        with pd.ExcelWriter(path, engine="openpyxl") as writer:
            self.to_dataframe(template).to_excel(writer, sheet_name="Output", header=False, index=False)

    def export_word(self, template: TemplateTable, path: Path, traceability_code: str = "") -> None:
        document = Document()
        document.add_heading(template.name or "Filled Template", level=1)
        table = document.add_table(rows=max(template.row_count, 1), cols=max(template.column_count, 1))
        table.style = "Table Grid"
        for row_index, row in enumerate(template.cells):
            for column_index in range(template.column_count):
                value = row[column_index].value if column_index < len(row) else ""
                table.cell(row_index, column_index).text = value
        self._append_word_traceability(document, traceability_code)
        document.save(path)

    def export_pdf(self, template: TemplateTable, path: Path, traceability_code: str = "") -> None:
        writer = QPdfWriter(str(path))
        writer.setPageSize(QPageSize(QPageSize.PageSizeId.A4))
        writer.setResolution(96)
        painter = QPainter(writer)
        try:
            painter.setFont(QFont("Segoe UI", 9))
            x, y = 40, 45
            if traceability_code:
                self._draw_traceability_footer(painter, writer.width(), writer.height(), traceability_code)
            row_height = 24
            col_width = max(110, int((writer.width() - 80) / max(template.column_count, 1)))
            reserved_footer = 96 if traceability_code else 60
            for row in template.cells:
                x = 40
                for cell in row:
                    rect = QRect(x, y, col_width, row_height)
                    painter.drawRect(rect)
                    painter.drawText(
                        rect.adjusted(4, 2, -4, -2),
                        Qt.AlignmentFlag.AlignLeft | Qt.AlignmentFlag.AlignVCenter,
                        cell.value,
                    )
                    x += col_width
                y += row_height
                if y > writer.height() - reserved_footer:
                    writer.newPage()
                    y = 45
                    if traceability_code:
                        self._draw_traceability_footer(painter, writer.width(), writer.height(), traceability_code)
        finally:
            painter.end()

    def export_preserved_pdf(
        self,
        template_path: Path,
        template: TemplateTable,
        path: Path,
        traceability_code: str = "",
    ) -> None:
        if template_path.suffix.lower() != ".pdf" or not self._has_pdf_coordinates(template):
            self.export_pdf(template, path, traceability_code)
            return

        writer = QPdfWriter(str(path))
        writer.setResolution(96)
        painter = QPainter(writer)
        overlays = 0
        try:
            rendered_pdf = pdfium.PdfDocument(str(template_path))
            with pdfplumber.open(template_path) as source_pdf, tempfile.TemporaryDirectory(prefix="intellifill_pdf_export_") as temp_dir:
                for page_index, page in enumerate(source_pdf.pages):
                    if page_index > 0:
                        writer.newPage()

                    bitmap = rendered_pdf[page_index].render(scale=2.0).to_pil()
                    image_path = Path(temp_dir) / f"page_{page_index + 1}.png"
                    bitmap.save(image_path)
                    image = QImage(str(image_path))
                    painter.drawImage(QRect(0, 0, writer.width(), writer.height()), image)

                    if page_index == 0 and traceability_code:
                        self._draw_traceability_footer(painter, writer.width(), writer.height(), traceability_code)

                    scale_x = writer.width() / max(float(page.width), 1.0)
                    scale_y = writer.height() / max(float(page.height), 1.0)
                    painter.setFont(QFont("Segoe UI", 9))
                    painter.setPen(QColor("#111827"))
                    for row in template.cells:
                        for cell in row:
                            if not cell.is_placeholder or not cell.value.strip() or cell.source_page != page_index or not cell.bbox:
                                continue
                            x0, top, x1, bottom = cell.bbox
                            rect = QRect(
                                int(x0 * scale_x) + 3,
                                int(top * scale_y) + 2,
                                max(8, int((x1 - x0) * scale_x) - 6),
                                max(8, int((bottom - top) * scale_y) - 4),
                            )
                            painter.drawText(rect, Qt.AlignmentFlag.AlignLeft | Qt.AlignmentFlag.AlignVCenter, cell.value)
                            overlays += 1
        finally:
            painter.end()

        if overlays == 0:
            self.export_pdf(template, path, traceability_code)

    def _draw_traceability_block(
        self,
        painter: QPainter,
        x: int,
        y: int,
        traceability_code: str,
        center: bool = False,
    ) -> int:
        narrow = 1
        barcode_width = code39_width(traceability_code, narrow=narrow)
        block_width = min(340, max(248, barcode_width + 24))
        block_height = 58
        text_alignment = Qt.AlignmentFlag.AlignHCenter if center else Qt.AlignmentFlag.AlignLeft
        barcode_x = x + max(0, int((block_width - barcode_width) / 2)) if center else x
        painter.save()
        painter.fillRect(QRect(x - 4, y - 4, block_width, block_height), QColor("#ffffff"))
        painter.setPen(QColor("#111827"))
        painter.setFont(QFont("Segoe UI", 8))
        painter.drawText(
            QRect(x, y, block_width - 8, 16),
            text_alignment | Qt.AlignmentFlag.AlignVCenter,
            f"Traceability ID: {traceability_code}",
        )
        draw_code39(painter, traceability_code, barcode_x, y + 20, 30, narrow=narrow, show_text=False)
        painter.restore()
        return block_height

    def _draw_traceability_footer(self, painter: QPainter, page_width: int, page_height: int, traceability_code: str) -> None:
        barcode_width = code39_width(traceability_code, narrow=1)
        block_width = min(340, max(248, barcode_width + 24))
        x = max(4, int((page_width - block_width) / 2))
        y = max(4, page_height - 68)
        self._draw_traceability_block(painter, x, y, traceability_code, center=True)

    def _append_word_traceability(self, document: Document, traceability_code: str) -> None:
        if not traceability_code:
            return
        footer = document.sections[0].footer
        paragraph = footer.add_paragraph()
        paragraph.alignment = WD_ALIGN_PARAGRAPH.CENTER
        paragraph.add_run(f"Traceability ID: {traceability_code}")
        paragraph.add_run().add_break()
        paragraph.add_run().add_picture(
            barcode_png_bytes(traceability_code, narrow=1, bar_height=30),
            width=Inches(2.55),
        )

    def _has_pdf_coordinates(self, template: TemplateTable) -> bool:
        return any(cell.bbox for row in template.cells for cell in row)
