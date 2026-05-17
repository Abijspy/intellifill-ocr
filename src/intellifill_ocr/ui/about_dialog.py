from __future__ import annotations

from PySide6.QtCore import QUrl
from PySide6.QtGui import QDesktopServices, QFont
from PySide6.QtWidgets import QDialog, QHBoxLayout, QLabel, QPushButton, QVBoxLayout

from intellifill_ocr import __app_name__, __version__


class AboutReleaseDialog(QDialog):
    def __init__(self, parent=None) -> None:
        super().__init__(parent)
        self.release_url = f"https://github.com/Abijspy/intellifill-ocr/releases/tag/v{__version__}"
        self.setWindowTitle("What's New")
        self.setMinimumWidth(520)

        title = QLabel(f"What's New in {__app_name__} v{__version__}")
        title_font = QFont()
        title_font.setPointSize(18)
        title_font.setBold(True)
        title.setFont(title_font)

        details = QLabel(
            "🧭 Installer now shows prerequisite notes before setup.\n"
            "🗄️ SQLite database preview is available from Tools.\n"
            "📋 Application logs can be viewed from Tools.\n"
            "🚀 Check for Updates can download and launch newer installers.\n"
            "🏷️ This page shows the installed release version."
        )
        details.setWordWrap(True)

        release_label = QLabel(f"Release page:\n{self.release_url}")
        release_label.setWordWrap(True)

        open_release_button = QPushButton("Open GitHub Release")
        open_release_button.clicked.connect(self._open_release_page)
        close_button = QPushButton("Close")
        close_button.clicked.connect(self.accept)

        buttons = QHBoxLayout()
        buttons.addWidget(open_release_button)
        buttons.addStretch(1)
        buttons.addWidget(close_button)

        layout = QVBoxLayout(self)
        layout.addWidget(title)
        layout.addWidget(details)
        layout.addWidget(release_label)
        layout.addLayout(buttons)

    def _open_release_page(self) -> None:
        QDesktopServices.openUrl(QUrl(self.release_url))
