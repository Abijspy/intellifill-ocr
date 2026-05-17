from __future__ import annotations

import re
from dataclasses import dataclass
from datetime import datetime

from intellifill_ocr.models.template import TemplateTable


@dataclass(frozen=True)
class ValidationIssue:
    severity: str
    rule: str
    row: int
    column: int
    field_name: str
    value: str
    message: str


class ValidationEngine:
    """Offline validation rules for filled template output."""

    GST_PATTERN = re.compile(r"^\d{2}[A-Z]{5}\d{4}[A-Z][A-Z0-9]Z[A-Z0-9]$")
    DATE_FORMATS = ("%d-%m-%Y", "%d/%m/%Y", "%Y-%m-%d", "%m/%d/%Y", "%d.%m.%Y")

    def validate(self, template: TemplateTable) -> list[ValidationIssue]:
        issues: list[ValidationIssue] = []
        snapshots = self._field_snapshots(template)
        issues.extend(self._required_issues(template))
        issues.extend(self._format_issues(snapshots))
        issues.extend(self._duplicate_issues(snapshots))
        issues.extend(self._total_mismatch_issues(snapshots))
        return issues

    def _required_issues(self, template: TemplateTable) -> list[ValidationIssue]:
        issues: list[ValidationIssue] = []
        for row_index, row in enumerate(template.cells):
            for column_index, cell in enumerate(row):
                if cell.value.strip():
                    continue
                label = self._label_for_blank_cell(template, row_index, column_index)
                if not label:
                    continue
                if cell.is_placeholder or self._looks_like_required_destination(label):
                    issues.append(
                        ValidationIssue(
                            severity="Warning",
                            rule="Required field",
                            row=row_index,
                            column=column_index,
                            field_name=label,
                            value="",
                            message="Required-looking destination cell is still blank.",
                        )
                    )
        return issues

    def _format_issues(self, snapshots: list[dict[str, object]]) -> list[ValidationIssue]:
        issues: list[ValidationIssue] = []
        for item in snapshots:
            label = str(item["label"])
            value = str(item["value"]).strip()
            if not value:
                continue
            normalized_label = self._normalize(label)
            if self._looks_like_gst_identifier_label(normalized_label):
                cleaned = re.sub(r"\s+", "", value.upper())
                if not self.GST_PATTERN.match(cleaned):
                    issues.append(self._issue("Error", "GST format", item, "Invalid GST/GSTIN format."))
            if "date" in normalized_label and not self._is_valid_date(value):
                issues.append(self._issue("Warning", "Date format", item, "Date could not be parsed."))
            if self._looks_like_amount_label(normalized_label) and self._parse_amount(value) is None:
                issues.append(self._issue("Warning", "Amount format", item, "Amount field is not numeric."))
        return issues

    def _duplicate_issues(self, snapshots: list[dict[str, object]]) -> list[ValidationIssue]:
        watched: dict[str, list[dict[str, object]]] = {}
        for item in snapshots:
            label = self._normalize(str(item["label"]))
            value = str(item["value"]).strip()
            if not value or not self._is_duplicate_sensitive_label(label):
                continue
            watched.setdefault(value.lower(), []).append(item)

        issues: list[ValidationIssue] = []
        for duplicates in watched.values():
            if len(duplicates) < 2:
                continue
            for item in duplicates:
                issues.append(self._issue("Warning", "Duplicate value", item, "Duplicate identifier-like value found."))
        return issues

    def _total_mismatch_issues(self, snapshots: list[dict[str, object]]) -> list[ValidationIssue]:
        totals = self._amounts_by_kind(snapshots)
        if not totals["total"]:
            return []
        subtotal = sum(amount for amount, _item in totals["subtotal"])
        tax = sum(amount for amount, _item in totals["tax"])
        if subtotal <= 0 or tax < 0:
            return []

        issues: list[ValidationIssue] = []
        expected = round(subtotal + tax, 2)
        for amount, item in totals["total"]:
            if abs(amount - expected) > 0.05:
                issues.append(
                    self._issue(
                        "Warning",
                        "Total mismatch",
                        item,
                        f"Invoice total {amount:.2f} does not match subtotal + tax {expected:.2f}.",
                    )
                )
        return issues

    def _field_snapshots(self, template: TemplateTable) -> list[dict[str, object]]:
        snapshots: list[dict[str, object]] = []
        for row_index, row in enumerate(template.cells):
            for column_index, cell in enumerate(row):
                value = cell.value.strip()
                if not value:
                    continue
                label = self._label_for_value_cell(template, row_index, column_index)
                if not label or self._normalize(label) == self._normalize(value):
                    continue
                snapshots.append(
                    {
                        "row": row_index,
                        "column": column_index,
                        "label": label,
                        "value": value,
                    }
                )
        return snapshots

    def _amounts_by_kind(self, snapshots: list[dict[str, object]]) -> dict[str, list[tuple[float, dict[str, object]]]]:
        result: dict[str, list[tuple[float, dict[str, object]]]] = {"subtotal": [], "tax": [], "total": []}
        for item in snapshots:
            label = self._normalize(str(item["label"]))
            amount = self._parse_amount(str(item["value"]))
            if amount is None:
                continue
            if "grand total" in label or "invoice total" in label or label == "total" or "total amount" in label:
                result["total"].append((amount, item))
            elif "subtotal" in label or "sub total" in label or "taxable" in label:
                result["subtotal"].append((amount, item))
            elif "tax" in label or "gst" in label or "igst" in label or "cgst" in label or "sgst" in label or "vat" in label:
                result["tax"].append((amount, item))
        return result

    def _issue(self, severity: str, rule: str, item: dict[str, object], message: str) -> ValidationIssue:
        return ValidationIssue(
            severity=severity,
            rule=rule,
            row=int(item["row"]),
            column=int(item["column"]),
            field_name=str(item["label"]),
            value=str(item["value"]),
            message=message,
        )

    def _label_for_blank_cell(self, template: TemplateTable, row: int, column: int) -> str:
        left = self._nearest_left_label(template, row, column)
        top = self._nearest_top_label(template, row, column)
        return left or top

    def _label_for_value_cell(self, template: TemplateTable, row: int, column: int) -> str:
        left = self._nearest_left_label(template, row, column)
        top = self._nearest_top_label(template, row, column)
        if left:
            return left
        return top if column > 0 else ""

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

    def _looks_like_required_destination(self, label: str) -> bool:
        normalized = self._normalize(label)
        terms = ("invoice", "customer", "vendor", "date", "amount", "total", "gst", "approved", "rejected")
        return any(term in normalized for term in terms)

    def _looks_like_amount_label(self, normalized_label: str) -> bool:
        if self._looks_like_gst_identifier_label(normalized_label):
            return False
        terms = ("amount", "total", "price", "tax", "gst", "balance", "subtotal", "value")
        return any(term in normalized_label for term in terms)

    def _looks_like_gst_identifier_label(self, normalized_label: str) -> bool:
        terms = ("gstin", "gst number", "gst no", "gst registration", "tax id")
        return any(term in normalized_label for term in terms)

    def _is_duplicate_sensitive_label(self, normalized_label: str) -> bool:
        terms = ("invoice number", "invoice no", "document number", "doc no", "gst", "gstin", "po number")
        return any(term in normalized_label for term in terms)

    def _parse_amount(self, value: str) -> float | None:
        cleaned = re.sub(r"[^0-9.\-]", "", value)
        if not cleaned or cleaned in {"-", ".", "-."}:
            return None
        try:
            return float(cleaned)
        except ValueError:
            return None

    def _is_valid_date(self, value: str) -> bool:
        text = value.strip()
        for date_format in self.DATE_FORMATS:
            try:
                datetime.strptime(text, date_format)
                return True
            except ValueError:
                continue
        return False

    def _normalize(self, value: str) -> str:
        value = value.lower().replace("no.", "number").replace("no", "number")
        value = re.sub(r"[^a-z0-9]+", " ", value)
        return re.sub(r"\s+", " ", value).strip()
