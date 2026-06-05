param(
    [string]$Version = "3.3.0",
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
$WinUiProjectDir = Join-Path $Root "winui\IntelliFillOCR.WinUI"
$InstallerProject = Join-Path $Root "packaging\PortableInstaller\IntelliFillOCR.PortableInstaller.csproj"
$PayloadDir = Join-Path $Root "packaging\PortableInstaller\Payload"
$PayloadZip = Join-Path $PayloadDir "IntelliFillOCR-portable.zip"
$StagingRoot = Join-Path $OutputPath "portable-exe-staging"
$PackageRoot = Join-Path $StagingRoot "$AppName-$Version-portable-win-x64"
$InstallerPublishDir = Join-Path $OutputPath "portable-installer-publish"
$FinalExe = Join-Path $OutputPath "$AppName-$Version-portable-win-x64.exe"
$AssemblyVersion = if ($Version.Split(".").Count -eq 4) { $Version } else { "$Version.0" }

if (-not $SkipWinUiBuild) {
    & (Join-Path $Root "scripts\build-winui.ps1") `
        -Configuration $Configuration `
        -Platform $Platform `
        -RuntimeIdentifier $RuntimeIdentifier
}

$WinUiOutputCandidates = @(
    (Join-Path $WinUiProjectDir "bin\$Platform\$Configuration\net8.0-windows10.0.19041.0\$RuntimeIdentifier\publish"),
    (Join-Path $WinUiProjectDir "bin\$Configuration\net8.0-windows10.0.19041.0\$RuntimeIdentifier\publish")
)
$WinUiOutput = $WinUiOutputCandidates | Where-Object { Test-Path (Join-Path $_ "IntelliFillOCR.exe") } | Select-Object -First 1
if (-not $WinUiOutput) {
    throw "WinUI publish output was not found. Run scripts\build-winui.ps1 first."
}

New-Item -ItemType Directory -Path $OutputPath -Force | Out-Null
if (Test-Path $StagingRoot) {
    Remove-Item -LiteralPath $StagingRoot -Recurse -Force
}
if (Test-Path $InstallerPublishDir) {
    Remove-Item -LiteralPath $InstallerPublishDir -Recurse -Force
}
if (Test-Path $FinalExe) {
    Remove-Item -LiteralPath $FinalExe -Force
}
if (Test-Path $PayloadZip) {
    Remove-Item -LiteralPath $PayloadZip -Force
}

New-Item -ItemType Directory -Path $PackageRoot -Force | Out-Null
New-Item -ItemType Directory -Path $PayloadDir -Force | Out-Null
Copy-Item -Path (Join-Path $WinUiOutput "*") -Destination $PackageRoot -Recurse -Force

@(
    "IntelliFill OCR $Version portable payload",
    "",
    "This folder is embedded inside IntelliFillOCR-$Version-portable-win-x64.exe.",
    "The portable updater extracts it to %LOCALAPPDATA%\Programs\IntelliFill OCR, replaces stale files, creates a Start Menu shortcut, and launches IntelliFillOCR.exe.",
    "Run the EXE again to update an existing portable install."
) | Set-Content -Path (Join-Path $PackageRoot "README.txt") -Encoding UTF8

Compress-Archive -Path $PackageRoot -DestinationPath $PayloadZip -CompressionLevel Optimal

$Dotnet = $env:DOTNET_EXE
if (-not $Dotnet) {
    $Dotnet = "C:\Program Files\dotnet\dotnet.exe"
}
if (-not (Test-Path $Dotnet)) {
    $DotnetCommand = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($DotnetCommand) {
        $Dotnet = $DotnetCommand.Source
    }
}
if (-not (Test-Path $Dotnet)) {
    throw "dotnet was not found. Install the .NET SDK or set DOTNET_EXE."
}

& $Dotnet publish $InstallerProject `
    -c $Configuration `
    -r $RuntimeIdentifier `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:Version=$Version `
    -p:AssemblyVersion=$AssemblyVersion `
    -p:FileVersion=$AssemblyVersion `
    -p:PublishDir="$InstallerPublishDir\"

if ($LASTEXITCODE -ne 0) {
    throw "Portable installer publish failed with exit code $LASTEXITCODE."
}

$BuiltExe = Join-Path $InstallerPublishDir "IntelliFillOCR-Portable.exe"
if (-not (Test-Path $BuiltExe)) {
    throw "Portable installer executable was not created: $BuiltExe"
}

Copy-Item -LiteralPath $BuiltExe -Destination $FinalExe -Force
Write-Host "Portable updater EXE created: $FinalExe"
