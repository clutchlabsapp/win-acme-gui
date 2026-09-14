<#
.SYNOPSIS
Binds a renewed certificate to the Remote Desktop listener.

.DESCRIPTION
Written to be driven by win-acme's installation script plugin. Parameters map onto the
tokens documented at https://www.win-acme.com/reference/plugins/installation/script.

Differences from the ImportRDListener.ps1 that ships with win-acme, which is why this
exists:

  * It reports failure. The bundled script catches its own errors, prints a message and
    then exits 0, so win-acme records a renewal that did not actually bind as a success.
    This one exits 1 on any failure.
  * It verifies. After setting the thumbprint it reads the value back and compares.
  * It does not use wmic, which Microsoft has deprecated and is removing from Windows.
  * It can import from the win-acme cache. If the certificate is not in the Windows
    store at all -- which is the case with "--store none" or a PFX-only setup -- it
    imports the cached .pfx instead of giving up.

Deliberately NOT done here, because neither can be verified without a live RDP host and
getting either wrong is worse than a documented manual step:

  * Private key permissions. If RDP still refuses the certificate after this succeeds,
    the Remote Desktop service may need read access to the private key.
  * Restarting TermService. That disconnects active sessions. Existing connections keep
    the old certificate until the listener is restarted or the machine reboots.

.PARAMETER Thumbprint
Thumbprint of the new certificate. Pass win-acme's {CertThumbprint}.

.PARAMETER CacheFile
Optional. Full path of win-acme's cached .pfx, from {CacheFile}. Used only when the
certificate cannot be found in the Windows certificate store.

.PARAMETER CachePassword
Optional. Password for that .pfx, from {CachePassword}.

.EXAMPLE
Set as the win-acme script parameters:

    -Thumbprint '{CertThumbprint}' -CacheFile '{CacheFile}' -CachePassword '{CachePassword}'

Single quotes are used because win-acme wraps .ps1 calls in
powershell.exe -Command "&{...}" and doubles any embedded double quote.

.NOTES
Requires PowerShell 5.1 and an elevated process. win-acme already runs elevated.
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Thumbprint,

    [string]$CacheFile = '',

    [string]$CachePassword = ''
)

$ErrorActionPreference = 'Stop'

function Write-Step { param([string]$Message) Write-Output $Message }

function Fail {
    param([string]$Message)
    Write-Output "ERROR: $Message"
    exit 1
}

# win-acme hands over a clean thumbprint, but a hand-typed one may carry the spaces and
# invisible left-to-right mark that the certificate MMC copies.
$Thumbprint = ($Thumbprint -replace '[^0-9A-Fa-f]', '').ToUpperInvariant()

if ($Thumbprint.Length -ne 40) {
    Fail "'$Thumbprint' is not a 40 character SHA1 thumbprint."
}

$personalPath = 'Cert:\LocalMachine\My'

function Get-CertificateFromStore {
    param([string]$Thumbprint)

    Get-ChildItem -Path Cert:\LocalMachine -Recurse -ErrorAction SilentlyContinue |
        Where-Object { $_.Thumbprint -eq $Thumbprint } |
        Select-Object -First 1
}

function Copy-ToPersonalStore {
    param($Certificate)

    $sourceName = $Certificate.PSParentPath.Split('\')[-1]
    Write-Step "Copying the certificate from LocalMachine\$sourceName to LocalMachine\My."

    $source = New-Object System.Security.Cryptography.X509Certificates.X509Store($sourceName, 'LocalMachine')
    $destination = New-Object System.Security.Cryptography.X509Certificates.X509Store('My', 'LocalMachine')

    try {
        $source.Open([System.Security.Cryptography.X509Certificates.OpenFlags]::ReadOnly)
        $destination.Open([System.Security.Cryptography.X509Certificates.OpenFlags]::ReadWrite)

        $found = $source.Certificates | Where-Object { $_.Thumbprint -eq $Certificate.Thumbprint }
        if (-not $found) {
            Fail "The certificate vanished from LocalMachine\$sourceName while being copied."
        }

        $destination.Add($found)
    }
    finally {
        $source.Close()
        $destination.Close()
    }
}

function Import-FromCache {
    param([string]$Path, [string]$Password)

    Write-Step "The certificate is not in the store; importing $Path."

    if (-not (Test-Path -LiteralPath $Path)) {
        Fail "The cached certificate $Path does not exist."
    }

    # MachineKeySet so the key belongs to the machine rather than the calling user, and
    # PersistKeySet so it survives this process.
    $flags =
        [System.Security.Cryptography.X509Certificates.X509KeyStorageFlags]::MachineKeySet -bor
        [System.Security.Cryptography.X509Certificates.X509KeyStorageFlags]::PersistKeySet

    $imported = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2(
        $Path, $Password, $flags)

    $store = New-Object System.Security.Cryptography.X509Certificates.X509Store('My', 'LocalMachine')

    try {
        $store.Open([System.Security.Cryptography.X509Certificates.OpenFlags]::ReadWrite)
        $store.Add($imported)
    }
    finally {
        $store.Close()
    }

    Write-Step "Imported $($imported.Thumbprint)."
}

# ---------------------------------------------------------------- find the certificate

try {
    $certificate = Get-CertificateFromStore -Thumbprint $Thumbprint

    if ($certificate -and $certificate.PSParentPath -notlike "*LocalMachine\My*") {
        Copy-ToPersonalStore -Certificate $certificate
        $certificate = Get-ChildItem -Path $personalPath |
            Where-Object { $_.Thumbprint -eq $Thumbprint } |
            Select-Object -First 1
    }

    if (-not $certificate -and $CacheFile) {
        Import-FromCache -Path $CacheFile -Password $CachePassword
        $certificate = Get-ChildItem -Path $personalPath |
            Where-Object { $_.Thumbprint -eq $Thumbprint } |
            Select-Object -First 1
    }
}
catch {
    Fail "Could not put the certificate into LocalMachine\My: $($_.Exception.Message)"
}

if (-not $certificate) {
    Fail "Certificate $Thumbprint is not in LocalMachine\My and could not be imported. If the renewal uses --store none, pass -CacheFile '{CacheFile}' -CachePassword '{CachePassword}'."
}

# ------------------------------------------------------------------ bind the listener

try {
    $setting = Get-CimInstance `
        -Namespace 'root/CIMV2/TerminalServices' `
        -ClassName 'Win32_TSGeneralSetting' `
        -Filter "TerminalName='RDP-Tcp'"
}
catch {
    Fail "Could not read the Remote Desktop listener settings: $($_.Exception.Message)"
}

if (-not $setting) {
    Fail "No RDP-Tcp listener found. Is this machine running Remote Desktop Services?"
}

try {
    Set-CimInstance -InputObject $setting -Property @{ SSLCertificateSHA1Hash = $Thumbprint }
}
catch {
    Fail "Could not set the listener certificate: $($_.Exception.Message)"
}

# ------------------------------------------------------------------------- verify

$applied = (Get-CimInstance `
    -Namespace 'root/CIMV2/TerminalServices' `
    -ClassName 'Win32_TSGeneralSetting' `
    -Filter "TerminalName='RDP-Tcp'").SSLCertificateSHA1Hash

if ($applied -ne $Thumbprint) {
    Fail "The listener still reports '$applied' rather than '$Thumbprint'."
}

Write-Step "Remote Desktop listener is now using certificate $Thumbprint."
Write-Step "Existing sessions keep the previous certificate until the listener restarts."
exit 0
