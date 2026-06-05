param(
    [string]$Configuration = "Release",
    [string]$Platform = "x64",
    [string]$RuntimeIdentifier = "win-x64",
    [string]$ProjectPath = "winui\IntelliFillOCR.WinUI\IntelliFillOCR.WinUI.csproj"
)

$ErrorActionPreference = "Stop"

$Root = Resolve-Path (Join-Path $PSScriptRoot "..")
$ResolvedProject = Join-Path $Root $ProjectPath
$ProjectDir = Split-Path -Parent $ResolvedProject
$PublishDir = Join-Path $ProjectDir "bin\$Platform\$Configuration\net8.0-windows10.0.19041.0\$RuntimeIdentifier\publish"
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
    throw "dotnet was not found. Install the Windows App SDK/WinUI tooling or set DOTNET_EXE."
}

if (-not (Test-Path $ResolvedProject)) {
    throw "WinUI project was not found at $ResolvedProject."
}

& $Dotnet restore $ResolvedProject
if ($LASTEXITCODE -ne 0) {
    throw "dotnet restore failed with exit code $LASTEXITCODE."
}

& $Dotnet publish $ResolvedProject `
    -c $Configuration `
    -p:Platform=$Platform `
    -r $RuntimeIdentifier `
    -p:WindowsPackageType=None `
    -p:WindowsAppSDKSelfContained=true `
    -p:PublishDir="$PublishDir\" `
    --self-contained true `
    --no-restore
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

Write-Host "WinUI publish completed: $PublishDir"
