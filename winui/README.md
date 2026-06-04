# IntelliFill OCR WinUI 3 Frontend

This folder contains the native Windows App SDK / WinUI 3 frontend for the v3 migration.

The native WinUI shell is the default Windows frontend. The existing Python OCR, parsing, SQLite, validation, and export services run as a local JSON IPC backend from either:

- `Backend/IntelliFillOCR.exe --ipc` in packaged builds.
- `.venv/Scripts/python.exe -m intellifill_ocr.main --ipc` during source development.

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
2. Add a JSON/IPC backend boundary for template upload, source upload, OCR extraction, mapping, validation, and export commands. ✅
3. Replace the remaining workflow screens with native WinUI pages one workflow at a time. Template upload is now native.
4. Move the Windows installer to package the WinUI shell plus Python backend. ✅

## JSON IPC Backend

The Python executable supports a native backend mode:

```powershell
python -m intellifill_ocr.main --ipc
```

The packaged backend supports the same flag:

```powershell
IntelliFillOCR.exe --ipc
```

IPC uses newline-delimited JSON over stdin/stdout. Each request is:

```json
{"id":"1","command":"system.ping","params":{}}
```

Each response is:

```json
{"id":"1","ok":true,"result":{}}
```

Errors return:

```json
{"id":"1","ok":false,"error":{"type":"ValueError","message":"Upload a template first."}}
```

Current commands:

- `system.ping`: returns version, Tesseract path, SQLite path, language, and backend capabilities.
- `state.get`: returns the current template, sources, fields, mappings, run id, and traceability code.
- `state.reset`: clears the current in-memory workflow.
- `template.upload`: params `{ "path": "C:\\path\\template.docx" }`.
- `template.set_cell`: params `{ "table_index": 0, "row": 1, "column": 2, "value": "Filled value" }`.
- `source.upload`: params `{ "paths": ["C:\\path\\source.pdf"] }`, up to five source files.
- `ocr.extract`: params `{ "source_id": 0, "region": { "x": 10, "y": 20, "width": 220, "height": 80 } }`.
- `mapping.suggest`: returns fuzzy/keyword mapping suggestions.
- `mapping.apply`: applies mappings into the template preview.
- `validation.run`: returns validation issues for required, regex/date/amount, duplicate, and total mismatch rules.
- `database.save`: saves completed values to SQLite.
- `export.create`: params `{ "format": "pdf", "output_path": "C:\\out\\filled.pdf" }`.

The WinUI shell includes a **Check backend** button that starts the Python backend in `--ipc` mode, sends `system.ping`, and shows the connection result. Template upload also runs natively through this IPC session.
