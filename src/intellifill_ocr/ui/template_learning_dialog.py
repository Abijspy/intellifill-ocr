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

from intellifill_ocr.services.template_learning import TemplateMatch


class TemplateSuggestionDialog(QDialog):
    def __init__(self, matches: list[TemplateMatch], parent=None) -> None:
        super().__init__(parent)
        self.matches = matches
        self.selected_match: TemplateMatch | None = None
        self.setWindowTitle("Learned Template Suggestions")
        self.resize(900, 520)

        summary = QLabel("Reusable mapping templates that look similar to the uploaded source documents.")
        summary.setWordWrap(True)

        self.table = QTableWidget()
        self.table.setAlternatingRowColors(True)
        self.table.setEditTriggers(QAbstractItemView.EditTrigger.NoEditTriggers)
        self.table.setSelectionBehavior(QAbstractItemView.SelectionBehavior.SelectRows)
        self.table.setColumnCount(6)
        self.table.setRowCount(len(matches))
        self.table.setHorizontalHeaderLabels(["Name", "Confidence", "Document Type", "Mappings", "Used", "Why"])
        for row_index, match in enumerate(matches):
            learned = match.template
            values = [
                learned.name,
                f"{match.confidence:.1f}%",
                learned.document_type or "General",
                str(len(learned.mappings)),
                str(learned.usage_count),
                ", ".join(match.reasons),
            ]
            for column_index, value in enumerate(values):
                item = QTableWidgetItem(value)
                item.setToolTip(value)
                self.table.setItem(row_index, column_index, item)
        if matches:
            self.table.setCurrentCell(0, 0)
        self.table.resizeColumnsToContents()

        apply_button = QPushButton("Apply Selected")
        apply_button.clicked.connect(self._apply_selected)
        close_button = QPushButton("Close")
        close_button.clicked.connect(self.reject)

        buttons = QHBoxLayout()
        buttons.addStretch(1)
        buttons.addWidget(close_button)
        buttons.addWidget(apply_button)

        layout = QVBoxLayout(self)
        layout.addWidget(summary)
        layout.addWidget(self.table)
        layout.addLayout(buttons)

    def _apply_selected(self) -> None:
        row = self.table.currentRow()
        if 0 <= row < len(self.matches):
            self.selected_match = self.matches[row]
            self.accept()
