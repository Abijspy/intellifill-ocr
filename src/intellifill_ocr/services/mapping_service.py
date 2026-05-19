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
    target_table_index: int = 0
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
        target_table_index: int = 0,
    ) -> FieldMapping:
        mapping = FieldMapping(
            source_label=field.label,
            source_value=field.value,
            target_row=target_row,
            target_column=target_column,
            confidence=field.confidence if confidence is None else confidence,
            target_table_index=target_table_index,
            region=field.source_region,
        )
        self.mappings.append(mapping)
        return mapping

    def apply(self, template: TemplateTable) -> TemplateTable:
        tables = template.all_tables()
        for mapping in self.mappings:
            target_table = tables[mapping.target_table_index] if mapping.target_table_index < len(tables) else template
            target_table.set_value(mapping.target_row, mapping.target_column, mapping.source_value)
        return template

    def clear(self) -> None:
        self.mappings.clear()
