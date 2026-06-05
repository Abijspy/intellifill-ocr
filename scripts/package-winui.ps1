param(
    [string]$Version = "3.2.0",
    [string]$Configuration = "Release",
    [string]$Platform = "x64",
    [string]$RuntimeIdentifier = "win-x64",
    [string]$WinUiProjectDir = "winui\IntelliFillOCR.WinUI",
    [string]$WinUiExeName = "IntelliFillOCR.exe",
    [string]$OutputDir = "release",
    [string]$PackageName = ""
)

$ErrorActionPreference = "Stop"

$Root = Resolve-Path (Join-Path $PSScriptRoot "..")
$WinUiOutputCandidates = @(
    (Join-Path $Root "$WinUiProjectDir\bin\$Platform\$Configuration\net8.0-windows10.0.19041.0\$RuntimeIdentifier\publish"),
    (Join-Path $Root "$WinUiProjectDir\bin\$Configuration\net8.0-windows10.0.19041.0\$RuntimeIdentifier\publish"),
    (Join-Path $Root "$WinUiProjectDir\bin\$Platform\$Configuration\net8.0-windows10.0.19041.0\$RuntimeIdentifier"),
    (Join-Path $Root "$WinUiProjectDir\bin\$Configuration\net8.0-windows10.0.19041.0\$RuntimeIdentifier")
)
$WinUiOutput = $WinUiOutputCandidates | Where-Object { Test-Path (Join-Path $_ $WinUiExeName) } | Select-Object -First 1
$ResolvedOutput = Join-Path $Root $OutputDir
if (-not $PackageName) {
    $PackageName = "IntelliFillOCR-$Version-winui-win-x64"
}
$StagingRoot = Join-Path $ResolvedOutput "winui-staging"
$PackageRoot = Join-Path $StagingRoot $PackageName
$ArchivePath = Join-Path $ResolvedOutput "$PackageName.zip"

if (-not $WinUiOutput -or -not (Test-Path -LiteralPath $WinUiOutput)) {
    throw "WinUI output was not found at $WinUiOutput. Run scripts\build-winui.ps1 first."
}

if (-not (Test-Path (Join-Path $WinUiOutput $WinUiExeName))) {
    throw "WinUI executable was not found in $WinUiOutput."
}

New-Item -ItemType Directory -Path $ResolvedOutput -Force | Out-Null
if (Test-Path $StagingRoot) {
    Remove-Item -LiteralPath $StagingRoot -Recurse -Force
}
if (Test-Path $ArchivePath) {
    Remove-Item -LiteralPath $ArchivePath -Force
}

New-Item -ItemType Directory -Path $PackageRoot -Force | Out-Null
Copy-Item -Path (Join-Path $WinUiOutput "*") -Destination $PackageRoot -Recurse -Force
@"
param()
`$ErrorActionPreference = "Stop"
`$source = Split-Path -Parent `$MyInvocation.MyCommand.Path
`$target = Join-Path `$env:LOCALAPPDATA "Programs\IntelliFill OCR"
`$shortcutDir = Join-Path `$env:APPDATA "Microsoft\Windows\Start Menu\Programs"
`$shortcutPath = Join-Path `$shortcutDir "IntelliFill OCR.lnk"

Get-Process IntelliFillOCR -ErrorAction SilentlyContinue | Stop-Process -Force
if (Test-Path `$target) {
    Remove-Item -LiteralPath `$target -Recurse -Force
}
New-Item -ItemType Directory -Path `$target -Force | Out-Null
Copy-Item -Path (Join-Path `$source "*") -Destination `$target -Recurse -Force -Exclude "InstallOrUpdate-IntelliFillOCR.ps1","InstallOrUpdate.cmd"

`$shell = New-Object -ComObject WScript.Shell
`$shortcut = `$shell.CreateShortcut(`$shortcutPath)
`$shortcut.TargetPath = Join-Path `$target "IntelliFillOCR.exe"
`$shortcut.WorkingDirectory = `$target
`$shortcut.IconLocation = Join-Path `$target "IntelliFillOCR.exe"
`$shortcut.Save()
Write-Host "Installed/updated IntelliFill OCR at `$target"
"@ | Set-Content -Path (Join-Path $PackageRoot "InstallOrUpdate-IntelliFillOCR.ps1") -Encoding UTF8

@"
@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0InstallOrUpdate-IntelliFillOCR.ps1"
pause
"@ | Set-Content -Path (Join-Path $PackageRoot "InstallOrUpdate.cmd") -Encoding ASCII

@(
    "IntelliFill OCR WinUI package $Version",
    "",
    "Run IntelliFillOCR.exe to open the native WinUI 3 shell.",
    "Run InstallOrUpdate.cmd to install or update the portable package for the current user.",
    "This Windows package is native WinUI only and does not bundle or launch a Python IPC backend."
) | Set-Content -Path (Join-Path $PackageRoot "README.txt") -Encoding UTF8

Compress-Archive -Path $PackageRoot -DestinationPath $ArchivePath -CompressionLevel Optimal
Write-Host "WinUI package created: $ArchivePath"
