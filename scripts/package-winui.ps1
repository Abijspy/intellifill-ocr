param(
    [string]$Version = "3.0.1",
    [string]$Configuration = "Release",
    [string]$Platform = "x64",
    [string]$RuntimeIdentifier = "win-x64",
    [string]$WinUiProjectDir = "winui\IntelliFillOCR.WinUI",
    [string]$BackendDistDir = "dist\IntelliFillOCR",
    [string]$OutputDir = "release"
)

$ErrorActionPreference = "Stop"

$Root = Resolve-Path (Join-Path $PSScriptRoot "..")
$WinUiOutput = Join-Path $Root "$WinUiProjectDir\bin\$Platform\$Configuration\net8.0-windows10.0.19041.0\$RuntimeIdentifier"
$BackendDist = Join-Path $Root $BackendDistDir
$ResolvedOutput = Join-Path $Root $OutputDir
$PackageName = "IntelliFillOCR-WinUI-$Version-win-x64"
$StagingRoot = Join-Path $ResolvedOutput "winui-staging"
$PackageRoot = Join-Path $StagingRoot $PackageName
$ArchivePath = Join-Path $ResolvedOutput "$PackageName.zip"

if (-not (Test-Path $WinUiOutput)) {
    throw "WinUI output was not found at $WinUiOutput. Run scripts\build-winui.ps1 first."
}

if (-not (Test-Path (Join-Path $WinUiOutput "IntelliFillOCR.WinUI.exe"))) {
    throw "WinUI executable was not found in $WinUiOutput."
}

if (-not (Test-Path $BackendDist)) {
    throw "Python backend dist was not found at $BackendDist. Run the PyInstaller build first."
}

if (-not (Test-Path (Join-Path $BackendDist "IntelliFillOCR.exe"))) {
    throw "Python backend executable was not found in $BackendDist."
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

$BackendTarget = Join-Path $PackageRoot "Backend"
New-Item -ItemType Directory -Path $BackendTarget -Force | Out-Null
Copy-Item -Path (Join-Path $BackendDist "*") -Destination $BackendTarget -Recurse -Force

@(
    "IntelliFill OCR WinUI package $Version",
    "",
    "Run IntelliFillOCR.WinUI.exe to open the native WinUI 3 shell.",
    "The current Python OCR workspace is bundled in the Backend folder.",
    "Tesseract OCR must still be installed locally or configured from the OCR workspace settings."
) | Set-Content -Path (Join-Path $PackageRoot "README.txt") -Encoding UTF8

Compress-Archive -Path $PackageRoot -DestinationPath $ArchivePath -CompressionLevel Optimal
Write-Host "WinUI package created: $ArchivePath"
