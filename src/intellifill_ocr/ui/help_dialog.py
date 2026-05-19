from __future__ import annotations

from PySide6.QtCore import QUrl
from PySide6.QtGui import QDesktopServices, QFont
from PySide6.QtWidgets import QDialog, QHBoxLayout, QPushButton, QTextBrowser, QVBoxLayout

from intellifill_ocr.ui.dialog_utils import keep_dialog_on_screen


class HelpGuideDialog(QDialog):
    """Offline user guide for the main IntelliFill OCR workflows."""

    def __init__(self, parent=None) -> None:
        super().__init__(parent)
        self.setWindowTitle("IntelliFill OCR User Guide")
        keep_dialog_on_screen(self, 980, 720)

        guide = QTextBrowser()
        guide.setOpenExternalLinks(False)
        guide.anchorClicked.connect(QDesktopServices.openUrl)
        guide.setHtml(self._guide_html())

        title_font = QFont()
        title_font.setPointSize(11)
        guide.setFont(title_font)

        close_button = QPushButton("Close")
        close_button.clicked.connect(self.accept)

        buttons = QHBoxLayout()
        buttons.addStretch(1)
        buttons.addWidget(close_button)

        layout = QVBoxLayout(self)
        layout.addWidget(guide)
        layout.addLayout(buttons)

    def _guide_html(self) -> str:
        return """
        <html>
        <body>
        <h1>IntelliFill OCR User Guide</h1>
        <p>
        IntelliFill OCR is a fully offline Windows desktop application for reading documents,
        extracting fields with OCR/parsers, mapping source data into a template, validating the
        result, and exporting a traceable filled document.
        </p>

        <h2>Main Screen</h2>
        <ul>
          <li><b>Actions button</b>: all upload, mapping, learning, export, tools, settings, and help commands.</li>
          <li><b>Uploaded Files panel</b>: templates and source files currently loaded.</li>
          <li><b>Center preview</b>: document image/PDF preview, parsed text, and parsed tables.</li>
          <li><b>Extracted Fields panel</b>: fields detected from tables, key/value text, or selected OCR regions.</li>
          <li><b>Output Preview panel</b>: destination template table. Click exact cells here before mapping.</li>
        </ul>
        <p>
        Scrollable panels, tables, logs, and help pages use smooth wheel scrolling. Hold
        <b>Shift</b> while scrolling to move sideways in wide tables. The document preview keeps
        its wheel zoom behavior so you can inspect scanned pages closely.
        </p>

        <h2>Recommended Workflow</h2>
        <ol>
          <li>Open <b>Actions &gt; Upload Template</b> and choose the destination template.</li>
          <li>Open <b>Actions &gt; Upload Source Files</b> or <b>Actions &gt; Scan Source Document</b>.</li>
          <li>Review the center <b>Document Preview</b>, <b>Parsed Text</b>, and <b>Parsed Tables</b> tabs.</li>
          <li>Use <b>Actions &gt; Auto Fill Matching Fields</b> for fuzzy matching.</li>
          <li>For manual work, select a source field on the right, click a destination cell at the bottom, then use <b>Actions &gt; Map Selected Field to Destination Cell</b>.</li>
          <li>Use <b>Actions &gt; Run Validation Checks</b> before saving or exporting.</li>
          <li>Use <b>Actions &gt; Save Filled Output to SQLite</b> and then export from <b>Actions &gt; Export Filled Output</b>.</li>
        </ol>
        <p>
        A good office workflow is: upload the template, upload all sources, review extracted text,
        use automatic matching for obvious fields, manually map any uncertain fields, validate, save
        to SQLite, then export. This keeps the original evidence, mapping decisions, and final output
        traceable.
        </p>

        <h2>Templates</h2>
        <p>
        Use <b>Actions &gt; Upload Template</b> for CSV, Excel, Word, image, or PDF templates.
        Templates may contain headings, logos, blank cells, placeholders, merged rows/columns,
        inner tables, and approval/signature areas. The editable output preview shows the table
        model that will be filled.
        </p>
        <p>
        If a template has two or more tables, the <b>Output Preview</b> panel shows a table selector
        above the grid. Choose <b>Table 1</b>, <b>Table 2</b>, or any later table before mapping.
        Mappings remember the selected table number, so values intended for a second approval,
        summary, or line-item table do not overwrite the first table.
        </p>
        <p>
        For DOCX, XLSX, and supported PDF templates, use the preserved-layout exports to keep
        headings, logos, table structure, signature areas, and existing document artwork while
        filling only blank/template fields.
        </p>
        <p>
        If a template contains labels such as "Approved By", "Rejected By", "Invoice Number", or
        blank cells beside headings, IntelliFill OCR treats the labels as destination context and
        fills the blank cells. Merged cells are preserved in the preview model where the input format
        exposes that structure.
        </p>

        <h2>Source Files and Scanning</h2>
        <p>
        Use <b>Actions &gt; Upload Source Files</b> to add up to five DOCX, XLSX/XLS, CSV, PNG,
        JPG/JPEG, or PDF files. Use <b>Actions &gt; Scan Source Document</b> to acquire an image
        from a local Windows WIA scanner. Scanned images are stored locally and treated as source
        documents.
        </p>
        <p>
        After upload, check all three center tabs:
        </p>
        <ul>
          <li><b>Document Preview</b>: confirms the correct file/page is visible and lets you draw OCR regions.</li>
          <li><b>Parsed Text</b>: shows raw extracted text that can explain why a match did or did not happen.</li>
          <li><b>Parsed Tables</b>: shows detected rows/columns. Use this to confirm table structure before mapping.</li>
        </ul>

        <h2>OCR and Region Selection</h2>
        <p>
        Image and scanned PDF content is processed offline through Tesseract, pytesseract, OpenCV,
        deskewing, denoising, and confidence scoring. In the document preview, drag a rectangle
        around any area to run region OCR. A new "OCR Region" field appears in the Extracted Fields
        panel and can be mapped like any other source value.
        </p>
        <p>
        Region OCR is the best option when a source document has no tables, when a value is printed
        in a paragraph, or when a scan is too messy for automatic extraction. Zoom the document
        preview, draw tightly around the value, review the extracted field text, then map it to the
        exact destination cell.
        </p>

        <h2>Manual Mapping</h2>
        <p>
        Manual mapping is for documents without clean tables or when OCR needs human review.
        Select the desired source field in the right panel, click the exact destination cell in the
        output preview, then choose <b>Actions &gt; Map Selected Field to Destination Cell</b>.
        You can edit any output cell directly before saving.
        </p>
        <ol>
          <li>Click the source field in <b>Extracted Fields</b>.</li>
          <li>If the template has multiple tables, choose the destination table in <b>Output Preview</b>.</li>
          <li>Click the destination cell in <b>Output Preview</b>.</li>
          <li>Run <b>Map Selected Field to Destination Cell</b>.</li>
          <li>Correct the destination cell text manually if OCR misread letters or numbers.</li>
        </ol>
        <p>
        For repeated forms, finish one clean mapping and save it as a learned template. Future
        documents of the same type can then be filled with far less manual work.
        </p>

        <h2>Intelligent Matching</h2>
        <p>
        <b>Actions &gt; Auto Fill Matching Fields</b> compares extracted source labels with blank
        destination fields using fuzzy matching, keyword matching, synonyms, and confidence scores.
        Example matches include "Invoice No" to "Invoice Number" and "Cust Name" to "Customer Name".
        </p>
        <p>
        Review confidence scores before saving. High-confidence matches are usually safe, while low
        confidence matches should be checked against the source preview. Automatic matching never
        prevents manual edits; the output preview remains editable until you save/export.
        </p>

        <h2>Template Learning</h2>
        <p>
        Template Learning turns one manual mapping into a reusable automation.
        </p>
        <ol>
          <li>Upload a template and source document family, such as Invoice Type A.</li>
          <li>Map the fields once.</li>
          <li>Choose <b>Actions &gt; Template Learning &gt; Save Current Mapping as Learned Template</b>.</li>
          <li>Later, upload similar source documents. IntelliFill OCR will suggest matching learned templates with confidence scores.</li>
          <li>Use <b>Suggest Learned Templates</b> to review options or <b>Apply Best Learned Template</b> to fill quickly.</li>
        </ol>

        <h2>Validation Rules</h2>
        <p>
        Use <b>Actions &gt; Run Validation Checks</b> to find required blanks, invalid GST/GSTIN,
        date problems, non-numeric amount fields, duplicate identifier values, and invoice
        subtotal/tax/total mismatches. Validation warnings also appear before saving or exporting.
        Cells with issues are highlighted in the output preview.
        </p>
        <p>
        Validation is a warning system, not a lock. You can still export when a warning is expected,
        but the highlighted cells show where a supervisor should review before final storage.
        </p>

        <h2>Signature and Stamp Detection</h2>
        <p>
        Use <b>Actions &gt; Tools &gt; Detect Signatures and Stamps</b> to scan loaded documents
        for signature-like ink, stamp-like marks, and approval keywords. Detection is heuristic,
        so review the preview visually for compliance work. Preserved-layout exports keep the
        original signature and stamp artwork because only blank fields are filled.
        </p>

        <h2>Traceability Barcode</h2>
        <p>
        Every template upload starts a SQLite extraction run and creates a compact traceability ID.
        The ID is saved with the run and printed as a bottom-center Code 39 barcode on PDF and Word
        exports. Use it to match an exported file back to the saved database record.
        </p>
        <p>
        In PDF exports the barcode is rendered with a white quiet zone and wider bars so it remains
        readable in common PDF viewers and printed copies. If a barcode looks unclear after printing,
        export again at the default size and avoid scaling the PDF page during print.
        </p>

        <h2>Saving and Database Preview</h2>
        <p>
        Use <b>Actions &gt; Save Filled Output to SQLite</b> to store completed values, mappings,
        uploaded file metadata, timestamps, and traceability IDs. Use <b>Actions &gt; Tools &gt;
        Preview SQLite Database</b> for a read-only view of stored tables.
        </p>

        <h2>Exports</h2>
        <ul>
          <li><b>CSV</b>: one file containing every output table separated by table headings.</li>
          <li><b>Excel Workbook</b>: one workbook with each output table on its own sheet.</li>
          <li><b>Word Document</b>: one Word file containing every output table and the traceability barcode.</li>
          <li><b>PDF with Traceability Barcode</b>: one generated PDF containing every output table and barcode.</li>
          <li><b>Export Filled Template - Preserve Original Layout</b>: fills all supported DOCX/XLSX template tables in place where possible.</li>
          <li><b>Export Filled Template PDF - Preserve Original Layout</b>: keeps original PDF page artwork and overlays values into detected blank cells across all detected tables when coordinates are available.</li>
        </ul>
        <p>
        Choose generated PDF/Word when you want a clean table built from the output preview. Choose
        preserved-layout export when the original template branding, headings, logos, merged rows,
        signature blocks, or approval text must remain exactly where they were.
        </p>

        <h2>Panels</h2>
        <p>
        If you close Uploaded Files, Extracted Fields, or Output Preview, open <b>Actions &gt;
        Panels</b> to show or restore them. Use <b>Restore All Panels</b> if the workspace becomes
        hard to navigate.
        </p>

        <h2>Settings</h2>
        <p>
        Use <b>Actions &gt; Settings</b> to choose the local Tesseract OCR executable, SQLite
        database path, OCR language, and dark/light appearance. The app remains offline; no cloud
        OCR or cloud database is used. On Ubuntu/Debian/Fedora, the app can auto-detect local
        Tesseract from PATH or common locations such as <b>/usr/bin/tesseract</b>.
        </p>
        <ul>
          <li><b>Tesseract path</b>: select the local <code>tesseract.exe</code> on Windows or <code>tesseract</code> on Linux. Use <b>Auto Detect</b> when available.</li>
          <li><b>SQLite database</b>: choose where runs, mappings, metadata, and extracted values are stored.</li>
          <li><b>OCR language</b>: use English by default; install local language packs for other languages.</li>
          <li><b>Theme</b>: switch between light and dark mode without changing your saved data.</li>
        </ul>

        <h2>Updates, Logs, and Troubleshooting</h2>
        <ul>
          <li><b>Actions &gt; Help &gt; Check for Updates</b>: checks releases and can download the newer Windows installer or Linux .deb/.rpm package for this platform.</li>
          <li><b>Actions &gt; Help &gt; What's New</b>: full scrollable in-app changelog.</li>
          <li><b>Actions &gt; Tools &gt; View Application Logs</b>: opens the local log viewer.</li>
          <li>If OCR fails, confirm Tesseract is installed and configured in Settings.</li>
          <li>If scanner import fails, confirm the scanner works through Windows and has WIA/TWAIN drivers installed.</li>
        </ul>
        </body>
        </html>
        """
