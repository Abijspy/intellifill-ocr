param(
    [string]$Version = "3.3.0"
)

$ErrorActionPreference = "Stop"

.\scripts\package-portable-exe.ps1 -Version $Version

Write-Host "Built release\IntelliFillOCR-$Version-portable-win-x64.exe"
