from __future__ import annotations

from pathlib import Path

from PySide6.QtCore import QUrl
from PySide6.QtGui import QDesktopServices, QFont, QTextCursor
from PySide6.QtWidgets import QDialog, QHBoxLayout, QLabel, QPushButton, QTextEdit, QVBoxLayout

from intellifill_ocr.ui.dialog_utils import keep_dialog_on_screen


class LogViewerDialog(QDialog):
    """Simple read-only viewer for the application log file."""

    MAX_LINES = 2000

    def __init__(self, log_file: Path, parent=None) -> None:
        super().__init__(parent)
        self.log_file = log_file
        self.setWindowTitle("Application Logs")
        keep_dialog_on_screen(self, 1000, 650)

        self.path_label = QLabel(f"Log file: {self.log_file}")
        self.path_label.setWordWrap(True)

        self.log_text = QTextEdit()
        self.log_text.setReadOnly(True)
        self.log_text.setLineWrapMode(QTextEdit.LineWrapMode.NoWrap)
        self.log_text.setFont(QFont("Consolas", 10))

        refresh_button = QPushButton("Refresh")
        refresh_button.clicked.connect(self.refresh)
        open_folder_button = QPushButton("Open Log Folder")
        open_folder_button.clicked.connect(self._open_log_folder)
        close_button = QPushButton("Close")
        close_button.clicked.connect(self.accept)

        buttons = QHBoxLayout()
        buttons.addWidget(refresh_button)
        buttons.addWidget(open_folder_button)
        buttons.addStretch(1)
        buttons.addWidget(close_button)

        layout = QVBoxLayout(self)
        layout.addWidget(self.path_label)
        layout.addWidget(self.log_text)
        layout.addLayout(buttons)

        self.refresh()

    def refresh(self) -> None:
        if not self.log_file.exists():
            self.log_text.setPlainText("No log file has been created yet.")
            return

        lines = self.log_file.read_text(encoding="utf-8", errors="replace").splitlines()
        if len(lines) > self.MAX_LINES:
            visible_lines = lines[-self.MAX_LINES :]
            prefix = f"Showing the last {self.MAX_LINES} of {len(lines)} log lines.\n\n"
        else:
            visible_lines = lines
            prefix = ""
        self.log_text.setPlainText(prefix + "\n".join(visible_lines))
        cursor = self.log_text.textCursor()
        cursor.movePosition(QTextCursor.MoveOperation.End)
        self.log_text.setTextCursor(cursor)

    def _open_log_folder(self) -> None:
        self.log_file.parent.mkdir(parents=True, exist_ok=True)
        QDesktopServices.openUrl(QUrl.fromLocalFile(str(self.log_file.parent)))
