param(
    [string]$Configuration = "Release",
    [string]$Platform = "x64",
    [string]$ProjectPath = "winui\IntelliFillOCR.WinUI\IntelliFillOCR.WinUI.csproj"
)

$ErrorActionPreference = "Stop"

$Root = Resolve-Path (Join-Path $PSScriptRoot "..")
$ResolvedProject = Join-Path $Root $ProjectPath
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

& $Dotnet build $ResolvedProject -c $Configuration -p:Platform=$Platform --no-restore
if ($LASTEXITCODE -ne 0) {
    throw "dotnet build failed with exit code $LASTEXITCODE."
}

Write-Host "WinUI build completed: $ResolvedProject"
