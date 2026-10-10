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
if ($ServerIp.AddressFamily -ne [System.Net.Sockets.AddressFamily]::InterNetwork -or [System.Net.IPAddress]::IsLoopback($ServerIp)) {
    throw "ServerIp must be a reserved, non-loopback IPv4 address."
}
$addressBytes = $ServerIp.GetAddressBytes()
if ($addressBytes[0] -eq 0 -or $addressBytes[0] -ge 224 -or $addressBytes[3] -eq 255) {
    throw "ServerIp must be a usable unicast IPv4 address."
}
if ([string]::IsNullOrWhiteSpace($DnsName) -or [Uri]::CheckHostName($DnsName) -ne [UriHostNameType]::Dns) {
    throw "DnsName must be a valid DNS host name without a scheme, path or port."
}

$common = [Environment]::GetFolderPath([Environment+SpecialFolder]::CommonApplicationData)
$configDirectory = Join-Path (Join-Path $common "GameNet Manager") "Config"
$serverConfigPath = Join-Path $configDirectory "server.json"
$publicPath = [System.IO.Path]::GetFullPath($PublicCertificatePath)
if (Test-Path -LiteralPath (Join-Path $configDirectory "server-secrets.bin")) {
    throw "Protected Server settings already exist. Refusing to recreate TLS configuration around existing secrets."
}
if (Test-Path -LiteralPath (Join-Path $configDirectory "server.pfx")) {
    throw "Legacy PFX material exists. Review and migrate it explicitly before configuring thumbprint-based TLS."
}
if (Test-Path -LiteralPath $serverConfigPath) {
    throw "Server runtime configuration already exists. Refusing to overwrite it; certificate rotation requires a dedicated procedure."
}
if (Test-Path -LiteralPath $publicPath) {
    throw "The public certificate output already exists. Refusing to overwrite it; certificate rotation requires a dedicated procedure."
}

$modulePath = Join-Path $PSScriptRoot "modules/ServerTlsCertificate.psm1"
if (-not (Test-Path -LiteralPath $modulePath -PathType Leaf)) {
    throw "Server TLS certificate module was not found: $modulePath"
}
Import-Module -Name $modulePath -Force

$certificateThumbprint = $null
$certificate = $null
$publicCertificate = $null
$privateKey = $null
$rootStore = $null
$rootImported = $false
$configWritten = $false
$publicExported = $false
$temporaryConfigPath = $null

function Remove-GameNetCertificateFromStore {
    param(
        [Parameter(Mandatory = $true)]
        [System.Security.Cryptography.X509Certificates.StoreName]$StoreName,
        [Parameter(Mandatory = $true)]
        [string]$Thumbprint
    )
    $store = [System.Security.Cryptography.X509Certificates.X509Store]::new(
        $StoreName,
        [System.Security.Cryptography.X509Certificates.StoreLocation]::LocalMachine)
    try {
        $store.Open([System.Security.Cryptography.X509Certificates.OpenFlags]::ReadWrite)
        $matches = $store.Certificates.Find([System.Security.Cryptography.X509Certificates.X509FindType]::FindByThumbprint, $Thumbprint, $false)
        foreach ($match in $matches) {
            try { $store.Remove($match) } finally { $match.Dispose() }
        }
    }
    finally {
        $store.Close()
        $store.Dispose()
    }
}

