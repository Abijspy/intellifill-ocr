param(
    [string]$Version = "3.1.1",
    [string]$Configuration = "Release",
    [string]$Platform = "x64",
    [string]$RuntimeIdentifier = "win-x64",
    [string]$WinUiProjectDir = "winui\IntelliFillOCR.WinUI",
    [string]$BackendDistDir = "dist\IntelliFillOCRBackend",
    [string]$WinUiExeName = "IntelliFillOCR.exe",
    [string]$BackendExeName = "IntelliFillOCRBackend.exe",
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
$BackendDist = Join-Path $Root $BackendDistDir
$ResolvedOutput = Join-Path $Root $OutputDir
if (-not $PackageName) {
    $PackageName = "IntelliFillOCR-WinUI-$Version-win-x64"
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

if (-not (Test-Path $BackendDist)) {
    throw "Python backend dist was not found at $BackendDist. Run the PyInstaller build first."
}

if (-not (Test-Path (Join-Path $BackendDist $BackendExeName))) {
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
$compatLauncher = Join-Path $PackageRoot "IntelliFillOCR.WinUI.exe"
if (-not (Test-Path $compatLauncher)) {
    Copy-Item -LiteralPath (Join-Path $PackageRoot $WinUiExeName) -Destination $compatLauncher -Force
}

$BackendTarget = Join-Path $PackageRoot "Backend"
New-Item -ItemType Directory -Path $BackendTarget -Force | Out-Null
Copy-Item -Path (Join-Path $BackendDist "*") -Destination $BackendTarget -Recurse -Force

@(
    "IntelliFill OCR WinUI package $Version",
    "",
    "Run IntelliFillOCR.exe to open the native WinUI 3 shell.",
    "IntelliFillOCR.WinUI.exe is included only as a compatibility launcher for older v3.1.0 shortcuts.",
    "The Python OCR engine is bundled in the Backend folder as a local JSON IPC service.",
    "Tesseract OCR must still be installed locally or configured in the application settings."
) | Set-Content -Path (Join-Path $PackageRoot "README.txt") -Encoding UTF8

Compress-Archive -Path $PackageRoot -DestinationPath $ArchivePath -CompressionLevel Optimal
Write-Host "WinUI package created: $ArchivePath"
