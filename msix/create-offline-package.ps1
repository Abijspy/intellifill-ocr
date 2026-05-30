[CmdletBinding()]
param(
    [string]$Version = "2.3.1.0",
    [ValidateSet("x64", "x86", "arm64", "neutral")]
    [string]$Architecture = "x64",
    [string]$OutputRoot = "offline-dist",
    [string]$PackageFolderName = "IntelliFillOCR-offline",
    [string]$DistAppPath = ""
)

$ErrorActionPreference = "Stop"

$RepoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$OutputDir = Join-Path $RepoRoot $OutputRoot
$PackageDir = Join-Path $OutputDir $PackageFolderName
$PortableDir = Join-Path $PackageDir "portable\IntelliFillOCR"
$ZipPath = Join-Path $OutputDir "$PackageFolderName.zip"
$MsixPath = Join-Path $RepoRoot "msix\out\IntelliFillOCR_${Version}_${Architecture}.msix"
$PfxPath = Join-Path $RepoRoot "msix\out\IntelliFillOCR_SigningCert.pfx"
$CerPath = Join-Path $RepoRoot "msix\out\IntelliFillOCR_SigningCert.cer"
$InstallScript = Join-Path $RepoRoot "msix\install-msix.ps1"
$PackageReadme = Join-Path $RepoRoot "msix\offline-package-readme.txt"
$DistApp = if ($DistAppPath) { (Resolve-Path $DistAppPath).Path } else { Join-Path $RepoRoot "dist\IntelliFillOCR" }

foreach ($required in @($MsixPath, $PfxPath, $CerPath, $InstallScript, $PackageReadme, (Join-Path $DistApp "IntelliFillOCR.exe"))) {
    if (-not (Test-Path $required)) {
        throw "Required package input was not found: $required"
    }
}

New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
if (Test-Path $PackageDir) {
    Remove-Item -LiteralPath $PackageDir -Recurse -Force
}
if (Test-Path $ZipPath) {
    Remove-Item -LiteralPath $ZipPath -Force
}

New-Item -ItemType Directory -Path $PackageDir -Force | Out-Null
New-Item -ItemType Directory -Path $PortableDir -Force | Out-Null

Copy-Item -LiteralPath $MsixPath -Destination $PackageDir
Copy-Item -LiteralPath $PfxPath -Destination $PackageDir
Copy-Item -LiteralPath $CerPath -Destination $PackageDir
Copy-Item -LiteralPath $InstallScript -Destination $PackageDir
Copy-Item -LiteralPath $PackageReadme -Destination (Join-Path $PackageDir "README.txt")
Copy-Item -Path (Join-Path $DistApp "*") -Destination $PortableDir -Recurse

Compress-Archive -Path $PackageDir -DestinationPath $ZipPath -CompressionLevel Optimal -Force

Write-Host "Offline package folder: $PackageDir"
Write-Host "Offline package zip: $ZipPath"
