param(
    [Parameter(Mandatory = $true)]
    [string]$Version
)

$ErrorActionPreference = "Stop"

if ($Version -notmatch '^\d+\.\d+\.\d+(\.\d+)?$') {
    throw "Version must use semantic format like 3.0.1 or hotfix format like 2.2.2.1. Received: $Version"
}

$Root = Resolve-Path (Join-Path $PSScriptRoot "..")
$Files = @(
    @{
        Path = Join-Path $Root "src\intellifill_ocr\__init__.py"
        Pattern = '__version__\s*=\s*"[^"]+"'
        Replacement = "__version__ = `"$Version`""
    },
    @{
        Path = Join-Path $Root "pyproject.toml"
        Pattern = '(?m)^version\s*=\s*"[^"]+"'
        Replacement = "version = `"$Version`""
    },
    @{
        Path = Join-Path $Root "installer\IntelliFillOCR.iss"
        Pattern = '#define AppVersion "[^"]+"'
        Replacement = "#define AppVersion `"$Version`""
    }
)

foreach ($File in $Files) {
    if (-not (Test-Path $File.Path)) {
        throw "Version file was not found: $($File.Path)"
    }

    $content = Get-Content -LiteralPath $File.Path -Raw
    $updated = [regex]::Replace($content, $File.Pattern, $File.Replacement)
    if ($updated -eq $content) {
        throw "No version replacement was made in $($File.Path)."
    }

    Set-Content -LiteralPath $File.Path -Value $updated -Encoding UTF8
}

Write-Host "Stamped IntelliFill OCR source version: $Version"
