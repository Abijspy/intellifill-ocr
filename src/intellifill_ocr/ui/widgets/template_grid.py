from __future__ import annotations

from PySide6.QtCore import Signal
from PySide6.QtWidgets import QAbstractItemView, QTableWidget, QTableWidgetItem

from intellifill_ocr.models.template import TemplateCell, TemplateTable


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
