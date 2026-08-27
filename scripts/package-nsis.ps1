param(
    [string]$Version = "6.1.0",
    [string]$Configuration = "Release",
    [string]$RuntimeIdentifier = "win-x64",
    [string]$OutputDir = "release",
    [switch]$SkipAvaloniaBuild
)

$ErrorActionPreference = "Stop"

$Root = Resolve-Path (Join-Path $PSScriptRoot "..")
$OutputPath = Join-Path $Root $OutputDir
$PublishDir = Join-Path $OutputPath "avalonia-$RuntimeIdentifier\publish"
$CliProject = Join-Path $Root "src\IntelliFillOCR.Cli\IntelliFillOCR.Cli.csproj"
$CliPublishDir = Join-Path $PublishDir "cli"
$InstallerOutDir = Join-Path $Root "installer\out"
$Installer = Join-Path $InstallerOutDir "IntelliFillOCR-$Version-setup-$RuntimeIdentifier.exe"
$NsiScript = Join-Path $Root "installer\IntelliFillOCR.nsi"
$VersionParts = @($Version.Split("."))
if ($VersionParts.Count -gt 4 -or ($VersionParts | Where-Object { $_ -notmatch "^\d+$" })) {
    throw "Version must contain one to four numeric parts. Received: $Version"
}
$AppFileVersion = @($VersionParts + @("0", "0", "0", "0"))[0..3] -join "."

if (-not $SkipAvaloniaBuild) {
    & (Join-Path $Root "scripts\build-avalonia.ps1") `
        -Configuration $Configuration `
        -RuntimeIdentifier $RuntimeIdentifier `
        -OutputDir (Join-Path $OutputDir "avalonia-$RuntimeIdentifier\publish") `
        -Platform Windows

    dotnet publish $CliProject `
        -c $Configuration `
        -r $RuntimeIdentifier `
        --self-contained true `
        -p:PublishSingleFile=false `
        -p:PublishDir="$CliPublishDir\"
    if ($LASTEXITCODE -ne 0) {
        throw "CLI publish failed with exit code $LASTEXITCODE."
    }
}

if (-not (Test-Path (Join-Path $PublishDir "IntelliFillOCR.exe"))) {
    throw "Avalonia Windows publish output was not found: $PublishDir"
}
if (-not (Test-Path (Join-Path $CliPublishDir "intellifill.exe"))) {
    throw "CLI Windows publish output was not found: $CliPublishDir"
}

New-Item -ItemType Directory -Path $InstallerOutDir -Force | Out-Null
if (Test-Path $Installer) {
    Remove-Item -LiteralPath $Installer -Force
}

$Makensis = $env:MAKENSIS_EXE
if (-not $Makensis) {
    $command = Get-Command makensis -ErrorAction SilentlyContinue
    if ($command) {
        $Makensis = $command.Source
    }
}
if (-not $Makensis) {
    $candidate = "${env:ProgramFiles(x86)}\NSIS\makensis.exe"
    if (Test-Path $candidate) {
        $Makensis = $candidate
    }
}
if (-not $Makensis) {
    $candidate = "${env:ProgramFiles}\NSIS\makensis.exe"
    if (Test-Path $candidate) {
        $Makensis = $candidate
    }
}
if (-not $Makensis -or -not (Test-Path $Makensis)) {
    throw "makensis.exe was not found. Install NSIS or set MAKENSIS_EXE to the full makensis.exe path."
}

& $Makensis `
    "/DAPP_VERSION=$Version" `
    "/DAPP_FILE_VERSION=$AppFileVersion" `
    "/DPUBLISH_DIR=$PublishDir" `
    "/DOUTPUT_EXE=$Installer" `
    "/DAPP_RUNTIME=$RuntimeIdentifier" `
    $NsiScript

if ($LASTEXITCODE -ne 0) {
    throw "NSIS installer build failed with exit code $LASTEXITCODE."
}

if (-not (Test-Path $Installer)) {
    throw "NSIS installer was not created: $Installer"
}

Write-Host "NSIS installer created: $Installer"
