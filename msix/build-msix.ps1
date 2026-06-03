[CmdletBinding()]
param(
    [string]$Version = "2.4.1.0",
    [string]$PackageName = "IntelliFillOCR.Desktop",
    [string]$DisplayName = "IntelliFill OCR",
    [string]$Publisher = "CN=IntelliFillOCR",
    [string]$PublisherDisplayName = "IntelliFill OCR",
    [ValidateSet("x64", "x86", "arm64", "neutral")]
    [string]$Architecture = "x64",
    [string]$OutputDir = "msix\out",
    [string]$DistAppPath = "",
    [string]$CertificatePath = "",
    [string]$CertificatePassword = "",
    [switch]$SkipExeBuild,
    [switch]$CreateSelfSignedCertificate
)

$ErrorActionPreference = "Stop"

$RepoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$DistApp = if ($DistAppPath) { (Resolve-Path $DistAppPath).Path } else { Join-Path $RepoRoot "dist\IntelliFillOCR" }
$StagingRoot = Join-Path $RepoRoot "msix\staging"
$PackageRoot = Join-Path $StagingRoot "Package"
$AssetsDir = Join-Path $PackageRoot "Assets"
$OutputRoot = Join-Path $RepoRoot $OutputDir
$LogoSource = Join-Path $RepoRoot "assets\logo.png"
$MsixPath = Join-Path $OutputRoot "IntelliFillOCR_${Version}_${Architecture}.msix"
$CertOutPath = Join-Path $OutputRoot "IntelliFillOCR_SigningCert.pfx"
$CertPublicPath = Join-Path $OutputRoot "IntelliFillOCR_SigningCert.cer"

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

function Remove-InvalidMsixPayloadFiles {
    $docxTemplates = Join-Path $PackageRoot "IntelliFillOCR\_internal\docx\templates"
    if (Test-Path $docxTemplates) {
        Write-Warning "Removing python-docx template payloads that are incompatible with MSIX packaging."
        Remove-Item -LiteralPath $docxTemplates -Recurse -Force
    }

    $invalidFiles = Get-ChildItem -Path $PackageRoot -Recurse -File |
        Where-Object {
            $relative = $_.FullName.Substring($PackageRoot.Length + 1)
            $_.Name.Contains("[") -or
            $_.Name.Contains("]") -or
            $_.Name.Contains("+") -or
            $relative.Contains(" ")
        }

    foreach ($file in $invalidFiles) {
        Write-Warning "Removing MSIX-incompatible payload file: $($file.FullName.Substring($PackageRoot.Length + 1))"
        Remove-Item -LiteralPath $file.FullName -Force
    }
}

if (-not $SkipExeBuild) {
    & (Join-Path $RepoRoot "build.ps1")
}

if (-not (Test-Path (Join-Path $DistApp "IntelliFillOCR.exe"))) {
    throw "PyInstaller output was not found at $DistApp. Run .\build.ps1 first or omit -SkipExeBuild."
}

New-Item -ItemType Directory -Path $OutputRoot -Force | Out-Null
if (Test-Path $StagingRoot) {
    Remove-Item -LiteralPath $StagingRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $PackageRoot -Force | Out-Null
New-Item -ItemType Directory -Path $AssetsDir -Force | Out-Null

Copy-Item -Path $DistApp -Destination (Join-Path $PackageRoot "IntelliFillOCR") -Recurse
New-TileAsset -Path (Join-Path $AssetsDir "StoreLogo.png") -Width 50 -Height 50 -LogoPath $LogoSource
New-TileAsset -Path (Join-Path $AssetsDir "Square44x44Logo.png") -Width 44 -Height 44 -LogoPath $LogoSource
New-TileAsset -Path (Join-Path $AssetsDir "Square71x71Logo.png") -Width 71 -Height 71 -LogoPath $LogoSource
New-TileAsset -Path (Join-Path $AssetsDir "Square150x150Logo.png") -Width 150 -Height 150 -LogoPath $LogoSource
New-TileAsset -Path (Join-Path $AssetsDir "Square310x310Logo.png") -Width 310 -Height 310 -LogoPath $LogoSource
New-TileAsset -Path (Join-Path $AssetsDir "Wide310x150Logo.png") -Width 310 -Height 150 -LogoPath $LogoSource
Expand-ManifestTemplate
Remove-InvalidMsixPayloadFiles

$makeAppx = Find-WindowsSdkTool "MakeAppx.exe"
& $makeAppx pack /d $PackageRoot /p $MsixPath /o /nv
if ($LASTEXITCODE -ne 0) {
    throw "MakeAppx failed with exit code $LASTEXITCODE. No MSIX package was created."
}
if (-not (Test-Path $MsixPath)) {
    throw "MakeAppx finished but the MSIX file was not found at $MsixPath."
}

if ($CreateSelfSignedCertificate) {
    $securePassword = ConvertTo-SecureString -String $(if ($CertificatePassword) { $CertificatePassword } else { "IntelliFillOCR" }) -Force -AsPlainText
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
    if (-not $CertificatePassword) {
        $CertificatePassword = "IntelliFillOCR"
    }
    Write-Host "Created signing certificate: $CertOutPath"
    Write-Host "Created public certificate: $CertPublicPath"
}

if ($CertificatePath) {
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
    Write-Host "Signed MSIX: $MsixPath"
} else {
    Write-Warning "MSIX created but not signed. Windows requires MSIX packages to be signed before install."
}

Write-Host "MSIX package: $MsixPath"
