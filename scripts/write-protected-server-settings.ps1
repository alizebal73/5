[CmdletBinding()]
param(
    [Parameter()][string]$DestinationPath = (Join-Path $env:ProgramData "GameNet Manager\Config\server-secrets.bin"),
    [Parameter()][string]$ServerConfigurationPath = (Join-Path $env:ProgramData "GameNet Manager\Config\server.json"),
    [Parameter()][string]$PublicCertificatePath = (Join-Path $PSScriptRoot "..\artifacts\gamenet-server.cer"),
    [Parameter()][string]$ServiceAccount = "NT AUTHORITY\NETWORK SERVICE"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$principal = [Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw "Run this script from an elevated PowerShell session."
}

$fullDestination = [System.IO.Path]::GetFullPath($DestinationPath)
$fullServerConfigurationPath = [System.IO.Path]::GetFullPath($ServerConfigurationPath)
$fullPublicCertificatePath = [System.IO.Path]::GetFullPath($PublicCertificatePath)
$configurationDirectory = [System.IO.Path]::GetFullPath((Split-Path -Parent $fullServerConfigurationPath))
$destinationDirectory = [System.IO.Path]::GetFullPath((Split-Path -Parent $fullDestination))
if (-not [string]::Equals($configurationDirectory, $destinationDirectory, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Protected Server secrets must be stored beside server.json in the ACL-protected configuration directory."
}
if (Test-Path -LiteralPath $fullDestination) {
    throw "Protected settings already exist. Refusing to overwrite the Server secrets file."
}
if (-not (Test-Path -LiteralPath $fullServerConfigurationPath -PathType Leaf)) {
    throw "Run configure-server-tls.ps1 first; the expected server.json configuration is missing."
}
if (-not (Test-Path -LiteralPath $fullPublicCertificatePath -PathType Leaf)) {
    throw "The public Server trust certificate was not found at the configured path."
}

try {
    Add-Type -AssemblyName System.Security.Cryptography.ProtectedData -ErrorAction Stop
}
catch {
    Add-Type -AssemblyName System.Security -ErrorAction Stop
}

function Read-RequiredText([string]$Prompt, [int]$MaximumLength = 512) {
    $value = Read-Host $Prompt
    if ([string]::IsNullOrWhiteSpace($value) -or $value.Length -gt $MaximumLength) {
        throw "The supplied value is empty or exceeds the supported length."
    }
    return $value.Trim()
}

function Read-SecretText([string]$Prompt, [int]$MinimumLength = 32, [int]$MaximumLength = 1024) {
    $secure = Read-Host -Prompt $Prompt -AsSecureString
    $pointer = [IntPtr]::Zero
    try {
        $pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
        $value = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer)
        if ($value.Length -lt $MinimumLength -or $value.Length -gt $MaximumLength) {
            throw "The secret must contain $MinimumLength-$MaximumLength characters."
        }
        return $value
    }
    finally {
        if ($pointer -ne [IntPtr]::Zero) {
            [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer)
        }
        $secure.Dispose()
    }
}

function Clear-ByteArray([byte[]]$Bytes) {
    if ($null -ne $Bytes -and $Bytes.Length -gt 0) {
        [System.Security.Cryptography.CryptographicOperations]::ZeroMemory($Bytes)
    }
}

# TLS is configured separately: its private key stays non-exportable in LocalMachine\My.
$serverConfiguration = Get-Content -LiteralPath $fullServerConfigurationPath -Raw | ConvertFrom-Json -AsHashtable
$urls = [string]$serverConfiguration["urls"]
$thumbprint = [string]$serverConfiguration["GameNet"]["ServerTls"]["CertificateThumbprint"]
if ([string]::IsNullOrWhiteSpace($urls) -or [string]::IsNullOrWhiteSpace($thumbprint)) {
    throw "server.json must contain HTTPS urls and GameNet:ServerTls:CertificateThumbprint."
}
$httpsUrls = @($urls.Split(';', [StringSplitOptions]::RemoveEmptyEntries) | ForEach-Object { $_.Trim() })
if ($httpsUrls.Count -ne 1 -or -not $httpsUrls[0].StartsWith("https://", [StringComparison]::OrdinalIgnoreCase)) {
    throw "The initial production Server configuration must contain exactly one HTTPS listener."
}
$serverUri = $null
if (-not [Uri]::TryCreate($httpsUrls[0], [UriKind]::Absolute, [ref]$serverUri) -or
    -not [string]::IsNullOrEmpty($serverUri.UserInfo) -or
    -not [string]::IsNullOrEmpty($serverUri.Query) -or
    -not [string]::IsNullOrEmpty($serverUri.Fragment) -or
    $serverUri.AbsolutePath -ne "/" -or
    $serverUri.IsLoopback) {
    throw "server.json must use a root HTTPS URL with a reserved non-loopback Server address."
}
$serverAddress = $null
if (-not [System.Net.IPAddress]::TryParse($serverUri.Host, [ref]$serverAddress) -or
    $serverAddress.AddressFamily -ne [System.Net.Sockets.AddressFamily]::InterNetwork) {
    throw "The configured HTTPS listener must use the reserved IPv4 address covered by the Server certificate."
}

$thumbprint = $thumbprint.Replace(" ", "").ToUpperInvariant()
if ($thumbprint.Length -ne 40 -or $thumbprint -notmatch '^[A-F0-9]{40}$') {
    throw "The Server certificate thumbprint in server.json is invalid."
}
$certificate = Get-Item -LiteralPath ("Cert:\LocalMachine\My\" + $thumbprint) -ErrorAction Stop
$publicCertificate = $null
$rsa = $null
try {
    if (-not $certificate.HasPrivateKey -or $certificate.Thumbprint -ne $thumbprint) {
        throw "The Server certificate store entry is missing its private key or does not match server.json."
    }
    $publicCertificate = [System.Security.Cryptography.X509Certificates.X509Certificate2]::new($fullPublicCertificatePath)
    if ($publicCertificate.HasPrivateKey -or $publicCertificate.Thumbprint -ne $thumbprint) {
        throw "The public trust certificate contains a private key or does not match the Server certificate."
    }

    Import-Module -Name (Join-Path $PSScriptRoot "modules/ServerTlsCertificate.psm1") -Force
    $dnsName = $certificate.GetNameInfo([System.Security.Cryptography.X509Certificates.X509NameType]::DnsName, $false)
    Test-GameNetServerTlsCertificateProfile -Certificate $certificate -ServerAddress $serverAddress -DnsName $dnsName

    $rsa = [System.Security.Cryptography.X509Certificates.RSACertificateExtensions]::GetRSAPrivateKey($certificate)
    $exportRejected = $false
    try {
        $privateBytes = $rsa.ExportRSAPrivateKey()
        [System.Security.Cryptography.CryptographicOperations]::ZeroMemory($privateBytes)
    }
    catch [System.Security.Cryptography.CryptographicException] {
        $exportRejected = $true
    }
    if (-not $exportRejected) {
        throw "The Server TLS private key is exportable. Refusing to provision protected Server settings."
    }
}
finally {
    if ($null -ne $rsa) { $rsa.Dispose() }
    if ($null -ne $publicCertificate) { $publicCertificate.Dispose() }
    if ($null -ne $certificate) { $certificate.Dispose() }
}

$bootstrapSecret = Read-SecretText "Initial owner bootstrap secret (re-enter only when prompted by bootstrap-admin.ps1)" 32 512

$settingsFileCreated = $false
$clearBytes = $null
$protectedBytes = $null
$settings = $null
try {
    $settings = [ordered]@{
        "GameNet:Authentication:Enabled" = "true"
        "GameNet:Authentication:Issuer" = "GameNet5.Server"
        "GameNet:Authentication:Audience" = "GameNet5.Desktop"
        "GameNet:Setup:BootstrapSecret" = $bootstrapSecret
        "GameNet:ServerTls:CertificateThumbprint" = $thumbprint
    }

    $clearBytes = [System.Text.Encoding]::UTF8.GetBytes(($settings | ConvertTo-Json -Compress))
    $protectedBytes = [Security.Cryptography.ProtectedData]::Protect(
        $clearBytes,
        $null,
        [Security.Cryptography.DataProtectionScope]::LocalMachine)

    $directory = Split-Path -Parent $fullDestination
    New-Item -ItemType Directory -Force -Path $directory | Out-Null
    $stream = [System.IO.File]::Open(
        $fullDestination,
        [System.IO.FileMode]::CreateNew,
        [System.IO.FileAccess]::Write,
        [System.IO.FileShare]::None)
    $settingsFileCreated = $true
    try {
        $stream.Write($protectedBytes, 0, $protectedBytes.Length)
        $stream.Flush($true)
    }
    finally {
        $stream.Dispose()
    }

    # Only LocalSystem, Administrators, and the configured Server service identity may read the file.
    & icacls.exe $fullDestination /inheritance:r /grant:r '*S-1-5-18:(F)' '*S-1-5-32-544:(F)' ("$ServiceAccount`:(R)") | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Could not restrict protected settings file ACLs." }

    Write-Host "Protected Server setup settings were written; deployment secrets are provisioned separately with the dedicated Server secret store."
    Write-Host "The TLS private key remains in the Windows certificate store."
    Write-Host "Server endpoint: $($serverUri.AbsoluteUri)"
    Write-Host "Public trust certificate: $fullPublicCertificatePath"
    Write-Host ("SHA-256 certificate fingerprint: {0}" -f (Get-FileHash -LiteralPath $fullPublicCertificatePath -Algorithm SHA256).Hash.ToUpperInvariant())
    Write-Host "Verify the fingerprint out of band on every client before trusting the public certificate."
    Write-Host "Never export or distribute the Server TLS private key."
    Write-Host "Re-enter the bootstrap secret when running scripts/bootstrap-admin.ps1."
    Write-Host "This file does not store the PostgreSQL connection string, JWT signing key, or Agent provisioning key."
}
catch {
    if ($settingsFileCreated -and (Test-Path -LiteralPath $fullDestination)) {
        Remove-Item -LiteralPath $fullDestination -Force -ErrorAction SilentlyContinue
    }
    throw
}
finally {
    Clear-ByteArray $clearBytes
    Clear-ByteArray $protectedBytes
    $bootstrapSecret = $null
    $settings = $null
}
