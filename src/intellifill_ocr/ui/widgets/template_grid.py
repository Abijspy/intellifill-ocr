from __future__ import annotations

from PySide6.QtCore import Signal
from PySide6.QtGui import QBrush, QColor
from PySide6.QtWidgets import QAbstractItemView, QTableWidget, QTableWidgetItem

from intellifill_ocr.models.template import TemplateCell, TemplateTable
from intellifill_ocr.services.validation import ValidationIssue


class TemplateGrid(QTableWidget):
    destination_selected = Signal(int, int)

    def __init__(self) -> None:
        super().__init__()
        self.template: TemplateTable | None = None
        self.setSelectionBehavior(QAbstractItemView.SelectionBehavior.SelectItems)
        self.setAlternatingRowColors(True)
        self.cellClicked.connect(self.destination_selected.emit)
        self.cellChanged.connect(self._sync_cell)

    def load_template(self, template: TemplateTable) -> None:
        self.blockSignals(True)
        self.template = template
        self.setRowCount(template.row_count)
        self.setColumnCount(template.column_count)
        for row in range(template.row_count):
            for col in range(template.column_count):
                item = QTableWidgetItem(template.value_at(row, col))
                if not template.value_at(row, col):
                    item.setToolTip("Blank destination cell")
                self.setItem(row, col, item)
                cell = template.cells[row][col]
                if cell.row_span > 1 or cell.column_span > 1:
                    self.setSpan(row, col, cell.row_span, cell.column_span)
        self.resizeColumnsToContents()
        self.blockSignals(False)

    def update_from_template(self) -> None:
        if self.template:
            self.load_template(self.template)

    def highlight_validation_issues(self, issues: list[ValidationIssue]) -> None:
        self.clear_validation_highlights()
        for issue in issues:
            item = self.item(issue.row, issue.column)
            if not item:
                continue
            color = QColor("#fecaca") if issue.severity.lower() == "error" else QColor("#fde68a")
            item.setBackground(QBrush(color))
            tooltip = item.toolTip()
            message = f"{issue.severity}: {issue.message}"
            item.setToolTip(f"{tooltip}\n{message}" if tooltip else message)

    def clear_validation_highlights(self) -> None:
        for row in range(self.rowCount()):
            for column in range(self.columnCount()):
                item = self.item(row, column)
                if item:
                    item.setBackground(QBrush())
                    item.setToolTip("Blank destination cell" if not item.text().strip() else "")

    def current_destination(self) -> tuple[int, int] | None:
        item = self.currentItem()
        if not item:
            return None
        return item.row(), item.column()

    def _sync_cell(self, row: int, col: int) -> None:
        if not self.template:
            return
        item = self.item(row, col)
        while len(self.template.cells) <= row:
            self.template.cells.append([])
        while len(self.template.cells[row]) <= col:
            self.template.cells[row].append(TemplateCell(row=row, column=len(self.template.cells[row])))
        self.template.cells[row][col].value = item.text() if item else ""
