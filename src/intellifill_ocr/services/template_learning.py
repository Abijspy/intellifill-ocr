from __future__ import annotations

import re
from collections import Counter
from dataclasses import dataclass
from typing import Any

from intellifill_ocr.models.document import ExtractedField, ParsedDocument
from intellifill_ocr.services.matching import FieldMatcher


@dataclass(frozen=True)
class LearnedTemplate:
    id: int
    name: str
    document_type: str
    target_template_name: str
    signature: dict[str, Any]
    mappings: list[dict[str, Any]]
    confidence_threshold: float
    usage_count: int = 0


@dataclass(frozen=True)
class TemplateMatch:
    template: LearnedTemplate
    confidence: float
    reasons: list[str]


@dataclass(frozen=True)
class LearnedMappingApplication:
    source_label: str
    source_value: str
    target_table_index: int
    target_row: int
    target_column: int
    confidence: float


class TemplateLearningService:
    """Learns document signatures and reusable mapping coordinates offline."""

    STOP_WORDS = {
        "and",
        "are",
        "for",
        "from",
        "has",
        "have",
        "into",
        "that",
        "the",
        "this",
        "with",
        "your",
        "date",
        "page",
        "table",
        "total",
    }

    def build_signature(self, documents: list[ParsedDocument], fields: list[ExtractedField]) -> dict[str, Any]:
        text = "\n".join(document.text for document in documents)
        table_shapes: list[str] = []
        for document in documents:
            for table in document.tables:
                columns = max((len(row) for row in table), default=0)
                table_shapes.append(f"{len(table)}x{columns}")

        file_types = Counter(document.path.suffix.lower().lstrip(".") for document in documents)
        label_tokens = sorted(
            {
                token
                for field in fields
                for token in self._tokens(field.label)
                if token not in self.STOP_WORDS
            }
        )
        name_tokens = sorted(
            {
                token
                for document in documents
                for token in self._tokens(document.path.stem)
                if token not in self.STOP_WORDS
            }
        )

        return {
            "version": 1,
            "field_labels": sorted({self._normalize(field.label) for field in fields if field.label.strip()}),
            "label_tokens": label_tokens,
            "keywords": self._keywords(text),
            "file_types": dict(file_types),
            "table_shapes": sorted(set(table_shapes)),
            "file_name_tokens": name_tokens,
        }

    def serialize_mappings(self, mappings: list[Any]) -> list[dict[str, Any]]:
        return [
            {
                "source_label": str(mapping.source_label),
                "target_table_index": int(getattr(mapping, "target_table_index", 0)),
                "target_row": int(mapping.target_row),
                "target_column": int(mapping.target_column),
                "confidence": float(mapping.confidence),
            }
            for mapping in mappings
        ]

    def from_records(self, records: list[dict[str, Any]]) -> list[LearnedTemplate]:
        templates: list[LearnedTemplate] = []
        for record in records:
            templates.append(
                LearnedTemplate(
                    id=int(record["id"]),
                    name=str(record.get("name") or ""),
                    document_type=str(record.get("document_type") or ""),
                    target_template_name=str(record.get("target_template_name") or ""),
                    signature=dict(record.get("signature") or {}),
                    mappings=list(record.get("mappings") or []),
                    confidence_threshold=float(record.get("confidence_threshold") or 72.0),
                    usage_count=int(record.get("usage_count") or 0),
                )
            )
        return templates

    def suggest(
        self,
        learned_templates: list[LearnedTemplate],
        current_signature: dict[str, Any],
        minimum_score: float = 45.0,
    ) -> list[TemplateMatch]:
        matches: list[TemplateMatch] = []
        for learned_template in learned_templates:
            confidence, reasons = self.score_signature(learned_template.signature, current_signature)
            if confidence >= minimum_score:
                matches.append(TemplateMatch(learned_template, confidence, reasons))
        return sorted(matches, key=lambda match: match.confidence, reverse=True)

    def score_signature(self, stored: dict[str, Any], current: dict[str, Any]) -> tuple[float, list[str]]:
        label_score = self._jaccard(stored.get("field_labels", []), current.get("field_labels", []))
        token_score = self._jaccard(stored.get("label_tokens", []), current.get("label_tokens", []))
        keyword_score = self._jaccard(stored.get("keywords", []), current.get("keywords", []))
        file_type_score = self._weighted_overlap(stored.get("file_types", {}), current.get("file_types", {}))
        shape_score = self._jaccard(stored.get("table_shapes", []), current.get("table_shapes", []))
        file_name_score = self._jaccard(stored.get("file_name_tokens", []), current.get("file_name_tokens", []))

        score = (
            label_score * 38.0
            + token_score * 24.0
            + keyword_score * 18.0
            + file_type_score * 8.0
            + shape_score * 7.0
            + file_name_score * 5.0
        )
        reasons = self._score_reasons(label_score, token_score, keyword_score, file_type_score, shape_score)
        return min(100.0, round(score, 1)), reasons

    def apply_mappings(
        self,
        learned_template: LearnedTemplate,
        fields: list[ExtractedField],
        matcher: FieldMatcher,
    ) -> list[LearnedMappingApplication]:
        applications: list[LearnedMappingApplication] = []
        used_indexes: set[int] = set()
        for mapping in learned_template.mappings:
            source_label = str(mapping.get("source_label") or "")
            target_table_index = int(mapping.get("target_table_index") or 0)
            target_row = int(mapping.get("target_row") or 0)
            target_column = int(mapping.get("target_column") or 0)
            best_index = -1
            best_score = 0.0
            for field_index, field in enumerate(fields):
                if field_index in used_indexes:
                    continue
                score = matcher.score(source_label, field.label)
                if self._normalize(source_label) == self._normalize(field.label):
                    score = max(score, 98.0)
                if score > best_score:
                    best_score = score
                    best_index = field_index
            if best_index < 0 or best_score < 42:
                continue
            used_indexes.add(best_index)
            field = fields[best_index]
            applications.append(
                LearnedMappingApplication(
                    source_label=field.label,
                    source_value=field.value,
                    target_table_index=target_table_index,
                    target_row=target_row,
                    target_column=target_column,
                    confidence=round(best_score, 1),
                )
            )
        return applications

    def _keywords(self, text: str, limit: int = 40) -> list[str]:
        counter = Counter(token for token in self._tokens(text) if token not in self.STOP_WORDS and len(token) >= 3)
        return [token for token, _count in counter.most_common(limit)]

    def _tokens(self, value: str) -> list[str]:
        return re.findall(r"[a-z0-9]+", value.lower())

    def _normalize(self, value: str) -> str:
        return " ".join(self._tokens(value))

    def _jaccard(self, left: Any, right: Any) -> float:
        left_set = {str(item) for item in left or [] if str(item)}
        right_set = {str(item) for item in right or [] if str(item)}
        if not left_set and not right_set:
            return 0.0
        return len(left_set & right_set) / max(len(left_set | right_set), 1)

    def _weighted_overlap(self, left: dict[str, int], right: dict[str, int]) -> float:
        if not left or not right:
            return 0.0
        shared = sum(min(int(left.get(key, 0)), int(right.get(key, 0))) for key in set(left) | set(right))
        total = max(sum(int(value) for value in left.values()), sum(int(value) for value in right.values()), 1)
        return shared / total

    def _score_reasons(
        self,
        label_score: float,
        token_score: float,
        keyword_score: float,
        file_type_score: float,
        shape_score: float,
    ) -> list[str]:
        reasons: list[str] = []
        if label_score >= 0.45:
            reasons.append("matching field labels")
        if token_score >= 0.45:
            reasons.append("similar label words")
        if keyword_score >= 0.25:
            reasons.append("matching document keywords")
        if file_type_score >= 0.75:
            reasons.append("same file type mix")
        if shape_score >= 0.5:
            reasons.append("similar table layout")
        return reasons or ["partial document similarity"]
