param(
    [string]$Version = "4.1.0"
)

$ErrorActionPreference = "Stop"

.\scripts\package-release.ps1 -Version $Version -RuntimeIdentifier win-x64

Write-Host "Built installer\out\IntelliFillOCR-$Version-setup-win-x64.exe"
