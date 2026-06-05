[CmdletBinding()]
param(
    [string]$Version = "3.2.0.0",
    [string]$PackageName = "IntelliFillOCR.WinUI",
    [string]$DisplayName = "IntelliFill OCR",
    [string]$Publisher = "CN=IntelliFillOCR",
    [string]$PublisherDisplayName = "IntelliFill OCR",
    [ValidateSet("x64", "x86", "arm64", "neutral")]
    [string]$Architecture = "x64",
    [string]$Configuration = "Release",
    [string]$RuntimeIdentifier = "win-x64",
    [string]$OutputDir = "msix\out",
    [string]$WinUiPublishDir = "",
    [string]$CertificatePath = "",
    [string]$CertificatePassword = "IntelliFillOCR",
    [switch]$SkipWinUiBuild,
    [switch]$CreateSelfSignedCertificate = $true
)

$ErrorActionPreference = "Stop"

$RepoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$OutputRoot = Join-Path $RepoRoot $OutputDir
$StagingRoot = Join-Path $RepoRoot "msix\staging"
$PackageRoot = Join-Path $StagingRoot "WinUIPackage"
$AssetsDir = Join-Path $PackageRoot "Assets"
$LogoSource = Join-Path $RepoRoot "assets\logo_512.png"
$MsixPath = Join-Path $OutputRoot "IntelliFillOCR_${Version}_${Architecture}.msix"
$CertOutPath = Join-Path $OutputRoot "IntelliFillOCR_MSIX_SigningCert.pfx"
$CertPublicPath = Join-Path $OutputRoot "IntelliFillOCR_MSIX_SigningCert.cer"

function Find-WindowsSdkTool {
    param([Parameter(Mandatory = $true)][string]$ToolName)

    $kitsRoot = "${env:ProgramFiles(x86)}\Windows Kits\10\bin"
    if (-not (Test-Path $kitsRoot)) {
        throw "Windows SDK was not found. Install the Windows 10/11 SDK to get $ToolName."
    }

    $candidate = Get-ChildItem -Path $kitsRoot -Recurse -Filter $ToolName -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -match "\\x64\\$ToolName$" } |
        Sort-Object FullName -Descending |
        Select-Object -First 1

    if (-not $candidate) {
        throw "$ToolName was not found under $kitsRoot. Install the Windows SDK packaging tools."
    }
    return $candidate.FullName
}

function Resolve-WinUiPublishDir {
    if ($WinUiPublishDir) {
        return (Resolve-Path $WinUiPublishDir).Path
    }

    $projectDir = Join-Path $RepoRoot "winui\IntelliFillOCR.WinUI"
    $candidates = @(
        (Join-Path $projectDir "bin\$Architecture\$Configuration\net8.0-windows10.0.19041.0\$RuntimeIdentifier\publish"),
        (Join-Path $projectDir "bin\$Configuration\net8.0-windows10.0.19041.0\$RuntimeIdentifier\publish")
    )

    foreach ($candidate in $candidates) {
        if (Test-Path (Join-Path $candidate "IntelliFillOCR.exe")) {
            return (Resolve-Path $candidate).Path
        }
    }
    throw "WinUI publish output was not found. Run scripts\build-winui.ps1 first or omit -SkipWinUiBuild."
}

