param(
    [string]$Version = "3.7.2",
    [string]$Configuration = "Release",
    [string]$RuntimeIdentifier = "win-x64",
    [string]$OutputDir = "release",
    [switch]$SkipAvaloniaBuild
)

$ErrorActionPreference = "Stop"

& (Join-Path $PSScriptRoot "package-nsis.ps1") `
    -Version $Version `
    -Configuration $Configuration `
    -RuntimeIdentifier $RuntimeIdentifier `
    -OutputDir $OutputDir `
    -SkipAvaloniaBuild:$SkipAvaloniaBuild
