param(
    [string]$Version = "3.3.0",
    [string]$Configuration = "Release",
    [string]$Platform = "x64",
    [string]$RuntimeIdentifier = "win-x64",
    [string]$OutputDir = "release",
    [switch]$SkipWinUiBuild
)

$ErrorActionPreference = "Stop"

& (Join-Path $PSScriptRoot "package-portable-exe.ps1") `
    -Version $Version `
    -Configuration $Configuration `
    -Platform $Platform `
    -RuntimeIdentifier $RuntimeIdentifier `
    -OutputDir $OutputDir `
    -SkipWinUiBuild:$SkipWinUiBuild
