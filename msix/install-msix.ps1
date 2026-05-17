[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$MsixPath,
    [string]$CertificatePath = "",
    [string]$CertificatePassword = "",
    [switch]$TrustMachineStore
)

$ErrorActionPreference = "Stop"
$ResolvedMsix = Resolve-Path $MsixPath

function Test-IsAdministrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

if ($CertificatePath) {
    $ResolvedCert = Resolve-Path $CertificatePath
    if ($CertificatePassword) {
        $password = ConvertTo-SecureString -String $CertificatePassword -Force -AsPlainText
        Import-PfxCertificate -FilePath $ResolvedCert -CertStoreLocation Cert:\CurrentUser\Root -Password $password | Out-Null
        Import-PfxCertificate -FilePath $ResolvedCert -CertStoreLocation Cert:\CurrentUser\TrustedPeople -Password $password | Out-Null
        if ($TrustMachineStore) {
            if (-not (Test-IsAdministrator)) {
                throw "TrustMachineStore requires running PowerShell as Administrator."
            }
            Import-PfxCertificate -FilePath $ResolvedCert -CertStoreLocation Cert:\LocalMachine\Root -Password $password | Out-Null
            Import-PfxCertificate -FilePath $ResolvedCert -CertStoreLocation Cert:\LocalMachine\TrustedPeople -Password $password | Out-Null
        }
    } else {
        Import-Certificate -FilePath $ResolvedCert -CertStoreLocation Cert:\CurrentUser\Root | Out-Null
        Import-Certificate -FilePath $ResolvedCert -CertStoreLocation Cert:\CurrentUser\TrustedPeople | Out-Null
        if ($TrustMachineStore) {
            if (-not (Test-IsAdministrator)) {
                throw "TrustMachineStore requires running PowerShell as Administrator."
            }
            Import-Certificate -FilePath $ResolvedCert -CertStoreLocation Cert:\LocalMachine\Root | Out-Null
            Import-Certificate -FilePath $ResolvedCert -CertStoreLocation Cert:\LocalMachine\TrustedPeople | Out-Null
        }
    }
}

Add-AppxPackage -Path $ResolvedMsix
Write-Host "Installed $ResolvedMsix"
