from __future__ import annotations

import tempfile
from pathlib import Path

import pdfplumber
import pypdfium2 as pdfium

from intellifill_ocr.models.document import ParsedDocument
from intellifill_ocr.ocr.engine import OCREngine


def parse_pdf_text(path: Path, ocr_engine: OCREngine | None = None) -> ParsedDocument:
    text_parts: list[str] = []
    tables: list[list[list[str]]] = []
    with pdfplumber.open(path) as pdf:
        for page in pdf.pages:
            text_parts.append(page.extract_text() or "")
            for table in page.extract_tables() or []:
                tables.append([[(cell or "").strip() for cell in row] for row in table])
    parsed = ParsedDocument(path=path, text="\n".join(text_parts).strip(), tables=tables)
    if parsed.text or not ocr_engine:
        return parsed

    boxes = []
    rendered_text: list[str] = []
    pdf = pdfium.PdfDocument(str(path))
    with tempfile.TemporaryDirectory(prefix="intellifill_pdf_") as temp_dir:
        for page_index in range(len(pdf)):
            bitmap = pdf[page_index].render(scale=2.0).to_pil()
            image_path = Path(temp_dir) / f"page_{page_index + 1}.png"
            bitmap.save(image_path)
            page_text, page_boxes = ocr_engine.image_to_text(image_path)
            for box in page_boxes:
                box.page = page_index
            boxes.extend(page_boxes)
            rendered_text.append(page_text)
    parsed.text = "\n".join(rendered_text)
    parsed.ocr_boxes = boxes
    return parsed