function New-TileAsset {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][int]$Width,
        [Parameter(Mandatory = $true)][int]$Height,
        [string]$Text = "IF",
        [string]$LogoPath = ""
    )

    Add-Type -AssemblyName System.Drawing
    $bitmap = New-Object System.Drawing.Bitmap $Width, $Height
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $background = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(21, 25, 34))
    $accent = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(96, 165, 250))
    $white = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(237, 242, 247))
    $graphics.FillRectangle($background, 0, 0, $Width, $Height)

    if ($LogoPath -and (Test-Path $LogoPath)) {
        $source = [System.Drawing.Image]::FromFile((Resolve-Path $LogoPath).Path)
        try {
            $scale = [Math]::Min($Width / $source.Width, $Height / $source.Height)
            $drawWidth = $source.Width * $scale
            $drawHeight = $source.Height * $scale
            $drawX = ($Width - $drawWidth) / 2
            $drawY = ($Height - $drawHeight) / 2
            $graphics.DrawImage($source, [float]$drawX, [float]$drawY, [float]$drawWidth, [float]$drawHeight)
            $bitmap.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
            return
        } finally {
            $source.Dispose()
            $graphics.Dispose()
            $bitmap.Dispose()
        }
    }

    $circleSize = [Math]::Min($Width, $Height) * 0.58
    $circleX = ($Width - $circleSize) / 2
    $circleY = ($Height - $circleSize) / 2
    $graphics.FillEllipse($accent, [float]$circleX, [float]$circleY, [float]$circleSize, [float]$circleSize)

    $fontSize = [Math]::Max(12, [Math]::Min($Width, $Height) * 0.26)
    $font = New-Object System.Drawing.Font "Segoe UI Semibold", $fontSize, ([System.Drawing.FontStyle]::Bold)
    $format = New-Object System.Drawing.StringFormat
    $format.Alignment = [System.Drawing.StringAlignment]::Center
    $format.LineAlignment = [System.Drawing.StringAlignment]::Center
    $rect = New-Object System.Drawing.RectangleF 0, 0, $Width, $Height
    $graphics.DrawString($Text, $font, $white, $rect, $format)

    $bitmap.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    $graphics.Dispose()
    $bitmap.Dispose()
}

function Expand-ManifestTemplate {
    $templatePath = Join-Path $PSScriptRoot "AppxManifest.template.xml"
    $manifestPath = Join-Path $PackageRoot "AppxManifest.xml"
    $content = Get-Content $templatePath -Raw
    $content = $content.Replace("{{PACKAGE_NAME}}", $PackageName)
    $content = $content.Replace("{{PUBLISHER}}", $Publisher)
    $content = $content.Replace("{{VERSION}}", $Version)
    $content = $content.Replace("{{ARCHITECTURE}}", $Architecture)
    $content = $content.Replace("{{DISPLAY_NAME}}", $DisplayName)
    $content = $content.Replace("{{PUBLISHER_DISPLAY_NAME}}", $PublisherDisplayName)
    Set-Content -Path $manifestPath -Value $content -Encoding UTF8
}

