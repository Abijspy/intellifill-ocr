from __future__ import annotations

from PySide6.QtWidgets import (
    QAbstractItemView,
    QDialog,
    QHBoxLayout,
    QLabel,
    QPushButton,
    QTableWidget,
    QTableWidgetItem,
    QVBoxLayout,
)

from intellifill_ocr.services.validation import ValidationIssue


class ValidationDialog(QDialog):
    def __init__(self, issues: list[ValidationIssue], parent=None) -> None:
        super().__init__(parent)
        self.setWindowTitle("Validation Results")
        self.resize(980, 560)

        error_count = sum(1 for issue in issues if issue.severity.lower() == "error")
        warning_count = len(issues) - error_count
        summary = QLabel(f"{error_count} error(s), {warning_count} warning(s)")
        summary.setWordWrap(True)

        table = QTableWidget()
        table.setAlternatingRowColors(True)
        table.setEditTriggers(QAbstractItemView.EditTrigger.NoEditTriggers)
        table.setSelectionBehavior(QAbstractItemView.SelectionBehavior.SelectRows)
        table.setColumnCount(7)
        table.setRowCount(len(issues))
        table.setHorizontalHeaderLabels(["Severity", "Rule", "Cell", "Field", "Value", "Message", "Location"])
        for row_index, issue in enumerate(issues):
            values = [
                issue.severity,
                issue.rule,
                f"R{issue.row + 1} C{issue.column + 1}",
                issue.field_name,
                issue.value,
                issue.message,
                f"{issue.row},{issue.column}",
            ]
            for column_index, value in enumerate(values):
                item = QTableWidgetItem(value)
                item.setToolTip(value)
                table.setItem(row_index, column_index, item)
        if len(issues) <= 80:
            table.resizeColumnsToContents()

        close_button = QPushButton("Close")
        close_button.clicked.connect(self.accept)
        buttons = QHBoxLayout()
        buttons.addStretch(1)
        buttons.addWidget(close_button)

        layout = QVBoxLayout(self)
        layout.addWidget(summary)
        layout.addWidget(table)
        layout.addLayout(buttons)
