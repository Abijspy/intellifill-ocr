from __future__ import annotations

from PySide6.QtCore import QUrl
from PySide6.QtGui import QDesktopServices, QFont, QPixmap
from PySide6.QtWidgets import QDialog, QHBoxLayout, QLabel, QPushButton, QTextBrowser, QVBoxLayout

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

        details = QTextBrowser()
        details.setMinimumHeight(360)
        details.setOpenExternalLinks(False)
        details.setHtml(self._changelog_html())

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
        layout.addWidget(details, 1)
        layout.addWidget(release_label)
        layout.addLayout(buttons)

    def _open_release_page(self) -> None:
        QDesktopServices.openUrl(QUrl(self.release_url))

    def _changelog_html(self) -> str:
        return """
        <html>
        <body>
        <h2>📘 Version 2.2.1</h2>
        <ul>
          <li>Added a full offline User Guide under Actions &gt; Help.</li>
          <li>Made the in-app What's New changelog scrollable and complete from v1.0.0 onward.</li>
          <li>Changed GitHub release notes so release pages show only the latest version's notes.</li>
        </ul>

        <h2>🧠 Version 2.2.0</h2>
        <ul>
          <li>Template Learning saves reusable mappings, detects similar documents later, and applies them with confidence scores.</li>
          <li>Validation checks warn about required blanks, GST/GSTIN format, dates, amounts, duplicate IDs, and invoice total mismatches.</li>
          <li>Signature and stamp detection helps review approvals while preserved exports keep original marks intact.</li>
          <li>Windows scanner import can acquire source images directly from local WIA scanner drivers.</li>
        </ul>

        <h2>🧩 Version 2.1.0</h2>
        <ul>
          <li>Actions &gt; Panels can show, hide, or restore Uploaded Files, Extracted Fields, and Output Preview.</li>
          <li>Closing those panels is no longer a dead end; restore them from the Actions button.</li>
        </ul>

        <h2>📌 Version 2.0.1</h2>
        <ul>
          <li>Windows taskbar pins now use the new icon when pinned from the updated shortcut.</li>
          <li>Installer shortcuts use the same app identity as the running application.</li>
        </ul>

        <h2>🖼️ Version 2.0.0</h2>
        <ul>
          <li>New AutoFill &amp; Export logo is used in the app, package, installer, and README.</li>
          <li>The old top toolbar is replaced by one Actions button with every workflow option.</li>
          <li>Fresh installs and updates show this changelog automatically on first launch.</li>
          <li>Traceability IDs are shorter, scannable, and printed once at the bottom center of PDF/Word exports.</li>
          <li>Preserved-layout exports fill blank/template fields only and keep headings, logos, tables, and signature areas.</li>
          <li>Light mode visibility is improved for the mapping workflow.</li>
        </ul>

        <h2>🔎 Version 1.1.1</h2>
        <ul>
          <li>Installer guidance explains that Tesseract OCR must be installed locally and SQLite storage is local/offline.</li>
          <li>Added SQLite database preview and application log viewer.</li>
          <li>Added About/What's New release page with installed version number.</li>
          <li>Added Check for Updates with download-and-launch installer flow.</li>
        </ul>

        <h2>📦 Version 1.1.0</h2>
        <ul>
          <li>Added Windows installer support and release packaging for the standalone EXE.</li>
          <li>Added GitHub release pipeline assets for portable ZIP and setup installer distribution.</li>
        </ul>

        <h2>🚀 Version 1.0.0</h2>
        <ul>
          <li>Initial offline OCR desktop app with PySide6 GUI, Tesseract OCR, OpenCV preprocessing, document parsing, and SQLite storage.</li>
          <li>Supported template/source upload for Word, Excel, CSV, images, and PDF where available.</li>
          <li>Added visual OCR region selection, extracted field mapping, editable output table preview, and export to CSV, Excel, Word, and PDF.</li>
          <li>Added traceability code storage, mapping configurations, uploaded file metadata, and core packaging scripts.</li>
        </ul>
        </body>
        </html>
        """
