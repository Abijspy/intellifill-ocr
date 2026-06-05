# IntelliFill OCR WinUI 3 Frontend

This folder contains the native Windows App SDK / WinUI 3 frontend.

The Windows package is native WinUI-only. It launches from `IntelliFillOCR.exe` and does not use the old Qt UI, Python IPC backend, or Inno installer.

## Build

```powershell
.\scripts\build-winui.ps1
```

The WinUI project uses:

- .NET 8 Windows target framework.
- Microsoft Windows App SDK 2.1.
- Mica backdrop and WinUI NavigationView shell.

## Package

Create the portable WinUI package:

```powershell
.\scripts\package-winui.ps1 -Version 3.2.0
```

Build and sign the MSIX:

```powershell
.\msix\build-msix.ps1 -Version 3.2.0.0
```

The GitHub release workflow builds one ZIP asset containing:

- `Portable\IntelliFillOCR.exe`
- `Portable\InstallOrUpdate.cmd`
- `MSIX\IntelliFillOCR_3.2.0.0_x64.msix`
- `MSIX\IntelliFillOCR_MSIX_SigningCert.cer`
- `MSIX\Install-IntelliFillOCR-MSIX.cmd`

## Native Preview Support

The WinUI shell currently parses CSV, TXT, XLSX, and DOCX tables directly in C# for template/source preview. PDF and image files are accepted as imported documents while native OCR/PDF table extraction is migrated into the WinUI engine.
