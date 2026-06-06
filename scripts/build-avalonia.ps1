param(
    [string]$Configuration = "Release",
    [string]$RuntimeIdentifier = "win-x64",
    [string]$OutputDir = "release\avalonia-win-x64\publish"
)

$ErrorActionPreference = "Stop"

$Root = Resolve-Path (Join-Path $PSScriptRoot "..")
$Project = Join-Path $Root "src\IntelliFillOCR.Avalonia\IntelliFillOCR.Avalonia.csproj"
$PublishDir = Join-Path $Root $OutputDir

New-Item -ItemType Directory -Path $PublishDir -Force | Out-Null

dotnet publish $Project `
    -c $Configuration `
    -r $RuntimeIdentifier `
    --self-contained true `
    -p:PublishSingleFile=false `
    -p:PublishDir="$PublishDir\"

if ($LASTEXITCODE -ne 0) {
    throw "Avalonia publish failed with exit code $LASTEXITCODE."
}

Write-Host "Avalonia publish completed: $PublishDir"
