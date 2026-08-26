param(
    [Parameter(Mandatory)][string]$Template,
    [Parameter(Mandatory)][string]$Source,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [Parameter(Mandatory)][string]$Database,
    [string]$Formats = "xlsx,pdf"
)

$ErrorActionPreference = "Stop"
& intellifill fill --template $Template --source $Source --output $OutputDirectory --format $Formats --save-db $Database
exit $LASTEXITCODE
