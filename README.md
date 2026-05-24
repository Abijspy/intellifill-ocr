<p align="center">
  <img src="assets/logo_512.png" alt="AutoFill & Export logo" width="220">
</p>

# IntelliFill OCR Desktop

IntelliFill OCR Desktop is a fully offline Windows/Linux(Ubuntu/debian/Fedora/RHEL) desktop application for OCR-driven data extraction, visual field mapping, and table/form filling.

It supports template upload, source document upload, region-based OCR, fuzzy field matching, editable previews, SQLite persistence, traceability barcodes, and export to CSV/Excel/PDF or the original template format where supported.

## Features

- PySide6 desktop UI with dark/light themes, a single Actions workflow button, split panes, tabs, zoomable document preview, and editable table preview.
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
- Ubuntu/Debian/Fedora builds auto-detect local Tesseract from `PATH` and common locations such as `/usr/bin/tesseract`.
- Header-aware matching that fills blank cells beside or under template headings.
- Compact traceability ID and Code 39 barcode for each extraction run. PDF, Word, and layout-preserving document exports place the ID/barcode once at the bottom center so saved files can be matched back to the SQLite run.
- Detailed offline user guide, SQLite database preview, application log viewer, scrollable emoji What's New page with the installed version number, and user-triggered update checker.
- Smooth wheel scrolling and polished scrollbars across tables, parsed text, help, logs, database preview, and changelog pages.
- First launch after a fresh install or update automatically shows the What's New changelog once for that installed version.
- Check for Updates downloads the matching Windows installer or Linux `.deb`/`.rpm` package. Linux updates show the terminal install command because package installation needs local admin privileges.
- Windows installer shortcuts and runtime app identity are configured so taskbar pins use the current application icon.
- SQLite storage through SQLAlchemy ORM for templates, runs, uploaded files, mappings, learned templates, extracted values, and timestamps.
- Save/load mapping templates.
- Export completed output to CSV, XLSX, Word, and PDF with traceability. Multi-table templates export all tables in one output document: CSV sections, Excel sheets, Word tables, or PDF table sections.
- Export back into DOCX, XLSX, and CSV templates while preserving the original file layout as much as those formats allow. DOCX/XLSX preserved exports fill all supported template tables, only blank/template cells, keeping headings, logos, merged table structure, split rows/columns, and approved/rejected signature areas intact.
- Export filled PDF templates with original PDF page artwork preserved and values overlaid into detected blank table cells when coordinates are available.
- PyInstaller packaging script for a standalone Windows `.exe`.
- MSIX installer packaging scripts for Windows deployment.

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

1. Open **Actions > Settings**.
2. Select the local `tesseract.exe` path, for example `C:\Program Files\Tesseract-OCR\tesseract.exe`.
3. Select or create the SQLite database path, for example `%LOCALAPPDATA%\IntelliFillOCR\intellifill.sqlite3`.
4. Choose dark or light appearance from the same Settings window.
5. Save. The app writes these settings to `%LOCALAPPDATA%\IntelliFillOCR\settings.json`.

## Linux Packages

The GitHub release workflow builds Linux packages for Debian/Ubuntu and Fedora/RPM systems. Install Tesseract locally first or let the app auto-detect it from `PATH`:

```bash
sudo apt install tesseract-ocr
sudo dnf install tesseract
```

The app UI and OCR workflow are the same PySide6 application as Windows. Windows-only scanner acquisition uses WIA drivers, so Linux users should import scanned images or PDFs as source files.

## Build Standalone EXE

```powershell
.\build.ps1
```

The executable will be produced under `dist\IntelliFillOCR\IntelliFillOCR.exe`.

## GitHub Release Pipeline

The repository includes a GitHub Actions workflow at `.github/workflows/release.yml`.

It builds the Windows x64 PyInstaller executable, packages it as:

```text
IntelliFillOCR-2.3.0-win-x64.zip
IntelliFillOCR-Setup-2.3.0-win-x64.exe
```