function Write-InstallHelpers {
    $installPs1 = Join-Path $OutputRoot "Install-IntelliFillOCR-MSIX.ps1"
    $installCmd = Join-Path $OutputRoot "Install-IntelliFillOCR-MSIX.cmd"
    $msixName = Split-Path $MsixPath -Leaf
    $certName = Split-Path $CertPublicPath -Leaf

    @"
`$ErrorActionPreference = "Stop"
`$Root = Split-Path -Parent `$MyInvocation.MyCommand.Path
`$Msix = Join-Path `$Root "$msixName"
`$Cert = Join-Path `$Root "$certName"

if (-not (Test-Path `$Msix)) { throw "MSIX file was not found: `$Msix" }
if (-not (Test-Path `$Cert)) { throw "Certificate file was not found: `$Cert" }

Write-Host "Trusting IntelliFill OCR MSIX certificate for CurrentUser..."
Import-Certificate -FilePath `$Cert -CertStoreLocation Cert:\CurrentUser\Root | Out-Null
Import-Certificate -FilePath `$Cert -CertStoreLocation Cert:\CurrentUser\TrustedPeople | Out-Null

Write-Host "Installing IntelliFill OCR MSIX..."
Add-AppxPackage -Path `$Msix -ForceUpdateFromAnyVersion
Write-Host "Install/update complete."
"@ | Set-Content -Path $installPs1 -Encoding UTF8

    @"
@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install-IntelliFillOCR-MSIX.ps1"
pause
"@ | Set-Content -Path $installCmd -Encoding ASCII
}

if (-not $SkipWinUiBuild) {
    & (Join-Path $RepoRoot "scripts\build-winui.ps1") -Configuration $Configuration -Platform $Architecture -RuntimeIdentifier $RuntimeIdentifier
}

$publishDir = Resolve-WinUiPublishDir
if (-not (Test-Path (Join-Path $publishDir "IntelliFillOCR.exe"))) {
    throw "WinUI executable was not found in $publishDir."
}

New-Item -ItemType Directory -Path $OutputRoot -Force | Out-Null
if (Test-Path $StagingRoot) {
    Remove-Item -LiteralPath $StagingRoot -Recurse -Force
}
if (Test-Path $MsixPath) {
    Remove-Item -LiteralPath $MsixPath -Force
}
New-Item -ItemType Directory -Path $PackageRoot -Force | Out-Null
New-Item -ItemType Directory -Path $AssetsDir -Force | Out-Null

Copy-Item -Path (Join-Path $publishDir "*") -Destination $PackageRoot -Recurse -Force
New-TileAsset -Path (Join-Path $AssetsDir "StoreLogo.png") -Width 50 -Height 50 -LogoPath $LogoSource
New-TileAsset -Path (Join-Path $AssetsDir "Square44x44Logo.png") -Width 44 -Height 44 -LogoPath $LogoSource
New-TileAsset -Path (Join-Path $AssetsDir "Square71x71Logo.png") -Width 71 -Height 71 -LogoPath $LogoSource
New-TileAsset -Path (Join-Path $AssetsDir "Square150x150Logo.png") -Width 150 -Height 150 -LogoPath $LogoSource
New-TileAsset -Path (Join-Path $AssetsDir "Square310x310Logo.png") -Width 310 -Height 310 -LogoPath $LogoSource
New-TileAsset -Path (Join-Path $AssetsDir "Wide310x150Logo.png") -Width 310 -Height 150 -LogoPath $LogoSource
Expand-ManifestTemplate

$makeAppx = Find-WindowsSdkTool "MakeAppx.exe"
& $makeAppx pack /d $PackageRoot /p $MsixPath /o /nv
if ($LASTEXITCODE -ne 0) {
    throw "MakeAppx failed with exit code $LASTEXITCODE. No MSIX package was created."
}
if (-not (Test-Path $MsixPath)) {
    throw "MakeAppx finished but the MSIX file was not found at $MsixPath."
}

if ($CreateSelfSignedCertificate -and -not $CertificatePath) {
    $securePassword = ConvertTo-SecureString -String $CertificatePassword -Force -AsPlainText
    $cert = New-SelfSignedCertificate `
        -Type Custom `
        -Subject $Publisher `
        -KeyUsage DigitalSignature, CertSign `
        -FriendlyName "IntelliFill OCR MSIX Signing" `
        -CertStoreLocation "Cert:\CurrentUser\My" `
        -TextExtension @(
            "2.5.29.37={text}1.3.6.1.5.5.7.3.3",
            "2.5.29.19={critical}{text}CA=true"
        )
    Export-PfxCertificate -Cert $cert -FilePath $CertOutPath -Password $securePassword | Out-Null
    Export-Certificate -Cert $cert -FilePath $CertPublicPath | Out-Null
    $CertificatePath = $CertOutPath
    Write-Host "Created signing certificate: $CertOutPath"
    Write-Host "Created public certificate: $CertPublicPath"
}

if (-not $CertificatePath) {
    throw "MSIX packages must be signed. Pass -CertificatePath or use -CreateSelfSignedCertificate."
}

$signTool = Find-WindowsSdkTool "SignTool.exe"
$signArgs = @("sign", "/fd", "SHA256", "/f", $CertificatePath)
if ($CertificatePassword) {
    $signArgs += @("/p", $CertificatePassword)
}
$signArgs += $MsixPath
& $signTool @signArgs
if ($LASTEXITCODE -ne 0) {
    throw "SignTool failed with exit code $LASTEXITCODE. The MSIX exists but is not signed correctly: $MsixPath"
}

Write-InstallHelpers
Write-Host "Signed MSIX: $MsixPath"
Write-Host "Certificate: $CertPublicPath"