try {
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $publicPath) | Out-Null
    New-Item -ItemType Directory -Path $configDirectory -Force | Out-Null

    $certificateInfo = New-GameNetServerTlsCertificate -ServerAddress $ServerIp -DnsName $DnsName -PublicCertificatePath $publicPath -CertificateStoreLocation "Cert:\LocalMachine\My"
    $certificateThumbprint = $certificateInfo.Thumbprint
    $publicExported = $true
    $certificate = Get-Item -LiteralPath ("Cert:\LocalMachine\My\" + $certificateThumbprint)
    if (-not $certificate.HasPrivateKey) {
        throw "The generated Server TLS certificate does not expose its private key to the current elevated account."
    }

    $privateKey = [System.Security.Cryptography.X509Certificates.RSACertificateExtensions]::GetRSAPrivateKey($certificate)
    if ($null -eq $privateKey) {
        throw "Could not obtain the generated Server TLS certificate private key."
    }
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

    $publicCertificate = [System.Security.Cryptography.X509Certificates.X509Certificate2]::new($publicPath)
    if ($publicCertificate.HasPrivateKey -or $publicCertificate.Thumbprint -ne $certificateThumbprint) {
        throw "Refusing to trust a public certificate that contains a private key or does not match the Server store certificate."
    }

    $rootStore = [System.Security.Cryptography.X509Certificates.X509Store]::new([System.Security.Cryptography.X509Certificates.StoreName]::Root, [System.Security.Cryptography.X509Certificates.StoreLocation]::LocalMachine)
    $rootStore.Open([System.Security.Cryptography.X509Certificates.OpenFlags]::ReadWrite)
    $rootStore.Add($publicCertificate)
    $rootImported = $true
    $rootStore.Close()
    $rootStore.Dispose()
    $rootStore = $null

    # Replace inherited ACLs on the runtime configuration directory. Settings are readable,
    # but not writable, by ordinary users; only the configured Server service account gets read.
    $acl = [System.Security.AccessControl.DirectorySecurity]::new()
    $acl.SetAccessRuleProtection($true, $false)
    $inheritance = [System.Security.AccessControl.InheritanceFlags]::ContainerInherit -bor [System.Security.AccessControl.InheritanceFlags]::ObjectInherit
    $acl.AddAccessRule([System.Security.AccessControl.FileSystemAccessRule]::new([System.Security.Principal.SecurityIdentifier]::new("S-1-5-18"), [System.Security.AccessControl.FileSystemRights]::FullControl, $inheritance, [System.Security.AccessControl.PropagationFlags]::None, [System.Security.AccessControl.AccessControlType]::Allow))
    $acl.AddAccessRule([System.Security.AccessControl.FileSystemAccessRule]::new([System.Security.Principal.SecurityIdentifier]::new("S-1-5-32-544"), [System.Security.AccessControl.FileSystemRights]::FullControl, $inheritance, [System.Security.AccessControl.PropagationFlags]::None, [System.Security.AccessControl.AccessControlType]::Allow))
    $acl.AddAccessRule([System.Security.AccessControl.FileSystemAccessRule]::new([System.Security.Principal.SecurityIdentifier]::new("S-1-5-32-545"), [System.Security.AccessControl.FileSystemRights]::ReadAndExecute, $inheritance, [System.Security.AccessControl.PropagationFlags]::None, [System.Security.AccessControl.AccessControlType]::Allow))
    $acl.AddAccessRule([System.Security.AccessControl.FileSystemAccessRule]::new($ServiceAccount, [System.Security.AccessControl.FileSystemRights]::ReadAndExecute, $inheritance, [System.Security.AccessControl.PropagationFlags]::None, [System.Security.AccessControl.AccessControlType]::Allow))
    Set-Acl -LiteralPath $configDirectory -AclObject $acl

    $serverConfig = [ordered]@{
        urls = "https://$($ServerIp.IPAddressToString):$HttpsPort"
        GameNet = [ordered]@{
            ServerTls = [ordered]@{
                CertificateThumbprint = $certificateThumbprint
            }
            ProtectedSettings = [ordered]@{
                Enabled = $true
            }
        }
    }
    $encoding = [System.Text.UTF8Encoding]::new($false)
    $temporaryConfigPath = $serverConfigPath + "." + [Guid]::NewGuid().ToString("N") + ".tmp"
    [System.IO.File]::WriteAllText($temporaryConfigPath, ($serverConfig | ConvertTo-Json -Depth 8) + [Environment]::NewLine, $encoding)
    [System.IO.File]::Move($temporaryConfigPath, $serverConfigPath)
    $configWritten = $true

    Write-Host "TLS certificate installed in LocalMachine\My; only its public .cer is trusted in LocalMachine\Root."
    Write-Host "Endpoint: https://$($ServerIp.IPAddressToString):$HttpsPort"
    Write-Host "DNS alternative name: $DnsName"
    Write-Host "Server configuration: $serverConfigPath"
    Write-Host "Public certificate (copy this file to client PCs): $publicPath"
    Write-Host "Windows certificate-store thumbprint (SHA-1; Server lookup only): $certificateThumbprint"
    Write-Host "Public certificate SHA-256 fingerprint: $($certificateInfo.Sha256Fingerprint)"
    Write-Host "If the Server service runs as an account other than '$ServiceAccount', pass that account explicitly."
    Write-Host "This initial configuration refuses overwrites. Certificate rotation requires a separately tested procedure."
}
catch {
    if ($configWritten -and (Test-Path -LiteralPath $serverConfigPath -PathType Leaf)) {
        Remove-Item -LiteralPath $serverConfigPath -Force -ErrorAction SilentlyContinue
    }
    if (-not [string]::IsNullOrWhiteSpace($temporaryConfigPath) -and (Test-Path -LiteralPath $temporaryConfigPath -PathType Leaf)) {
        Remove-Item -LiteralPath $temporaryConfigPath -Force -ErrorAction SilentlyContinue
    }
    if ($rootImported -and -not [string]::IsNullOrWhiteSpace($certificateThumbprint)) {
        Remove-GameNetCertificateFromStore -StoreName ([System.Security.Cryptography.X509Certificates.StoreName]::Root) -Thumbprint $certificateThumbprint
    }
    if ($publicExported -and (Test-Path -LiteralPath $publicPath -PathType Leaf)) {
        Remove-Item -LiteralPath $publicPath -Force -ErrorAction SilentlyContinue
    }
    if (-not [string]::IsNullOrWhiteSpace($certificateThumbprint)) {
        Remove-Item -LiteralPath ("Cert:\LocalMachine\My\" + $certificateThumbprint) -Force -ErrorAction SilentlyContinue
    }
    throw
}
finally {
    if ($null -ne $rootStore) { $rootStore.Close(); $rootStore.Dispose() }
    if ($null -ne $privateKey) { $privateKey.Dispose() }
    if ($null -ne $publicCertificate) { $publicCertificate.Dispose() }
    if ($null -ne $certificate) { $certificate.Dispose() }
}
