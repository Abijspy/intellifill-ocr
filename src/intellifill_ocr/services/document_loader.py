from __future__ import annotations

from pathlib import Path

import pandas as pd
from docx import Document

from intellifill_ocr.models.document import ParsedDocument
from intellifill_ocr.ocr.engine import OCREngine
from intellifill_ocr.ocr.pdf import parse_pdf_text
from intellifill_ocr.utils.exceptions import UnsupportedDocumentError


class DocumentLoader:
    SUPPORTED = {".docx", ".xlsx", ".xls", ".csv", ".png", ".jpg", ".jpeg", ".pdf"}

    def __init__(self, ocr_engine: OCREngine) -> None:
        self.ocr_engine = ocr_engine

    def parse(self, path: Path) -> ParsedDocument:
        suffix = path.suffix.lower()
        if suffix not in self.SUPPORTED:
            raise UnsupportedDocumentError(f"Unsupported file type: {path.suffix}")
        if suffix == ".docx":
            return self._parse_docx(path)
        if suffix in {".xlsx", ".xls"}:
            return self._parse_excel(path)
        if suffix == ".csv":
            return self._parse_csv(path)
        if suffix in {".png", ".jpg", ".jpeg"}:
            text, boxes = self.ocr_engine.image_to_text(path)
            return ParsedDocument(path=path, text=text, ocr_boxes=boxes)
        if suffix == ".pdf":
            return parse_pdf_text(path, self.ocr_engine)
        raise UnsupportedDocumentError(f"Unsupported file type: {path.suffix}")

    def _parse_docx(self, path: Path) -> ParsedDocument:
        doc = Document(path)
        text_parts = [p.text for p in doc.paragraphs if p.text.strip()]
        tables: list[list[list[str]]] = []
        for table in doc.tables:
            parsed_table: list[list[str]] = []
            for row in table.rows:
                parsed_table.append([cell.text.strip() for cell in row.cells])
            tables.append(parsed_table)
            text_parts.extend(" | ".join(row) for row in parsed_table)
        return ParsedDocument(path=path, text="\n".join(text_parts), tables=tables)

    def _parse_excel(self, path: Path) -> ParsedDocument:
        sheets = pd.read_excel(path, sheet_name=None, header=None, dtype=str)
        text_parts: list[str] = []
        tables: list[list[list[str]]] = []
        for sheet_name, frame in sheets.items():
            clean = frame.fillna("")
            table = clean.astype(str).values.tolist()
            tables.append(table)
            text_parts.append(f"[{sheet_name}]")
            text_parts.extend(" | ".join(row) for row in table)
        return ParsedDocument(path=path, text="\n".join(text_parts), tables=tables)

    def _parse_csv(self, path: Path) -> ParsedDocument:
        frame = pd.read_csv(path, header=None, dtype=str).fillna("")
        table = frame.astype(str).values.tolist()
        text = "\n".join(" | ".join(row) for row in table)
        return ParsedDocument(path=path, text=text, tables=[table])
