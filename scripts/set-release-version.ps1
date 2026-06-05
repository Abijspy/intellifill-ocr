param(
    [Parameter(Mandatory = $true)]
    [string]$Version
)

$ErrorActionPreference = "Stop"

if ($Version -notmatch '^\d+\.\d+\.\d+(\.\d+)?$') {
    throw "Version must use semantic format like 3.2.0 or hotfix format like 2.2.2.1. Received: $Version"
}

$Root = Resolve-Path (Join-Path $PSScriptRoot "..")
$VersionParts = $Version.Split(".")
$AssemblyVersion = if ($VersionParts.Count -eq 4) { $Version } else { "$Version.0" }
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
    },
    @{
        Path = Join-Path $Root "winui\IntelliFillOCR.WinUI\IntelliFillOCR.WinUI.csproj"
        Pattern = '<Version>[^<]+</Version>'
        Replacement = "<Version>$Version</Version>"
    },
    @{
        Path = Join-Path $Root "winui\IntelliFillOCR.WinUI\IntelliFillOCR.WinUI.csproj"
        Pattern = '<AssemblyVersion>[^<]+</AssemblyVersion>'
        Replacement = "<AssemblyVersion>$AssemblyVersion</AssemblyVersion>"
    },
    @{
        Path = Join-Path $Root "winui\IntelliFillOCR.WinUI\IntelliFillOCR.WinUI.csproj"
        Pattern = '<FileVersion>[^<]+</FileVersion>'
        Replacement = "<FileVersion>$AssemblyVersion</FileVersion>"
    },
    @{
        Path = Join-Path $Root "winui\IntelliFillOCR.WinUI\Package.appxmanifest"
        Pattern = 'Version="[^"]+"'
        Replacement = "Version=`"$AssemblyVersion`""
    },
    @{
        Path = Join-Path $Root "winui\IntelliFillOCR.WinUI\app.manifest"
        Pattern = 'assemblyIdentity version="[^"]+"'
        Replacement = "assemblyIdentity version=`"$AssemblyVersion`""
    }
)

foreach ($File in $Files) {
    if (-not (Test-Path $File.Path)) {
        throw "Version file was not found: $($File.Path)"
    }

    $content = Get-Content -LiteralPath $File.Path -Raw
    if (-not [regex]::IsMatch($content, $File.Pattern)) {
        throw "Version pattern was not found in $($File.Path)."
    }

    $updated = [regex]::Replace($content, $File.Pattern, $File.Replacement)
    Set-Content -LiteralPath $File.Path -Value $updated -Encoding UTF8
}

Write-Host "Stamped IntelliFill OCR source version: $Version"
