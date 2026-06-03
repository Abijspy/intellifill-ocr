from __future__ import annotations

from PySide6.QtGui import QFont, QPixmap
from PySide6.QtWidgets import QDialog, QHBoxLayout, QLabel, QPushButton, QTextBrowser, QVBoxLayout

from intellifill_ocr import __app_name__, __version__
from intellifill_ocr.ui.dialog_utils import keep_dialog_on_screen
from intellifill_ocr.utils.paths import resource_path


class AboutReleaseDialog(QDialog):
    def __init__(self, parent=None) -> None:
        super().__init__(parent)
        self.setWindowTitle("What's New")
        self.setMinimumWidth(520)
        keep_dialog_on_screen(self, 720, 680)

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

        close_button = QPushButton("Close")
        close_button.clicked.connect(self.accept)

        buttons = QHBoxLayout()
        buttons.addStretch(1)
        buttons.addWidget(close_button)

        layout = QVBoxLayout(self)
        layout.addWidget(logo)
        layout.addWidget(title)
        layout.addWidget(details, 1)
        layout.addLayout(buttons)

    def _changelog_html(self) -> str:
        return """
        <html>
        <body>
        <h2>🪟 Version 2.4.2</h2>
        <ul>
          <li>Added a scrolling Installation details output window below the Windows setup progress bar.</li>
          <li>Each installer operation is appended as it runs, including app file copying, optional Tesseract setup preparation, and final metadata steps.</li>
          <li>Repeated progress events are de-duplicated so the output stays readable during long installs.</li>
        </ul>

        <h2>🛠️ Version 2.4.1</h2>
        <ul>
          <li>Fixed the Windows uninstaller runtime proc error seen during uninstall.</li>
          <li>Removed fragile custom uninstall progress label updates while keeping stable uninstall logging and default progress UI.</li>
          <li>Installer operations still show progress during installation.</li>
        </ul>

        <h2>🧰 Version 2.4.0</h2>
        <ul>
          <li>Major installer upgrade with Full, Minimal, and Custom setup types.</li>
          <li>The Windows setup installer now shows the current operation during install and uninstall.</li>
          <li>If Tesseract OCR is missing, setup can optionally download, verify, and launch the Tesseract OCR 5.5.0 installer.</li>
          <li>Installer metadata now includes registry entries and an install.ini file with version, path, and install mode.</li>
          <li>Update checks and package downloads now use a 180-second network timeout.</li>
        </ul>

        <h2>🌗 Version 2.3.2</h2>
        <ul>
          <li>User Guide and Feature Help workflow diagrams are now readable in dark mode.</li>
          <li>Screenshot-style maps, flowcharts, warning boxes, and help panels now use theme-aware colors.</li>
          <li>The detailed help content remains the same, but contrast is improved in both dark and light themes.</li>
        </ul>

        <h2>🪟 Version 2.3.1</h2>
        <ul>
          <li>Dock panels now use Qt6 native close and float controls instead of custom title-bar buttons.</li>
          <li>Removed the custom [] and X panel buttons that could be hard to read or behave inconsistently.</li>
          <li>Actions &gt; Panels still restores Uploaded Files, Extracted Fields, and Output Preview if a panel is closed.</li>
        </ul>

        <h2>🗂️ Version 2.3.0</h2>
        <ul>
          <li>Template documents with two or more tables now load every table into the Output Preview.</li>
          <li>Use the table selector above the output grid to fill Table 1, Table 2, Table 3, and any later tables.</li>
          <li>Manual mappings, intelligent matching, learned templates, validation, SQLite storage, and exports now remember the destination table number.</li>
          <li>CSV, Excel, Word, PDF, and preserved-layout exports include all template tables in one output document.</li>
          <li>The release pipeline can build Linux Debian and Fedora packages from GitHub Actions.</li>
          <li>Linux update checks can download .deb or .rpm packages and show the correct terminal install command.</li>
          <li>Ubuntu/Debian/Fedora builds automatically detect local Tesseract from PATH and common install locations.</li>
          <li>Removed the external release link from this What's New page.</li>
        </ul>

        <h2>🧭 Version 2.2.4</h2>
        <ul>
          <li>Added smoother wheel scrolling for tables, logs, parsed text, help, database preview, and changelog pages.</li>
          <li>Polished scrollbar styling in light and dark mode.</li>
          <li>Expanded the in-app User Guide with clearer workflow, mapping, export, validation, and troubleshooting details.</li>
        </ul>

        <h2>🏷️ Version 2.2.3</h2>
        <ul>
          <li>PDF traceability barcodes now render as clear bottom-center barcode images instead of collapsed black strips.</li>
          <li>Barcode exports keep a white quiet zone and wider modules so the ID remains scannable in PDF viewers and prints.</li>
        </ul>

        <h2>🩹 Version 2.2.2.1</h2>
        <ul>
          <li>Dock panel close and float controls now use custom high-contrast buttons in dark and light mode.</li>
          <li>Removed reliance on native Windows/Qt dock glyphs that could remain white and disappear against the button box.</li>
        </ul>

        <h2>🪟 Version 2.2.2</h2>
        <ul>
          <li>Large help, database, log, validation, detection, and learned-template windows now open inside the visible screen area.</li>
          <li>Dock panel close/float buttons are visible again in light mode.</li>
        </ul>

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
