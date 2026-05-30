param(
    [string]$Version = "2.3.2",
    [string]$DistDir = "dist",
    [string]$OutputDir = "installer\out",
    [string]$InstallerScript = "installer\IntelliFillOCR.iss"
)

$ErrorActionPreference = "Stop"

$AppName = "IntelliFillOCR"
$Root = Resolve-Path (Join-Path $PSScriptRoot "..")
$DistPath = Join-Path $Root $DistDir
$AppDistPath = Join-Path $DistPath $AppName
$OutputPath = Join-Path $Root $OutputDir
$ScriptPath = Join-Path $Root $InstallerScript
$PrerequisitesPath = Join-Path (Split-Path -Parent $ScriptPath) "prerequisites.txt"
$IconPath = Join-Path $Root "assets\app.ico"
$InstallerFile = Join-Path $OutputPath "$AppName-Setup-$Version-win-x64.exe"

function Find-Iscc {
    if ($env:INNO_SETUP_ISCC -and (Test-Path $env:INNO_SETUP_ISCC)) {
        return (Resolve-Path $env:INNO_SETUP_ISCC).Path
    }

    $command = Get-Command iscc.exe -ErrorAction SilentlyContinue
    if ($command) {
        return $command.Source
    }

    $candidates = @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles(x86)}\Inno Setup 5\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 5\ISCC.exe"
    )

    foreach ($candidate in $candidates) {
        if ($candidate -and (Test-Path $candidate)) {
            return (Resolve-Path $candidate).Path
        }
    }

    throw "Inno Setup compiler was not found. Install Inno Setup 6 or set INNO_SETUP_ISCC to ISCC.exe."
}

if (-not (Test-Path $AppDistPath)) {
    throw "PyInstaller output was not found at $AppDistPath. Run the exe build first."
}

if (-not (Test-Path $ScriptPath)) {
    throw "Installer script was not found at $ScriptPath."
}

if (-not (Test-Path $PrerequisitesPath)) {
    throw "Installer prerequisite notice was not found at $PrerequisitesPath."
}

if (-not (Test-Path $IconPath)) {
    throw "Application icon was not found at $IconPath."
}

if (-not (Test-Path $OutputPath)) {
    New-Item -ItemType Directory -Path $OutputPath | Out-Null
}

if (Test-Path $InstallerFile) {
    Remove-Item -LiteralPath $InstallerFile -Force
}

$Iscc = Find-Iscc

& $Iscc `
    "/DAppVersion=$Version" `
    "/DSourceDir=$AppDistPath" `
    "/DOutputDir=$OutputPath" `
    "/DPrerequisitesFile=$PrerequisitesPath" `
    "/DIconFile=$IconPath" `
    $ScriptPath

if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup failed with exit code $LASTEXITCODE."
}

if (-not (Test-Path $InstallerFile)) {
    throw "Installer was not created at $InstallerFile."
}

Write-Host "Installer created: $InstallerFile"
