<p align="center">
  <img src="assets/logo_512.png" alt="AutoFill & Export logo" width="220">
</p>

# IntelliFill OCR Desktop

IntelliFill OCR Desktop is a fully offline Windows desktop application for OCR-driven data extraction, visual field mapping, and table/form filling.

It supports template upload, source document upload, region-based OCR, fuzzy field matching, editable previews, SQLite persistence, traceability barcodes, and export to CSV/Excel/PDF or the original template format where supported.

## Features

- Native WinUI 3 Windows package under `winui/IntelliFillOCR.WinUI`, built with Windows App SDK and Mica.
- Native WinUI template upload previews detected tables before source extraction and mapping.
- Actions > Panels can show, hide, or restore Uploaded Files, Extracted Fields, and Output Preview after those dock panels are closed.
- Offline OCR using Tesseract, `pytesseract`, OpenCV preprocessing, deskewing, denoising, confidence scoring, and bounding boxes.
- Template import from CSV, Excel, DOCX tables, images, and PDFs, including templates with multiple tables. The Output Preview table selector lets users fill each table separately while saving/exporting them together.
- Source import from DOCX, XLSX/XLS, CSV, PNG/JPG/JPEG, and PDFs, with the same document/text/table preview tabs.
- Manual region selection for OCR and exact destination-cell selection.
- Drag/click mapping workflow from extracted text to template cells.
- Fuzzy and keyword matching with confidence percentages.
- Template Learning System: save a mapped document family once, auto-detect similar future documents, view confidence scores, and apply reusable mappings instantly.
- Validation Rules Engine for required fields, GST/GSTIN format, dates, amounts, duplicate identifiers, and invoice subtotal/tax/total mismatch warnings.
- Signature and stamp detection for approval/compliance documents, with visual review and preserved-layout exports that keep original signature/stamp artwork intact.
- Direct Windows scanner import through local WIA scanner drivers, saving scanned pages as offline source images.
- Header-aware matching that fills blank cells beside or under template headings.
- Compact traceability ID and Code 39 barcode for each extraction run. PDF, Word, and layout-preserving document exports place the ID/barcode once at the bottom center so saved files can be matched back to the SQLite run.
- Detailed offline user guide, SQLite database preview, application log viewer, scrollable emoji What's New page with the installed version number, and user-triggered update checker.
- Smooth wheel scrolling and polished scrollbars across tables, parsed text, help, logs, database preview, and changelog pages.
- First launch after a fresh install or update automatically shows the What's New changelog once for that installed version.
- GitHub releases publish one Windows ZIP package containing a portable WinUI app and a signed MSIX with certificate/install helper.
- SQLite storage through SQLAlchemy ORM for templates, runs, uploaded files, mappings, learned templates, extracted values, and timestamps.
- Save/load mapping templates.
- Export completed output to CSV, XLSX, Word, and PDF with traceability. Multi-table templates export all tables in one output document: CSV sections, Excel sheets, Word tables, or PDF table sections.
- Export back into DOCX, XLSX, and CSV templates while preserving the original file layout as much as those formats allow. DOCX/XLSX preserved exports fill all supported template tables, only blank/template cells, keeping headings, logos, merged table structure, split rows/columns, and approved/rejected signature areas intact.
- Export filled PDF templates with original PDF page artwork preserved and values overlaid into detected blank table cells when coordinates are available.
- Single GitHub release ZIP with both portable WinUI launch and signed MSIX install options.

## Windows Setup

1. Install Python 3.11 or 3.12.
2. Install Tesseract OCR for Windows.
   - Recommended install path: `C:\Program Files\Tesseract-OCR\tesseract.exe`
   - Install English language data at minimum.
3. Create a virtual environment:

```powershell
python -m venv .venv
.\.venv\Scripts\Activate.ps1
pip install -r requirements.txt
pip install -e .
```

4. Run the application:

```powershell
python -m intellifill_ocr.main
```

If Tesseract is not on `PATH`, set `TESSERACT_CMD`:

```powershell
$env:TESSERACT_CMD="C:\Program Files\Tesseract-OCR\tesseract.exe"
python -m intellifill_ocr.main
```

You can also set paths inside the app:

1. Open **Settings** in the WinUI shell.
2. Select the local `tesseract.exe` path, for example `C:\Program Files\Tesseract-OCR\tesseract.exe`.
3. Select or create the SQLite database path, for example `%LOCALAPPDATA%\IntelliFillOCR\intellifill.sqlite3`.
4. Choose dark or light appearance from the same Settings window.
5. Save. The app writes these settings to `%LOCALAPPDATA%\IntelliFillOCR\settings.json`.

