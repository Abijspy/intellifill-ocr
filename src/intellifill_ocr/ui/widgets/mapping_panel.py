from __future__ import annotations

from PySide6.QtCore import Signal
from PySide6.QtWidgets import QListWidget, QListWidgetItem, QVBoxLayout, QWidget

from intellifill_ocr.models.document import ExtractedField


class MappingPanel(QWidget):
    field_selected = Signal(int)

    def __init__(self) -> None:
        super().__init__()
        self.fields: list[ExtractedField] = []
        self.list_widget = QListWidget()
        self.list_widget.currentRowChanged.connect(self.field_selected.emit)
        layout = QVBoxLayout(self)
        layout.setContentsMargins(0, 0, 0, 0)
        layout.addWidget(self.list_widget)

    def set_fields(self, fields: list[ExtractedField]) -> None:
        self.fields = fields
        self.list_widget.clear()
        for field in fields:
            item = QListWidgetItem(f"{field.label}: {field.value}  ({field.confidence:.0f}%)")
            item.setToolTip(str(field.source_path or "Manual/OCR region"))
            self.list_widget.addItem(item)

    def current_field(self) -> ExtractedField | None:
        row = self.list_widget.currentRow()
        if 0 <= row < len(self.fields):
            return self.fields[row]
        return None
