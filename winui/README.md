# IntelliFill OCR WinUI

This folder contains the supported native Windows App SDK / WinUI 3 application.

The release package is a single portable updater EXE generated from:

```powershell
.\scripts\package-portable-exe.ps1 -Version 3.3.0
```

The EXE embeds the WinUI publish output, installs or updates the app under the current user profile, creates a Start Menu shortcut, and launches `IntelliFillOCR.exe`.

## Build Only

```powershell
.\scripts\build-winui.ps1
```

The WinUI app uses:

- .NET 8 Windows target framework.
- Microsoft Windows App SDK.
- WinUI NavigationView shell.
- Native C# parsing, mapping, validation, SQLite save/preview, and export services.

## Release

Use the root build script:

```powershell
.\build.ps1 -Version 3.3.0
```

Output:

```text
release\IntelliFillOCR-3.3.0-portable-win-x64.exe
```