## Windows Package

The Windows release is native WinUI-only. It does not bundle the old Qt UI, an Inno installer, or a Python IPC backend.

## GitHub Release Pipeline

The repository includes a GitHub Actions workflow at `.github/workflows/release.yml`.

It builds one Windows package ZIP:

```text
IntelliFillOCR-3.2.0-windows-x64-package.zip
```

The ZIP contains:

```text
Portable\IntelliFillOCR.exe
Portable\InstallOrUpdate.cmd
MSIX\IntelliFillOCR_3.2.0.0_x64.msix
MSIX\IntelliFillOCR_MSIX_SigningCert.cer
MSIX\Install-IntelliFillOCR-MSIX.cmd
```

To publish version `3.2.0` manually:

1. Open the GitHub repository.
2. Go to **Actions**.
3. Select **WinUI Release Package**.
4. Click **Run workflow**.
5. Keep version `3.2.0` and run it.

You can also publish by pushing a tag:

```powershell
git tag v3.2.0
git push origin v3.2.0
```

## Build WinUI 3 Frontend

The v3 Windows migration uses a native WinUI 3 shell in `winui\IntelliFillOCR.WinUI`.
The Windows package is native WinUI-only.

Install the Windows App SDK/WinUI tooling, then run:

```powershell
.\scripts\build-winui.ps1
```

The build output is created under:

```text
winui\IntelliFillOCR.WinUI\bin\x64\Release\net8.0-windows10.0.19041.0\win-x64\publish
```

Create the portable WinUI package:

```powershell
.\scripts\package-winui.ps1 -Version 3.2.0
```

## Build MSIX Installer

MSIX packages must be built and signed on Windows. Install the Windows 10/11 SDK first so `MakeAppx.exe` and `SignTool.exe` are available.

Build and sign a WinUI MSIX with a self-signed certificate:

```powershell
.\msix\build-msix.ps1
```

The package and install helpers are created under `msix\out`:

```text
msix\out\IntelliFillOCR_3.2.0.0_x64.msix
msix\out\IntelliFillOCR_MSIX_SigningCert.cer
msix\out\Install-IntelliFillOCR-MSIX.cmd
```

For local/offline installation, run:

```powershell
.\msix\out\Install-IntelliFillOCR-MSIX.cmd
```

For production distribution, sign the MSIX with a trusted certificate whose subject matches the manifest publisher. The default publisher is `CN=IntelliFillOCR`; override it if needed:

```powershell
.\msix\build-msix.ps1 `
  -Publisher "CN=Your Company Name" `
  -PublisherDisplayName "Your Company Name" `
  -CertificatePath .\certs\your-code-signing-cert.pfx `
  -CertificatePassword "YourPfxPassword"
```

The MSIX contains the WinUI app only and remains fully offline. `MakeAppx.exe` creates the package and `SignTool.exe` signs it; both are part of the Windows SDK.

## Create Single Offline ZIP

The release workflow creates one ZIP for offline machines:

```text
release\IntelliFillOCR-3.2.0-windows-x64-package.zip
```

That ZIP includes:

```text
Portable\IntelliFillOCR.exe
Portable\InstallOrUpdate.cmd
MSIX\IntelliFillOCR_3.2.0.0_x64.msix
MSIX\IntelliFillOCR_MSIX_SigningCert.cer
MSIX\Install-IntelliFillOCR-MSIX.cmd
```

Use `Portable\InstallOrUpdate.cmd` for a normal current-user install/update, or `MSIX\Install-IntelliFillOCR-MSIX.cmd` when you want the signed MSIX route.

## Project Structure

```text
src/intellifill_ocr/
  database/       SQLAlchemy models and repository
  models/         App dataclasses
  ocr/            Tesseract, OpenCV, PDF OCR, table detection
  services/       Document parsing, templates, matching, mapping, export
  ui/             Legacy PySide6 screens kept for non-Windows and migration reference
  winui/          Native Windows App SDK / WinUI 3 shell
  utils/          Paths, config, exceptions, logging
demo/             Small offline demo fixtures
```

## Offline Notes

The app does not call cloud APIs. OCR, parsing, matching, database storage, and export all run locally. Tesseract language packs must be installed locally for each OCR language you want to use.

## Demo

Use `demo/template_invoice.csv` as a template and `demo/source_invoice.csv` as source data. Run `python demo/create_demo_files.py` after installing requirements to generate optional DOCX/XLSX demo files.

