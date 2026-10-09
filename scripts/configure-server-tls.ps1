[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [System.Net.IPAddress]$ServerIp,

    [string]$DnsName = "gamenet.local",

    [ValidateRange(1, 65535)]
    [int]$HttpsPort = 5080,

    [string]$ServiceAccount = "NT AUTHORITY\NETWORK SERVICE",

    [string]$PublicCertificatePath = (Join-Path $PSScriptRoot "..\artifacts\gamenet-server.cer")
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$principal = [Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw "Run this script from an elevated PowerShell session."
}
if ($ServerIp.AddressFamily -ne [System.Net.Sockets.AddressFamily]::InterNetwork) {
    throw "The current certificate helper expects an IPv4 Server address."
}
if ([string]::IsNullOrWhiteSpace($DnsName) -or $DnsName.Contains("/") -or $DnsName.Contains(":")) {
    throw "DnsName must be a host name without a scheme, path or port."
}

$extensions = @(
    "2.5.29.17={text}DNS=$DnsName&IPAddress=$($ServerIp.IPAddressToString)",
    "2.5.29.37={text}1.3.6.1.5.5.7.3.1"
)

$certificate = New-SelfSignedCertificate -Subject "CN=$DnsName" -CertStoreLocation "Cert:\LocalMachine\My" -KeyAlgorithm RSA -KeyLength 3072 -HashAlgorithm SHA256 -KeyExportPolicy NonExportable -KeyUsage DigitalSignature,KeyEncipherment -Type Custom -TextExtension $extensions -NotAfter (Get-Date).AddYears(3)
if (-not $certificate.HasPrivateKey) {
    throw "The generated TLS certificate has no private key."
}

# This single-site setup trusts its self-signed endpoint certificate locally.
# Only the public .cer file is exported to client machines; no private key is exported.
$rootStore = [System.Security.Cryptography.X509Certificates.X509Store]::new([System.Security.Cryptography.X509Certificates.StoreName]::Root, [System.Security.Cryptography.X509Certificates.StoreLocation]::LocalMachine)
try {
    $rootStore.Open([System.Security.Cryptography.X509Certificates.OpenFlags]::ReadWrite)
    $rootStore.Add($certificate)
}
finally {
    $rootStore.Close()
    $rootStore.Dispose()
}

$privateKey = [System.Security.Cryptography.X509Certificates.RSACertificateExtensions]::GetRSAPrivateKey($certificate)
try {
    $keyFile = $null
    if ($privateKey -is [System.Security.Cryptography.RSACng]) {
        $keyFile = Join-Path "$env:ProgramData\Microsoft\Crypto\Keys" $privateKey.Key.UniqueName
    }
    elseif ($privateKey -is [System.Security.Cryptography.RSACryptoServiceProvider]) {
        $keyFile = Join-Path "$env:ProgramData\Microsoft\Crypto\RSA\MachineKeys" $privateKey.CspKeyContainerInfo.UniqueKeyContainerName
    }

    if ([string]::IsNullOrWhiteSpace($keyFile) -or -not (Test-Path -LiteralPath $keyFile -PathType Leaf)) {
        throw "Could not safely locate the generated machine private-key file to grant the Server service access."
    }

    & icacls.exe $keyFile /grant "$($ServiceAccount):(R)" | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "Could not grant read access to the TLS private key for $ServiceAccount."
    }
}
finally {
    if ($privateKey) { $privateKey.Dispose() }
}

$publicPath = [IO.Path]::GetFullPath($PublicCertificatePath)
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $publicPath) | Out-Null
Export-Certificate -Cert $certificate -FilePath $publicPath -Type CERT -Force | Out-Null

$common = [Environment]::GetFolderPath([Environment+SpecialFolder]::CommonApplicationData)
$configDirectory = Join-Path (Join-Path $common "GameNet Manager") "Config"
New-Item -ItemType Directory -Path $configDirectory -Force | Out-Null
$serverConfigPath = Join-Path $configDirectory "server.json"

$serverConfig = [ordered]@{
    urls = "https://$($ServerIp.IPAddressToString):$HttpsPort"
    GameNet = [ordered]@{
        ServerTls = [ordered]@{
            CertificateThumbprint = $certificate.Thumbprint
        }
    }
}
$encoding = [System.Text.UTF8Encoding]::new($false)
[IO.File]::WriteAllText($serverConfigPath, ($serverConfig | ConvertTo-Json -Depth 8) + [Environment]::NewLine, $encoding)

& icacls.exe $configDirectory /grant "$($ServiceAccount):(OI)(CI)(RX)" | Out-Null
if ($LASTEXITCODE -ne 0) {
    throw "Could not grant the Server service read access to $configDirectory."
}

Write-Host "TLS certificate installed in LocalMachine\My and trusted locally in LocalMachine\Root."
Write-Host "Endpoint: https://$($ServerIp.IPAddressToString):$HttpsPort"
Write-Host "DNS alternative name: $DnsName"
Write-Host "Server configuration: $serverConfigPath"
Write-Host "Public certificate (copy this file to client PCs): $publicPath"
Write-Host "Windows certificate-store thumbprint (SHA-1): $($certificate.Thumbprint)"
Write-Host "Public certificate SHA-256 fingerprint: $((Get-FileHash -LiteralPath $publicPath -Algorithm SHA256).Hash.ToUpperInvariant())"
Write-Host "If the Server service runs as an account other than '$ServiceAccount', rerun with -ServiceAccount '<actual account>'."
Write-Host "Restart the GameNet Server service, then validate the health endpoint from this PC."
