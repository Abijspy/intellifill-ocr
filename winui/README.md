# IntelliFill OCR WinUI 3 Frontend

This folder contains the native Windows App SDK / WinUI 3 frontend for the v3 migration.

The existing Python application remains the production OCR backend while the native screens are migrated. The WinUI shell can launch the current Python OCR workspace from either:

- `dist/IntelliFillOCR/IntelliFillOCR.exe` after a PyInstaller build.
- `.venv/Scripts/python.exe -m intellifill_ocr.main` during source development.

## Build

```powershell
.\scripts\build-winui.ps1
```

The WinUI project uses:

- .NET 8 Windows target framework.
- Microsoft Windows App SDK 2.1.
- Mica backdrop and WinUI NavigationView shell.

## Migration Plan

1. Keep the Python OCR, parsing, database, export, and validation services stable.
2. Add a JSON/IPC backend boundary for template upload, source upload, OCR extraction, mapping, validation, and export commands.
3. Replace the Qt screens with native WinUI pages one workflow at a time.
4. Move the Windows installer to package the WinUI shell plus Python backend.
