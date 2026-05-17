from __future__ import annotations

import os
from pathlib import Path

from PySide6.QtWidgets import (
    QDialog,
    QComboBox,
    QFileDialog,
    QFormLayout,
    QHBoxLayout,
    QLabel,
    QLineEdit,
    QMessageBox,
    QPushButton,
    QVBoxLayout,
)

from intellifill_ocr.utils.config import AppConfig


class SettingsDialog(QDialog):
    def __init__(self, config: AppConfig, parent=None) -> None:
        super().__init__(parent)
        self.setWindowTitle("Application Settings")
        self.setMinimumWidth(680)

        self.tesseract_edit = QLineEdit(config.tesseract_cmd or "")
        self.database_edit = QLineEdit(str(config.database_path))
        self.language_edit = QLineEdit(config.default_language)
        self.theme_combo = QComboBox()
        self.theme_combo.addItem("Dark mode", "dark")
        self.theme_combo.addItem("Light mode", "light")
        self.theme_combo.setCurrentIndex(0 if config.theme == "dark" else 1)

        tesseract_browse = QPushButton("Browse")
        tesseract_browse.clicked.connect(self._browse_tesseract)
        database_browse = QPushButton("Browse")
        database_browse.clicked.connect(self._browse_database)

        tesseract_row = QHBoxLayout()
        tesseract_row.addWidget(self.tesseract_edit)
        tesseract_row.addWidget(tesseract_browse)

        database_row = QHBoxLayout()
        database_row.addWidget(self.database_edit)
        database_row.addWidget(database_browse)

        form = QFormLayout()
        form.addRow("Tesseract OCR executable", tesseract_row)
        form.addRow("SQLite database file", database_row)
        form.addRow("OCR language", self.language_edit)
        form.addRow("Appearance", self.theme_combo)

        note = QLabel(
            "Changing the SQLite path switches the app to that database and creates the schema if needed. "
            "Changing Tesseract affects new OCR operations. Use Tools to preview SQLite records or view logs."
        )
        note.setWordWrap(True)

        save_button = QPushButton("Save")
        save_button.clicked.connect(self.accept)
        cancel_button = QPushButton("Cancel")
        cancel_button.clicked.connect(self.reject)

        buttons = QHBoxLayout()
        buttons.addStretch(1)
        buttons.addWidget(cancel_button)
        buttons.addWidget(save_button)

        layout = QVBoxLayout(self)
        layout.addLayout(form)
        layout.addWidget(note)
        layout.addLayout(buttons)

    def selected_config(self, current: AppConfig) -> AppConfig:
        database_path = Path(os.path.expandvars(self.database_edit.text().strip())).expanduser()
        tesseract_text = self.tesseract_edit.text().strip()
        return AppConfig(
            database_path=database_path,
            log_file=current.log_file,
            tesseract_cmd=tesseract_text or None,
            default_language=self.language_edit.text().strip() or "eng",
            theme=str(self.theme_combo.currentData() or "dark"),
        )

    def validate(self) -> bool:
        tesseract_text = self.tesseract_edit.text().strip()
        if tesseract_text:
            tesseract_path = Path(tesseract_text)
            if not tesseract_path.exists() or tesseract_path.name.lower() != "tesseract.exe":
                QMessageBox.warning(self, "Invalid Tesseract path", "Select a valid tesseract.exe file.")
                return False

        database_path = Path(os.path.expandvars(self.database_edit.text().strip())).expanduser()
        if not database_path.name.lower().endswith((".sqlite", ".sqlite3", ".db")):
            QMessageBox.warning(self, "Invalid database path", "Choose a .sqlite, .sqlite3, or .db file.")
            return False
        if not database_path.parent.exists():
            QMessageBox.warning(self, "Invalid database folder", "The selected SQLite folder does not exist.")
            return False
        return True

    def accept(self) -> None:
        if self.validate():
            super().accept()

    def _browse_tesseract(self) -> None:
        path, _ = QFileDialog.getOpenFileName(
            self,
            "Select tesseract.exe",
            self.tesseract_edit.text() or r"C:\Program Files\Tesseract-OCR",
            "Tesseract OCR (tesseract.exe);;Executable (*.exe)",
        )
        if path:
            self.tesseract_edit.setText(path)

    def _browse_database(self) -> None:
        path, _ = QFileDialog.getSaveFileName(
            self,
            "Select SQLite database",
            self.database_edit.text(),
            "SQLite database (*.sqlite3 *.sqlite *.db)",
        )
        if path:
            self.database_edit.setText(path)
