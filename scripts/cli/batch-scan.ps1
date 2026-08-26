param(
    [Parameter(Mandatory)][string]$InputDirectory,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [string]$Formats = "xlsx,pdf"
)

$ErrorActionPreference = "Stop"
if (-not (Test-Path -LiteralPath $InputDirectory -PathType Container)) {
    throw "Input directory not found: $InputDirectory"
}
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null

$extensions = '.pdf', '.png', '.jpg', '.jpeg', '.tif', '.tiff', '.csv', '.xlsx', '.docx'
$documents = Get-ChildItem -LiteralPath $InputDirectory -File | Where-Object { $extensions -contains $_.Extension.ToLowerInvariant() }
if (-not $documents) { throw "No supported documents found in: $InputDirectory" }

foreach ($document in $documents) {
    & intellifill scan $document.FullName --output $OutputDirectory --format $Formats
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
