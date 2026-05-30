param(
    [string]$Version = "2.3.1",
    [string]$DistDir = "dist",
    [string]$OutputDir = "release"
)

$ErrorActionPreference = "Stop"

$AppName = "IntelliFillOCR"
$Root = Resolve-Path (Join-Path $PSScriptRoot "..")
$DistPath = Join-Path $Root $DistDir
$AppDistPath = Join-Path $DistPath $AppName
$OutputPath = Join-Path $Root $OutputDir
$PackageName = "$AppName-$Version-win-x64"
$PackagePath = Join-Path $OutputPath $PackageName
$ZipPath = Join-Path $OutputPath "$PackageName.zip"

if (-not (Test-Path $AppDistPath)) {
    throw "PyInstaller output was not found at $AppDistPath. Run the build first."
}

if (-not (Test-Path $OutputPath)) {
    New-Item -ItemType Directory -Path $OutputPath | Out-Null
}

if (Test-Path $PackagePath) {
    Remove-Item -LiteralPath $PackagePath -Recurse -Force
}

New-Item -ItemType Directory -Path $PackagePath | Out-Null
Copy-Item -Path (Join-Path $AppDistPath "*") -Destination $PackagePath -Recurse -Force

$InstallNotes = @"
IntelliFill OCR Desktop $Version

Offline Windows package

How to run
1. Extract this zip on a Windows 10/11 machine.
2. Run IntelliFillOCR.exe.
3. Open Settings and select the local Tesseract OCR executable path if it is not detected automatically.
4. Choose or create the SQLite database path from Settings.

Notes
- The application runs fully offline.
- Tesseract OCR must be installed locally on the target computer for OCR features.
- Source and template upload supports Word, Excel, CSV, images, and PDF files.
- Exports include CSV, Excel, PDF, Word, and preserved-layout document output where supported.
- Traceability barcodes are compact and are placed at the bottom center of PDF/Word exports.
- Taskbar pins use the new application icon after installing 2.0.1 and re-pinning from the updated shortcut.
- Closed panels can be restored or hidden from Actions > Panels.
- Learned templates, validation checks, signature/stamp detection, and Windows scanner import are available from Actions.
- User Guide and full scrollable changelog are available from Actions > Help.
- Large dialogs stay inside the visible screen, and dock panel close/float buttons use custom high-contrast controls in light and dark mode.
- PDF traceability barcodes render as clear, scannable bottom-center barcode images instead of collapsed black strips.
- Scrollable tables, text previews, logs, help pages, and changelogs use smoother wheel scrolling and polished scrollbars.
"@

Set-Content -Path (Join-Path $PackagePath "INSTALL.txt") -Value $InstallNotes -Encoding UTF8

if (Test-Path $ZipPath) {
    Remove-Item -LiteralPath $ZipPath -Force
}

Compress-Archive -Path (Join-Path $PackagePath "*") -DestinationPath $ZipPath -Force

Write-Host "Release package created: $ZipPath"
