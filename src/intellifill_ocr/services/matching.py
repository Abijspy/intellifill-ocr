from __future__ import annotations

import re
from dataclasses import dataclass
from difflib import SequenceMatcher

from intellifill_ocr.models.document import ExtractedField, ParsedDocument
from intellifill_ocr.models.template import TemplateTable


@dataclass
class MatchSuggestion:
    source_label: str
    source_value: str
    target_table_index: int
    target_row: int
    target_column: int
    target_label: str
    confidence: float


class FieldMatcher:
    KEY_VALUE_PATTERN = re.compile(r"^\s*([^:|,=\t]{2,80})\s*[:|,=\t]\s*(.+?)\s*$")

    def extract_fields(self, documents: list[ParsedDocument]) -> list[ExtractedField]:
        fields: list[ExtractedField] = []
        for document in documents:
            fields.extend(self._from_tables(document))
            fields.extend(self._from_text(document))
        return self._dedupe(fields)

    def suggest(self, template: TemplateTable, fields: list[ExtractedField]) -> list[MatchSuggestion]:
        destinations = self._destinations(template)
        candidates: list[tuple[int, MatchSuggestion]] = []
        for field_index, field in enumerate(fields):
            for table_index, row, column, label in destinations:
                confidence = self.score(field.label, label)
                if confidence >= 45:
                    candidates.append(
                        (
                            field_index,
                            MatchSuggestion(field.label, field.value, table_index, row, column, label, confidence),
                        )
                    )

        suggestions: list[MatchSuggestion] = []
        used_sources: set[int] = set()
        used_destinations: set[tuple[int, int, int]] = set()
        for field_index, suggestion in sorted(candidates, key=lambda item: item[1].confidence, reverse=True):
            destination = (suggestion.target_table_index, suggestion.target_row, suggestion.target_column)
            if field_index in used_sources or destination in used_destinations:
                continue
            used_sources.add(field_index)
            used_destinations.add(destination)
            suggestions.append(suggestion)
        return suggestions

    def score(self, source: str, target: str) -> float:
        norm_source = self._normalize(source)
        norm_target = self._normalize(target)
        if not norm_source or not norm_target:
            return 0.0
        ratio = SequenceMatcher(None, norm_source, norm_target).ratio()
        source_tokens = set(norm_source.split())
        target_tokens = set(norm_target.split())
        token_overlap = len(source_tokens & target_tokens) / max(len(source_tokens | target_tokens), 1)
        synonym_bonus = self._synonym_bonus(norm_source, norm_target)
        return min(100.0, (ratio * 65.0) + (token_overlap * 30.0) + synonym_bonus)

    def _from_tables(self, document: ParsedDocument) -> list[ExtractedField]:
        fields: list[ExtractedField] = []
        for table in document.tables:
            for row in table:
                if len(row) >= 2 and row[0].strip() and row[1].strip():
                    fields.append(ExtractedField(label=row[0].strip(), value=row[1].strip(), confidence=95, source_path=document.path))
        return fields

    def _from_text(self, document: ParsedDocument) -> list[ExtractedField]:
        fields: list[ExtractedField] = []
        for line in document.text.splitlines():
            match = self.KEY_VALUE_PATTERN.match(line)
            if match:
                fields.append(
                    ExtractedField(
                        label=match.group(1).strip(),
                        value=match.group(2).strip(),
                        confidence=80,
                        source_path=document.path,
                    )
                )
        return fields

    def _destinations(self, template: TemplateTable) -> list[tuple[int, int, int, str]]:
        destinations: list[tuple[int, int, int, str]] = []
        seen: set[tuple[int, int, int]] = set()
        for table in template.all_tables():
            for row_index, row in enumerate(table.cells):
                for col_index, cell in enumerate(row):
                    if not cell.value.strip():
                        label = self._label_for_blank_cell(table, row_index, col_index)
                        destination = (table.table_index, row_index, col_index)
                        if label and destination not in seen:
                            destinations.append((table.table_index, row_index, col_index, label))
                            seen.add(destination)

        if destinations:
            return destinations

        for table in template.all_tables():
            for row_index, row in enumerate(table.cells):
                for col_index, cell in enumerate(row):
                    if cell.value.strip():
                        target_column = min(col_index + 1, max(table.column_count - 1, 0))
                        destination = (table.table_index, row_index, target_column)
                        if destination not in seen:
                            destinations.append((table.table_index, row_index, target_column, cell.value.strip()))
                            seen.add(destination)
                        break
        return destinations

    def _label_for_blank_cell(self, template: TemplateTable, row: int, column: int) -> str:
        left_label = self._nearest_left_label(template, row, column)
        top_label = self._nearest_top_label(template, row, column)
        if left_label and top_label:
            return f"{top_label} {left_label}"
        return left_label or top_label

    def _nearest_left_label(self, template: TemplateTable, row: int, column: int) -> str:
        for col_index in range(column - 1, -1, -1):
            value = template.value_at(row, col_index).strip()
            if value:
                return value
        return ""

    def _nearest_top_label(self, template: TemplateTable, row: int, column: int) -> str:
        for row_index in range(row - 1, -1, -1):
            value = template.value_at(row_index, column).strip()
            if value:
                return value
        return ""

    def _dedupe(self, fields: list[ExtractedField]) -> list[ExtractedField]:
        seen: set[tuple[str, str]] = set()
        unique: list[ExtractedField] = []
        for field in fields:
            key = (self._normalize(field.label), field.value.strip())
            if key not in seen:
                seen.add(key)
                unique.append(field)
        return unique

    def _normalize(self, value: str) -> str:
        value = value.lower()
        value = value.replace("no.", "number").replace("no", "number")
        value = value.replace("cust", "customer").replace("amt", "amount")
        value = re.sub(r"[^a-z0-9]+", " ", value)
        return re.sub(r"\s+", " ", value).strip()

    def _synonym_bonus(self, source: str, target: str) -> float:
        pairs = [("gst", "tax"), ("grand total", "total amount"), ("date", "invoice date")]
        return 10.0 if any(a in source and b in target or b in source and a in target for a, b in pairs) else 0.0
