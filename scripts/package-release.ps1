param(
    [string]$Version = "3.2.0",
    [string]$Configuration = "Release",
    [string]$Platform = "x64",
    [string]$RuntimeIdentifier = "win-x64",
    [string]$OutputDir = "release",
    [switch]$SkipWinUiBuild
)

$ErrorActionPreference = "Stop"

$AppName = "IntelliFillOCR"
$Root = Resolve-Path (Join-Path $PSScriptRoot "..")
$OutputPath = Join-Path $Root $OutputDir
$PortableName = "$AppName-$Version-portable-win-x64"
$PortableRoot = Join-Path $OutputPath "winui-staging\$PortableName"
$BundleName = "$AppName-$Version-windows-x64-package"
$BundleRoot = Join-Path $OutputPath "bundle\$BundleName"
$ArchivePath = Join-Path $OutputPath "$BundleName.zip"

if (-not $SkipWinUiBuild) {
    & (Join-Path $Root "scripts\build-winui.ps1") `
        -Configuration $Configuration `
        -Platform $Platform `
        -RuntimeIdentifier $RuntimeIdentifier
}

& (Join-Path $Root "scripts\package-winui.ps1") `
    -Version $Version `
    -Configuration $Configuration `
    -Platform $Platform `
    -RuntimeIdentifier $RuntimeIdentifier `
    -PackageName $PortableName

$MsixVersion = "$Version.0"
if ($Version -match '^\d+\.\d+\.\d+\.\d+$') {
    $MsixVersion = $Version
}

& (Join-Path $Root "msix\build-msix.ps1") `
    -Version $MsixVersion `
    -Architecture $Platform `
    -Configuration $Configuration `
    -RuntimeIdentifier $RuntimeIdentifier `
    -SkipWinUiBuild `
    -CreateSelfSignedCertificate

$MsixFile = Get-ChildItem -LiteralPath (Join-Path $Root "msix\out") -Filter "IntelliFillOCR_${MsixVersion}_${Platform}.msix" |
    Select-Object -First 1
$CertFile = Get-ChildItem -LiteralPath (Join-Path $Root "msix\out") -Filter "IntelliFillOCR_MSIX_SigningCert.cer" |
    Select-Object -First 1

if (-not (Test-Path (Join-Path $PortableRoot "IntelliFillOCR.exe"))) {
    throw "Portable WinUI package was not found at $PortableRoot."
}
if (-not $MsixFile) {
    throw "Signed MSIX was not found in msix\out."
}
if (-not $CertFile) {
    throw "MSIX public certificate was not found in msix\out."
}

if (Test-Path $BundleRoot) {
    Remove-Item -LiteralPath $BundleRoot -Recurse -Force
}
if (Test-Path $ArchivePath) {
    Remove-Item -LiteralPath $ArchivePath -Force
}

New-Item -ItemType Directory -Path (Join-Path $BundleRoot "Portable") -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $BundleRoot "MSIX") -Force | Out-Null

Copy-Item -Path (Join-Path $PortableRoot "*") -Destination (Join-Path $BundleRoot "Portable") -Recurse -Force
Copy-Item -LiteralPath $MsixFile.FullName -Destination (Join-Path $BundleRoot "MSIX") -Force
Copy-Item -LiteralPath $CertFile.FullName -Destination (Join-Path $BundleRoot "MSIX") -Force
Copy-Item -LiteralPath (Join-Path $Root "msix\out\Install-IntelliFillOCR-MSIX.ps1") -Destination (Join-Path $BundleRoot "MSIX") -Force
Copy-Item -LiteralPath (Join-Path $Root "msix\out\Install-IntelliFillOCR-MSIX.cmd") -Destination (Join-Path $BundleRoot "MSIX") -Force

@(
    "IntelliFill OCR $Version Windows package",
    "",
    "Portable:",
    "- Open Portable\IntelliFillOCR.exe directly, or run Portable\InstallOrUpdate.cmd to install/update for the current user.",
    "",
    "MSIX:",
    "- Open MSIX\Install-IntelliFillOCR-MSIX.cmd to trust the included signing certificate and install/update the signed MSIX.",
    "- The MSIX is signed with the included IntelliFillOCR_MSIX_SigningCert.cer certificate.",
    "",
    "This package contains the native WinUI app only. It does not include the old Qt UI, Python IPC backend, or Inno installer."
) | Set-Content -Path (Join-Path $BundleRoot "README.txt") -Encoding UTF8

Compress-Archive -Path $BundleRoot -DestinationPath $ArchivePath -CompressionLevel Optimal
Write-Host "Single Windows package created: $ArchivePath"
