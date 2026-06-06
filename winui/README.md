# IntelliFill OCR WinUI Legacy

This folder contains the previous Windows App SDK / WinUI 3 implementation.

The active release path has moved to:

```text
src/IntelliFillOCR.Avalonia/
```

Windows releases are now built with NSIS from the Avalonia publish output:

```powershell
.\scripts\package-release.ps1 -Version 3.4.0 -RuntimeIdentifier win-x64
```

The WinUI project is kept only as migration reference while remaining features are ported to Avalonia.
