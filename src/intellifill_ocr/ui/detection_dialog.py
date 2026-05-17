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

from intellifill_ocr.services.signature_detection import DetectedMark
from intellifill_ocr.ui.dialog_utils import keep_dialog_on_screen


class DetectionDialog(QDialog):
    def __init__(self, marks: list[DetectedMark], parent=None) -> None:
        super().__init__(parent)
        self.setWindowTitle("Signature and Stamp Detection")
        keep_dialog_on_screen(self, 980, 560)

        summary = QLabel(
            "Detected marks are heuristic offline results. Preserved-layout DOCX/PDF exports keep the original signature "
            "and stamp artwork because they fill blank fields only."
        )
        summary.setWordWrap(True)

        table = QTableWidget()
        table.setAlternatingRowColors(True)
        table.setEditTriggers(QAbstractItemView.EditTrigger.NoEditTriggers)
        table.setSelectionBehavior(QAbstractItemView.SelectionBehavior.SelectRows)
        table.setColumnCount(7)
        table.setRowCount(len(marks))
        table.setHorizontalHeaderLabels(["Type", "Confidence", "File", "Page", "Box", "Reason", "Verify"])
        for row_index, mark in enumerate(marks):
            values = [
                mark.kind,
                f"{mark.confidence:.1f}%",
                mark.source_path.name,
                str(mark.page + 1),
                f"{mark.x},{mark.y},{mark.width},{mark.height}" if mark.width and mark.height else "text-only",
                mark.reason,
                "Review visually before compliance use" if mark.confidence < 75 else "Likely mark",
            ]
            for column_index, value in enumerate(values):
                item = QTableWidgetItem(value)
                item.setToolTip(value)
                table.setItem(row_index, column_index, item)
        if marks:
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
