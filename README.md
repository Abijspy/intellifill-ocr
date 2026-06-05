<p align="center">
  <img src="assets/logo_512.png" alt="AutoFill & Export logo" width="220">
</p>

# IntelliFill OCR Desktop

IntelliFill OCR is an offline Windows desktop application for document extraction, template table filling, validation, SQLite storage, and traceable exports.

The supported Windows app is now the native WinUI 3 build. GitHub releases publish one file only:

```text
IntelliFillOCR-<version>-portable-win-x64.exe
```

Run that EXE to install or update IntelliFill OCR for the current Windows user. It extracts a clean WinUI app payload to:

```text
%LOCALAPPDATA%\Programs\IntelliFill OCR
```

Running the same EXE again updates the app and removes stale files from older installs.

## Current Features

- Native WinUI 3 desktop shell with a single Actions button.
- Template upload for CSV, TXT, XLSX, DOCX, PDF, PNG, JPG, and JPEG.
- Multi-table template preview and output table selector.
- Source upload for up to five files with parsed text preview.
- Extracted field list and manual source-to-cell mapping.
- Auto Fill Matching Fields using local fuzzy matching.
- Editable output preview before saving.
- Saved mapping templates as JSON files.
- Template Learning with reusable mapping suggestions.
- Validation checks for required blanks, GST/GSTIN format, dates, amounts, duplicates, and invoice total mismatch.
- SQLite save and database preview.
- Application log viewer.
- Settings for Tesseract path, SQLite path, and light/dark/system appearance.
- Exports to CSV, XLSX, DOCX, and PDF.
- PDF exports include one bottom traceability barcode/code.
- In-app update checker that downloads and runs the latest portable updater EXE from GitHub releases.
- Fully local operation; no cloud APIs are called.

## Install

1. Download the latest `IntelliFillOCR-<version>-portable-win-x64.exe` from GitHub Releases.
2. Run it.
3. The app installs for the current user and opens automatically.

For offline machines, copy that same EXE to the machine and run it there.

## Build Locally

Requirements:

- Windows 10/11.
- .NET 8 SDK.
- Windows App SDK compatible build tools.

Build the single portable updater EXE:

```powershell
.\build.ps1 -Version 3.3.0
```

Output:

```text
release\IntelliFillOCR-3.3.0-portable-win-x64.exe
```

The build script publishes the WinUI app, embeds it as a payload inside the portable updater project, and produces one EXE release asset.

## GitHub Release Pipeline

The only release workflow is:

```text
.github/workflows/release.yml
```

It builds and attaches exactly one Windows asset:

```text
IntelliFillOCR-<version>-portable-win-x64.exe
```

Publish by pushing a tag:

```powershell
git tag v3.3.0
git push origin v3.3.0
```

Or run the workflow manually from GitHub Actions and enter the version.

## Repository Layout

```text
winui/IntelliFillOCR.WinUI/       Native WinUI application
packaging/PortableInstaller/      Single-file portable installer/updater EXE
scripts/                          Build, version, and packaging scripts
assets/                           App icon and logo
demo/                             Small CSV demo fixtures
src/                              Legacy Python/Qt reference code, not shipped in the WinUI release
```

## Notes

- The release EXE is the supported installer/update package.
- The old MSIX, Inno Setup, and PyInstaller release paths were removed from the workflow to avoid duplicate GitHub assets.
- Configure local Tesseract and SQLite paths from Actions > Settings inside the app.
