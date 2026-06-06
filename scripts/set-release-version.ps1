param(
    [Parameter(Mandatory = $true)]
    [string]$Version
)

$ErrorActionPreference = "Stop"

if ($Version -notmatch '^\d+\.\d+\.\d+(\.\d+)?$') {
    throw "Version must use semantic format like 3.3.0 or hotfix format like 3.3.0.1. Received: $Version"
}

$Root = Resolve-Path (Join-Path $PSScriptRoot "..")
$VersionParts = $Version.Split(".")
$AssemblyVersion = if ($VersionParts.Count -eq 4) { $Version } else { "$Version.0" }
$Files = @(
    @{
        Path = Join-Path $Root "src/intellifill_ocr/__init__.py"
        Pattern = '__version__\s*=\s*"[^"]+"'
        Replacement = "__version__ = `"$Version`""
    },
    @{
        Path = Join-Path $Root "pyproject.toml"
        Pattern = '(?m)^version\s*=\s*"[^"]+"'
        Replacement = "version = `"$Version`""
    },
    @{
        Path = Join-Path $Root "winui/IntelliFillOCR.WinUI/IntelliFillOCR.WinUI.csproj"
        Pattern = '<Version>[^<]+</Version>'
        Replacement = "<Version>$Version</Version>"
    },
    @{
        Path = Join-Path $Root "winui/IntelliFillOCR.WinUI/IntelliFillOCR.WinUI.csproj"
        Pattern = '<AssemblyVersion>[^<]+</AssemblyVersion>'
        Replacement = "<AssemblyVersion>$AssemblyVersion</AssemblyVersion>"
    },
    @{
        Path = Join-Path $Root "winui/IntelliFillOCR.WinUI/IntelliFillOCR.WinUI.csproj"
        Pattern = '<FileVersion>[^<]+</FileVersion>'
        Replacement = "<FileVersion>$AssemblyVersion</FileVersion>"
    },
    @{
        Path = Join-Path $Root "winui/IntelliFillOCR.WinUI/Package.appxmanifest"
        Pattern = '(<Identity\b[^>]*\bVersion=")[^"]+(")'
        Replacement = "`${1}$AssemblyVersion`${2}"
    },
    @{
        Path = Join-Path $Root "winui/IntelliFillOCR.WinUI/app.manifest"
        Pattern = 'assemblyIdentity version="[^"]+"'
        Replacement = "assemblyIdentity version=`"$AssemblyVersion`""
    },
    @{
        Path = Join-Path $Root "winui/IntelliFillOCR.WinUI/MainWindow.xaml.cs"
        Pattern = 'private const string AppVersion = "[^"]+";'
        Replacement = "private const string AppVersion = `"$Version`";"
    },
    @{
        Path = Join-Path $Root "src/IntelliFillOCR.Avalonia/IntelliFillOCR.Avalonia.csproj"
        Pattern = '<Version>[^<]+</Version>'
        Replacement = "<Version>$Version</Version>"
    },
    @{
        Path = Join-Path $Root "src/IntelliFillOCR.Avalonia/IntelliFillOCR.Avalonia.csproj"
        Pattern = '<AssemblyVersion>[^<]+</AssemblyVersion>'
        Replacement = "<AssemblyVersion>$AssemblyVersion</AssemblyVersion>"
    },
    @{
        Path = Join-Path $Root "src/IntelliFillOCR.Avalonia/IntelliFillOCR.Avalonia.csproj"
        Pattern = '<FileVersion>[^<]+</FileVersion>'
        Replacement = "<FileVersion>$AssemblyVersion</FileVersion>"
    },
    @{
        Path = Join-Path $Root "src/IntelliFillOCR.Avalonia/MainWindow.axaml.cs"
        Pattern = 'private const string AppVersion = "[^"]+";'
        Replacement = "private const string AppVersion = `"$Version`";"
    }
)

foreach ($File in $Files) {
    if (-not (Test-Path $File.Path)) {
        throw "Version file was not found: $($File.Path)"
    }

    $resolvedPath = (Resolve-Path -LiteralPath $File.Path).Path
    $content = [System.IO.File]::ReadAllText($resolvedPath)
    if (-not [regex]::IsMatch($content, $File.Pattern)) {
        throw "Version pattern was not found in $($File.Path)."
    }

    $updated = [regex]::Replace($content, $File.Pattern, $File.Replacement)
    $updated = $updated.TrimEnd("`r", "`n") + "`r`n"
    [System.IO.File]::WriteAllText($resolvedPath, $updated, [System.Text.UTF8Encoding]::new($false))
}

Write-Host "Stamped IntelliFill OCR source version: $Version"
