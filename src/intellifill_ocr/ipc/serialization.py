from __future__ import annotations

from dataclasses import asdict
from pathlib import Path
from typing import Any

from intellifill_ocr.models.document import ExtractedField, OCRBox, ParsedDocument, RegionSelection
from intellifill_ocr.models.template import TemplateCell, TemplateTable
from intellifill_ocr.services.mapping_service import FieldMapping
from intellifill_ocr.services.matching import MatchSuggestion
from intellifill_ocr.services.validation import ValidationIssue


def template_to_dict(template: TemplateTable) -> dict[str, Any]:
    return {
        "name": template.name,
        "table_count": template.table_count,
        "tables": [table_to_dict(table) for table in template.all_tables()],
    }


def table_to_dict(table: TemplateTable) -> dict[str, Any]:
    return {
        "name": table.name,
        "label": table.label,
        "table_index": table.table_index,
        "row_count": table.row_count,
        "column_count": table.column_count,
        "cells": [[cell_to_dict(cell) for cell in row] for row in table.cells],
    }


def cell_to_dict(cell: TemplateCell) -> dict[str, Any]:
    return {
        "row": cell.row,
        "column": cell.column,
        "value": cell.value,
        "is_placeholder": cell.is_placeholder,
        "row_span": cell.row_span,
        "column_span": cell.column_span,
        "table_index": cell.table_index,
        "source_page": cell.source_page,
        "bbox": list(cell.bbox) if cell.bbox else None,
    }


def parsed_document_to_dict(document: ParsedDocument, source_id: int) -> dict[str, Any]:
    return {
        "source_id": source_id,
        "path": str(document.path),
        "name": document.path.name,
        "suffix": document.path.suffix.lower(),
        "text": document.text,
        "tables": document.tables,
        "ocr_boxes": [ocr_box_to_dict(box) for box in document.ocr_boxes],
        "metadata": dict(document.metadata),
    }


def ocr_box_to_dict(box: OCRBox) -> dict[str, Any]:
    return asdict(box)


def extracted_field_to_dict(field: ExtractedField, field_id: int) -> dict[str, Any]:
    return {
        "field_id": field_id,
        "label": field.label,
        "value": field.value,
        "confidence": field.confidence,
        "source_path": str(field.source_path) if field.source_path else "",
        "source_region": region_to_dict(field.source_region) if field.source_region else None,
    }


def region_to_dict(region: RegionSelection) -> dict[str, Any]:
    return {
        "source_path": str(region.source_path),
        "page": region.page,
        "x": region.x,
        "y": region.y,
        "width": region.width,
        "height": region.height,
    }


def suggestion_to_dict(suggestion: MatchSuggestion, suggestion_id: int) -> dict[str, Any]:
    return {
        "suggestion_id": suggestion_id,
        "source_label": suggestion.source_label,
        "source_value": suggestion.source_value,
        "target_table_index": suggestion.target_table_index,
        "target_row": suggestion.target_row,
        "target_column": suggestion.target_column,
        "target_label": suggestion.target_label,
        "confidence": suggestion.confidence,
    }


def mapping_to_dict(mapping: FieldMapping, mapping_id: int) -> dict[str, Any]:
    return {
        "mapping_id": mapping_id,
        "source_label": mapping.source_label,
        "source_value": mapping.source_value,
        "target_table_index": mapping.target_table_index,
        "target_row": mapping.target_row,
        "target_column": mapping.target_column,
        "confidence": mapping.confidence,
        "region": region_to_dict(mapping.region) if mapping.region else None,
    }


def validation_issue_to_dict(issue: ValidationIssue) -> dict[str, Any]:
    return asdict(issue)


def path_from_param(params: dict[str, Any], key: str) -> Path:
    value = str(params.get(key) or "").strip()
    if not value:
        raise ValueError(f"Missing required path parameter: {key}")
    return Path(value).expanduser().resolve()