It also builds Linux packages in GitHub Actions only:

```text
IntelliFillOCR-2.3.0-linux-x64.deb
IntelliFillOCR-2.3.0-linux-x64.rpm
```

and publishes all release files to a GitHub release.

To publish version `2.3.0` manually:

1. Open the GitHub repository.
2. Go to **Actions**.
3. Select **CI/CD Release**.
4. Click **Run workflow**.
5. Keep version `2.3.0` and run it.

You can also publish by pushing a tag:

```powershell
git tag v2.3.0
git push origin v2.3.0
```

## Build Windows Installer

The project includes an Inno Setup installer definition at `installer\IntelliFillOCR.iss`.
Install Inno Setup 6 locally, build the PyInstaller exe, then run:

```powershell
.\scripts\build-installer.ps1 -Version 2.3.0
```

The installer is produced at:

```text
installer\out\IntelliFillOCR-Setup-2.3.0-win-x64.exe
```

## Build MSIX Installer

MSIX packages must be built and signed on Windows. Install the Windows 10/11 SDK first so `MakeAppx.exe` and `SignTool.exe` are available.

Build an unsigned MSIX:

```powershell
.\msix\build-msix.ps1
```

Build and sign with a local self-signed certificate:

```powershell
.\msix\build-msix.ps1 -CreateSelfSignedCertificate -CertificatePassword "ChangeThisPassword"
```

The package is created at:

```text
msix\out\IntelliFillOCR_2.3.0.0_x64.msix
```

For local installation of a self-signed package, trust the generated certificate and install the MSIX:

```powershell
.\msix\install-msix.ps1 `
  -MsixPath .\msix\out\IntelliFillOCR_2.3.0.0_x64.msix `
  -CertificatePath .\msix\out\IntelliFillOCR_SigningCert.pfx `
  -CertificatePassword "ChangeThisPassword"
```

For production distribution, sign the MSIX with a certificate whose subject matches the manifest publisher. The default publisher is `CN=IntelliFillOCR`; override it if needed:

```powershell
.\msix\build-msix.ps1 `
  -Publisher "CN=Your Company Name" `
  -PublisherDisplayName "Your Company Name" `
  -CertificatePath .\certs\your-code-signing-cert.pfx `
  -CertificatePassword "YourPfxPassword"
```

The MSIX includes the PyInstaller output and remains fully offline. Tesseract OCR still needs to be installed locally on the target Windows machine unless you choose to bundle a Tesseract distribution into the PyInstaller build. After install, open **Actions > Settings** to point the app to the offline machine's local `tesseract.exe` and SQLite database path.

This repository contains everything needed to build the MSIX, but the package itself must be produced on a Windows machine with Python and the Windows SDK installed. `MakeAppx.exe` creates the package and `SignTool.exe` signs it; both are part of the Windows SDK.

## Create Offline ZIP

After building the EXE and signed MSIX, create one folder/zip that can be copied to offline machines:

```powershell
.\msix\create-offline-package.ps1
```

The zip is created at:

```text
offline-dist\IntelliFillOCR-offline.zip
```

## Project Structure

```text
src/intellifill_ocr/
  database/       SQLAlchemy models and repository
  models/         App dataclasses
  ocr/            Tesseract, OpenCV, PDF OCR, table detection
  services/       Document parsing, templates, matching, mapping, export
  ui/             PySide6 windows and widgets
  utils/          Paths, config, exceptions, logging
demo/             Small offline demo fixtures
```

## Offline Notes

The app does not call cloud APIs. OCR, parsing, matching, database storage, and export all run locally. Tesseract language packs must be installed locally for each OCR language you want to use.

## Demo

Use `demo/template_invoice.csv` as a template and `demo/source_invoice.csv` as source data. Run `python demo/create_demo_files.py` after installing requirements to generate optional DOCX/XLSX demo files.

