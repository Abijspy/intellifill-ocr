from __future__ import annotations

from dataclasses import dataclass

from intellifill_ocr.models.document import ExtractedField, RegionSelection
from intellifill_ocr.models.template import TemplateTable


@dataclass
class FieldMapping:
    source_label: str
    source_value: str
    target_row: int
    target_column: int
    confidence: float
    region: RegionSelection | None = None


class MappingService:
    def __init__(self) -> None:
        self.mappings: list[FieldMapping] = []

    def add_mapping(
        self,
        field: ExtractedField,
        target_row: int,
        target_column: int,
        confidence: float | None = None,
    ) -> FieldMapping:
        mapping = FieldMapping(
            source_label=field.label,
            source_value=field.value,
            target_row=target_row,
            target_column=target_column,
            confidence=field.confidence if confidence is None else confidence,
            region=field.source_region,
        )
        self.mappings.append(mapping)
        return mapping

    def apply(self, template: TemplateTable) -> TemplateTable:
        for mapping in self.mappings:
            template.set_value(mapping.target_row, mapping.target_column, mapping.source_value)
        return template

    def clear(self) -> None:
        self.mappings.clear()
