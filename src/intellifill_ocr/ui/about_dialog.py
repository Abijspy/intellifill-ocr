from __future__ import annotations

from PySide6.QtCore import QUrl
from PySide6.QtGui import QDesktopServices, QFont, QPixmap
from PySide6.QtWidgets import QDialog, QHBoxLayout, QLabel, QPushButton, QVBoxLayout

from intellifill_ocr import __app_name__, __version__
from intellifill_ocr.utils.paths import resource_path


class AboutReleaseDialog(QDialog):
    def __init__(self, parent=None) -> None:
        super().__init__(parent)
        self.release_url = f"https://github.com/Abijspy/intellifill-ocr/releases/tag/v{__version__}"
        self.setWindowTitle("What's New")
        self.setMinimumWidth(520)

        logo = QLabel()
        logo_path = resource_path("assets/logo_512.png")
        if logo_path.exists():
            pixmap = QPixmap(str(logo_path))
            if not pixmap.isNull():
                logo.setPixmap(pixmap.scaledToWidth(120))

        title = QLabel(f"What's New in {__app_name__} v{__version__}")
        title_font = QFont()
        title_font.setPointSize(18)
        title_font.setBold(True)
        title.setFont(title_font)

        details = QLabel(
            "🖼️ New AutoFill & Export logo is used in the app, package, installer, and README.\n"
            "🎛️ The old top toolbar is replaced by one Actions button with every workflow option.\n"
            "📣 Fresh installs and updates show this changelog automatically on first launch.\n"
            "🏷️ Traceability IDs are shorter, scannable, and printed once at the bottom center of PDF/Word exports.\n"
            "📄 Preserved-layout exports now fill blank/template fields only and keep headings, logos, tables, and signature areas.\n"
            "☀️ Light mode visibility is improved for the mapping workflow."
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
        layout.addWidget(logo)
        layout.addWidget(title)
        layout.addWidget(details)
        layout.addWidget(release_label)
        layout.addLayout(buttons)

    def _open_release_page(self) -> None:
        QDesktopServices.openUrl(QUrl(self.release_url))
