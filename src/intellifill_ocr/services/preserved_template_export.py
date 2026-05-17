from __future__ import annotations

from pathlib import Path

import pandas as pd
from docx import Document
from docx.shared import Inches
from openpyxl import load_workbook
from openpyxl.cell.cell import MergedCell
from openpyxl.drawing.image import Image as ExcelImage

from intellifill_ocr.models.template import TemplateTable
from intellifill_ocr.ui.barcode import barcode_png_bytes


class PreservedTemplateExporter:
    def export(self, template_path: Path, table: TemplateTable, output_path: Path, traceability_code: str = "") -> None:
        suffix = template_path.suffix.lower()
        if suffix == ".xlsx":
            self._export_xlsx(template_path, table, output_path, traceability_code)
            return
        if suffix == ".docx":
            self._export_docx(template_path, table, output_path, traceability_code)
            return
        if suffix == ".csv":
            rows = [[cell.value for cell in row] for row in table.cells]
            if traceability_code:
                rows.extend([[], ["Traceability ID", traceability_code]])
            pd.DataFrame(rows).to_csv(output_path, index=False, header=False)
            return
        raise ValueError("Original-format export is currently available for DOCX, XLSX, and CSV templates.")

    def _export_xlsx(self, template_path: Path, table: TemplateTable, output_path: Path, traceability_code: str) -> None:
        workbook = load_workbook(template_path)
        sheet = workbook.active
        for row_index, row in enumerate(table.cells, start=1):
            for col_index, cell in enumerate(row, start=1):
                target_cell = sheet.cell(row=row_index, column=col_index)
                if isinstance(target_cell, MergedCell):
                    continue
                target_cell.value = cell.value
        if traceability_code:
            traceability_row = sheet.max_row + 2
            sheet.cell(row=traceability_row, column=1).value = "Traceability ID"
            sheet.cell(row=traceability_row, column=2).value = traceability_code
            image = ExcelImage(barcode_png_bytes(traceability_code))
            image.width = 320
            image.height = 72
            sheet.add_image(image, f"A{traceability_row + 1}")
        workbook.save(output_path)

    def _export_docx(self, template_path: Path, table: TemplateTable, output_path: Path, traceability_code: str) -> None:
        document = Document(template_path)
        if not document.tables:
            self._append_docx_traceability(document, traceability_code)
            document.save(output_path)
            return

        target_table = document.tables[0]
        for row_index, row in enumerate(table.cells):
            if row_index >= len(target_table.rows):
                break
            for col_index, source_cell in enumerate(row):
                if col_index >= len(target_table.rows[row_index].cells):
                    break
                self._set_cell_text(target_table.rows[row_index].cells[col_index], source_cell.value)
        self._append_docx_traceability(document, traceability_code)
        document.save(output_path)

    def _set_cell_text(self, cell, value: str) -> None:
        paragraph = cell.paragraphs[0] if cell.paragraphs else cell.add_paragraph()
        if paragraph.runs:
            paragraph.runs[0].text = value
            for run in paragraph.runs[1:]:
                run.text = ""
        else:
            paragraph.add_run(value)

    def _append_docx_traceability(self, document: Document, traceability_code: str) -> None:
        if not traceability_code:
            return
        paragraph = document.add_paragraph()
        paragraph.add_run(f"Traceability ID: {traceability_code}")
        document.add_picture(barcode_png_bytes(traceability_code), width=Inches(3.2))
