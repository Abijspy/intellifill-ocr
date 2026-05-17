from __future__ import annotations

import sqlite3
from pathlib import Path
from typing import Any

from PySide6.QtCore import QUrl
from PySide6.QtGui import QDesktopServices
from PySide6.QtWidgets import (
    QAbstractItemView,
    QComboBox,
    QDialog,
    QHBoxLayout,
    QLabel,
    QMessageBox,
    QPushButton,
    QTableWidget,
    QTableWidgetItem,
    QVBoxLayout,
)

from intellifill_ocr.ui.dialog_utils import keep_dialog_on_screen


class DatabasePreviewDialog(QDialog):
    """Read-only preview of the configured SQLite database."""

    ROW_LIMIT = 500

    def __init__(self, database_path: Path, parent=None) -> None:
        super().__init__(parent)
        self.database_path = database_path
        self.setWindowTitle("SQLite Database Preview")
        keep_dialog_on_screen(self, 1050, 680)

        self.path_label = QLabel(f"Database: {self.database_path}")
        self.path_label.setWordWrap(True)
        self.summary_label = QLabel("Select a table to preview stored records.")

        self.table_selector = QComboBox()
        self.table_selector.currentTextChanged.connect(self._load_selected_table)

        self.preview_table = QTableWidget()
        self.preview_table.setAlternatingRowColors(True)
        self.preview_table.setEditTriggers(QAbstractItemView.EditTrigger.NoEditTriggers)
        self.preview_table.setSelectionBehavior(QAbstractItemView.SelectionBehavior.SelectRows)

        refresh_button = QPushButton("Refresh")
        refresh_button.clicked.connect(self.refresh)
        open_folder_button = QPushButton("Open Database Folder")
        open_folder_button.clicked.connect(self._open_database_folder)
        close_button = QPushButton("Close")
        close_button.clicked.connect(self.accept)

        buttons = QHBoxLayout()
        buttons.addWidget(refresh_button)
        buttons.addWidget(open_folder_button)
        buttons.addStretch(1)
        buttons.addWidget(close_button)

        layout = QVBoxLayout(self)
        layout.addWidget(self.path_label)
        layout.addWidget(QLabel("Table"))
        layout.addWidget(self.table_selector)
        layout.addWidget(self.summary_label)
        layout.addWidget(self.preview_table)
        layout.addLayout(buttons)

        self.refresh()

    def refresh(self) -> None:
        self.table_selector.blockSignals(True)
        self.table_selector.clear()
        try:
            tables = self._table_names()
        except sqlite3.Error as exc:
            self.table_selector.blockSignals(False)
            self._show_sqlite_error("Database preview failed", exc)
            return

        self.table_selector.addItems(tables)
        self.table_selector.blockSignals(False)

        if tables:
            self.table_selector.setCurrentIndex(0)
            self._load_table(tables[0])
        else:
            self.summary_label.setText("No application tables were found in this database.")
            self.preview_table.clear()
            self.preview_table.setRowCount(0)
            self.preview_table.setColumnCount(0)

    def _table_names(self) -> list[str]:
        with self._connect() as connection:
            rows = connection.execute(
                """
                SELECT name
                FROM sqlite_master
                WHERE type = 'table' AND name NOT LIKE 'sqlite_%'
                ORDER BY name
                """
            ).fetchall()
        return [str(row[0]) for row in rows]

    def _load_selected_table(self, table_name: str) -> None:
        if table_name:
            self._load_table(table_name)

    def _load_table(self, table_name: str) -> None:
        try:
            quoted_table = self._quote_identifier(table_name)
            with self._connect() as connection:
                count = int(connection.execute(f"SELECT COUNT(*) FROM {quoted_table}").fetchone()[0])
                cursor = connection.execute(f"SELECT * FROM {quoted_table} LIMIT ?", (self.ROW_LIMIT,))
                rows = cursor.fetchall()
                headers = [description[0] for description in cursor.description or []]
        except sqlite3.Error as exc:
            self._show_sqlite_error("Table preview failed", exc)
            return

        self.summary_label.setText(
            f"{table_name}: showing {len(rows)} of {count} row(s)"
            + (f" (limited to {self.ROW_LIMIT})" if count > self.ROW_LIMIT else "")
        )
        self._render_rows(headers, rows)

    def _render_rows(self, headers: list[str], rows: list[sqlite3.Row]) -> None:
        self.preview_table.clear()
        self.preview_table.setColumnCount(len(headers))
        self.preview_table.setRowCount(len(rows))
        self.preview_table.setHorizontalHeaderLabels(headers)
        self.preview_table.setVerticalHeaderLabels([str(row_index + 1) for row_index in range(len(rows))])

        for row_index, row in enumerate(rows):
            for column_index, value in enumerate(row):
                display_value = self._display_value(value)
                item = QTableWidgetItem(display_value)
                item.setToolTip("" if value is None else str(value))
                self.preview_table.setItem(row_index, column_index, item)

        if len(rows) * max(len(headers), 1) <= 1200:
            self.preview_table.resizeColumnsToContents()
            self.preview_table.resizeRowsToContents()

    def _connect(self) -> sqlite3.Connection:
        if not self.database_path.exists():
            raise sqlite3.OperationalError(f"Database file does not exist: {self.database_path}")
        connection = sqlite3.connect(f"file:{self.database_path}?mode=ro", uri=True)
        connection.row_factory = sqlite3.Row
        return connection

    @staticmethod
    def _display_value(value: Any) -> str:
        if value is None:
            return ""
        text = str(value)
        return text if len(text) <= 500 else f"{text[:500]}..."

    @staticmethod
    def _quote_identifier(identifier: str) -> str:
        return '"' + identifier.replace('"', '""') + '"'

    def _open_database_folder(self) -> None:
        self.database_path.parent.mkdir(parents=True, exist_ok=True)
        QDesktopServices.openUrl(QUrl.fromLocalFile(str(self.database_path.parent)))

    def _show_sqlite_error(self, title: str, exc: sqlite3.Error) -> None:
        QMessageBox.warning(self, title, str(exc))
