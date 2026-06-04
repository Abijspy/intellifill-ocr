IntelliFill OCR Offline Install Package
======================================

This package includes:

- IntelliFillOCR_3.0.1.0_x64.msix
- IntelliFillOCR_SigningCert.pfx
- IntelliFillOCR_SigningCert.cer
- install-msix.ps1
- portable/IntelliFillOCR/

Recommended install
-------------------

1. Copy this extracted folder to the offline Windows machine.
2. Open PowerShell as Administrator.
3. Run:

   powershell.exe -ExecutionPolicy Bypass -File .\install-msix.ps1 `
     -MsixPath .\IntelliFillOCR_3.0.1.0_x64.msix `
     -CertificatePath .\IntelliFillOCR_SigningCert.pfx `
     -CertificatePassword "ChangeThisPassword" `
     -TrustMachineStore

Portable fallback
-----------------

If MSIX installation is blocked by policy, run:

   .\portable\IntelliFillOCR\IntelliFillOCR.exe

OCR dependency
--------------

Tesseract OCR must be installed on the target machine, or the app must be pointed
to a local tesseract.exe from Actions > Settings.

Recommended Tesseract path:

   C:\Program Files\Tesseract-OCR\tesseract.exe

After install
-------------

Open Actions > Settings in the app to choose:

- Tesseract OCR executable
- SQLite database path
- OCR language
- Dark or light appearance

The current build supports PDF upload for templates and source files, immediate
document/text/table preview after upload, traceability barcode IDs for extraction
runs, Word export, and layout-preserving export for DOCX, XLSX, CSV, and detected
PDF template table cells, reusable learned templates, validation checks,
signature/stamp detection, and direct Windows scanner import.

Traceability barcode
--------------------

Each template upload starts an extraction run and creates a traceability ID. The
ID is saved in SQLite and printed once as text plus a compact Code 39 barcode at
the bottom center of PDF and Word exports. Use it to match a final exported file
back to the saved database run.
