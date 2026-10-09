[CmdletBinding()]
param(
    [Parameter()][string]$DestinationPath = (Join-Path $env:ProgramData "GameNet Manager\Config\server-secrets.bin")
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$principal = [Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw "Run this script from an elevated PowerShell session."
}
$fullDestination = [System.IO.Path]::GetFullPath($DestinationPath)
if (Test-Path -LiteralPath $fullDestination) {
    throw "Protected settings already exist. Refusing to overwrite the Server secrets file."
}

Add-Type -AssemblyName System.Security.Cryptography.ProtectedData

function Read-RequiredText([string]$Prompt, [int]$MaximumLength = 512) {
    $value = Read-Host $Prompt
    if ([string]::IsNullOrWhiteSpace($value) -or $value.Length -gt $MaximumLength) {
        throw "The supplied value is empty or exceeds the supported length."
    }
    return $value.Trim()
}

function Read-SecretText([string]$Prompt, [int]$MinimumLength = 32, [int]$MaximumLength = 1024) {
    $secure = Read-Host -Prompt $Prompt -AsSecureString
    try {
        $value = ConvertFrom-SecureString -SecureString $secure -AsPlainText
        if ($value.Length -lt $MinimumLength -or $value.Length -gt $MaximumLength) {
            throw "The secret must contain $MinimumLength-$MaximumLength characters."
        }
        return $value
    }
    finally {
        $secure.Dispose()
    }
}

function New-RandomSecret([int]$ByteCount = 48) {
    $value = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes($ByteCount))
    return $value.TrimEnd('=').Replace('+', '-').Replace('/', '_')
}

$directory = Split-Path -Parent $fullDestination
New-Item -ItemType Directory -Force -Path $directory | Out-Null

# Restrict the directory before creating any file that will contain protected credentials.
& icacls.exe $directory /inheritance:r /grant:r '*S-1-5-18:(OI)(CI)(F)' '*S-1-5-32-544:(OI)(CI)(F)' | Out-Null
if ($LASTEXITCODE -ne 0) { throw "Could not restrict protected settings directory ACLs." }

$hostName = Read-RequiredText "PostgreSQL host (use localhost when PostgreSQL is installed on this PC)" 255
$portText = Read-RequiredText "PostgreSQL port (normally 5432)" 5
$port = 0
if (-not [int]::TryParse($portText, [ref]$port) -or $port -lt 1 -or $port -gt 65535) {
    throw "The PostgreSQL port must be a valid TCP port."
}
$database = Read-RequiredText "GameNet database name" 63
$dbUser = Read-RequiredText "Dedicated PostgreSQL application username" 63
$dbPassword = Read-SecretText "PostgreSQL application password" 20 256
$bootstrapSecret = Read-SecretText "Initial owner bootstrap secret (you must re-enter this in bootstrap-admin.ps1)" 32 512
$serverAddressText = Read-RequiredText "Reserved/static LAN IPv4 address that Desktop and Agent will use" 15
$serverAddress = $null
if (-not [System.Net.IPAddress]::TryParse($serverAddressText, [ref]$serverAddress) -or
    $serverAddress.AddressFamily -ne [System.Net.Sockets.AddressFamily]::InterNetwork -or
    $serverAddress.ToString() -cne $serverAddressText -or
    [System.Net.IPAddress]::IsLoopback($serverAddress)) {
    throw "The Server address must be a canonical, non-loopback IPv4 address reserved for this PC."
}
$serverAddressBytes = $serverAddress.GetAddressBytes()
if ($serverAddressBytes[0] -eq 0 -or $serverAddressBytes[0] -ge 224 -or $serverAddressBytes[3] -eq 255) {
    throw "The Server address must be a usable unicast IPv4 address."
}

$builder = [System.Data.Common.DbConnectionStringBuilder]::new()
$builder["Host"] = $hostName
$builder["Port"] = $port
$builder["Database"] = $database
$builder["Username"] = $dbUser
$builder["Password"] = $dbPassword
$builder["Timeout"] = 10
$builder["Command Timeout"] = 30
$builder["Pooling"] = $true
$connectionString = $builder.ConnectionString

$certificatePath = Join-Path $directory "server.pfx"
$publicCertificatePath = Join-Path $directory "server-trust.cer"
if ((Test-Path -LiteralPath $certificatePath) -or (Test-Path -LiteralPath $publicCertificatePath)) {
    throw "A Server TLS certificate already exists. Refusing to overwrite existing TLS material."
}

$certificate = $null
$secureCertificatePassword = $null
$certificatePassword = $null
$certificatePfxCreated = $false
$publicCertificateCreated = $false
$settingsFileCreated = $false
$clearBytes = $null
$protectedBytes = $null
$settings = $null
try {
    $certificatePassword = New-RandomSecret 48
    $secureCertificatePassword = ConvertTo-SecureString -String $certificatePassword -AsPlainText -Force

    $certificate = New-SelfSignedCertificate `
        -Type SSLServerAuthentication `
        -Subject "CN=GameNet Server" `
        -TextExtension @("2.5.29.17={text}IPAddress=$($serverAddress.ToString())") `
        -KeyAlgorithm RSA `
        -KeyLength 3072 `
        -HashAlgorithm SHA256 `
        -KeyExportPolicy Exportable `
        -CertStoreLocation "Cert:\\LocalMachine\\My" `
        -NotAfter (Get-Date).AddYears(2)
    if ($null -eq $certificate -or -not $certificate.HasPrivateKey) {
        throw "Could not create a Server TLS certificate with a private key."
    }

    $sanExtension = $certificate.Extensions | Where-Object { $_.Oid.Value -eq "2.5.29.17" } | Select-Object -First 1
    if ($null -eq $sanExtension -or $sanExtension.Format($true) -notmatch [regex]::Escape($serverAddress.ToString())) {
        throw "The generated Server TLS certificate does not contain the requested LAN IP in its Subject Alternative Name."
    }

    $ekuExtension = $certificate.Extensions | Where-Object { $_.Oid.Value -eq "2.5.29.37" } | Select-Object -First 1
    $hasServerAuthentication = $false
    if ($null -ne $ekuExtension) {
        $ekuOids = ([System.Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension]$ekuExtension).EnhancedKeyUsages
        foreach ($oid in $ekuOids) {
            if ($oid.Value -eq "1.3.6.1.5.5.7.3.1") { $hasServerAuthentication = $true }
        }
    }
    if (-not $hasServerAuthentication) {
        throw "The generated Server TLS certificate is missing the Server Authentication EKU."
    }

    $certificatePfxCreated = $true
    Export-PfxCertificate -Cert $certificate -FilePath $certificatePath -Password $secureCertificatePassword | Out-Null
    $publicCertificateCreated = $true
    Export-Certificate -Cert $certificate -FilePath $publicCertificatePath -Type CERT | Out-Null

    if (-not (Test-Path -LiteralPath $certificatePath -PathType Leaf) -or
        (Get-Item -LiteralPath $certificatePath).Length -lt 1024) {
        throw "Server TLS private certificate export is missing or unexpectedly small."
    }
    $publicCertificate = Get-PfxCertificate -FilePath $publicCertificatePath
    if ($null -eq $publicCertificate -or $publicCertificate.HasPrivateKey -or
        $publicCertificate.Thumbprint -ne $certificate.Thumbprint) {
        throw "The exported public Server certificate did not match the generated certificate."
    }

    foreach ($certificateFile in @($certificatePath, $publicCertificatePath)) {
        & icacls.exe $certificateFile /inheritance:r /grant:r '*S-1-5-18:(F)' '*S-1-5-32-544:(F)' | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "Could not restrict Server TLS certificate file ACLs." }
    }

    # The private key is retained only in the ACL-protected PFX; do not leave a second store copy.
    $storeCertificatePath = "Cert:\\LocalMachine\\My\\$($certificate.Thumbprint)"
    Remove-Item -LiteralPath $storeCertificatePath -Force

    $settings = [ordered]@{
        "GameNet:DatabaseConnectionString" = $connectionString
        "GameNet:Authentication:Enabled" = "true"
        "GameNet:Authentication:Issuer" = "GameNet5.Server"
        "GameNet:Authentication:Audience" = "GameNet5.Desktop"
        "GameNet:Authentication:SigningKey" = (New-RandomSecret 48)
        "GameNet:Agent:ProvisioningKey" = (New-RandomSecret 48)
        "GameNet:Setup:BootstrapSecret" = $bootstrapSecret
        "Kestrel:Endpoints:Https:Certificate:Path" = [System.IO.Path]::GetFullPath($certificatePath)
        "Kestrel:Endpoints:Https:Certificate:Password" = $certificatePassword
    }

    $clearBytes = [System.Text.Encoding]::UTF8.GetBytes(($settings | ConvertTo-Json -Compress))
    $protectedBytes = [Security.Cryptography.ProtectedData]::Protect(
        $clearBytes,
        $null,
        [Security.Cryptography.DataProtectionScope]::LocalMachine)
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

    & icacls.exe $fullDestination /inheritance:r /grant:r '*S-1-5-18:(F)' '*S-1-5-32-544:(F)' | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Could not restrict protected settings file ACLs." }

    Write-Host "Protected Server settings and TLS certificate were written and ACL-restricted."
    Write-Host "The Server will listen on HTTPS port 5081; use https://$($serverAddress.ToString()):5081 from the LAN."
    Write-Host "Public certificate file: $publicCertificatePath"
    Write-Host ("SHA-256 certificate fingerprint: {0}" -f (Get-FileHash -LiteralPath $publicCertificatePath -Algorithm SHA256).Hash)
    Write-Host "Copy only server-trust.cer to each client PC; verify this fingerprint out of band before trusting it."
    Write-Host "Never distribute server.pfx. No secret values were displayed or written to plaintext configuration."
    Write-Host "Re-enter the bootstrap secret when running scripts/bootstrap-admin.ps1."
}
catch {
    if ($settingsFileCreated -and (Test-Path -LiteralPath $fullDestination)) {
        Remove-Item -LiteralPath $fullDestination -Force -ErrorAction SilentlyContinue
    }
    if ($certificatePfxCreated -and (Test-Path -LiteralPath $certificatePath)) {
        Remove-Item -LiteralPath $certificatePath -Force -ErrorAction SilentlyContinue
    }
    if ($publicCertificateCreated -and (Test-Path -LiteralPath $publicCertificatePath)) {
        Remove-Item -LiteralPath $publicCertificatePath -Force -ErrorAction SilentlyContinue
    }
    if ($null -ne $certificate) {
        $storeCertificatePath = "Cert:\\LocalMachine\\My\\$($certificate.Thumbprint)"
        if (Test-Path -LiteralPath $storeCertificatePath) {
            Remove-Item -LiteralPath $storeCertificatePath -Force -ErrorAction SilentlyContinue
        }
    }
    throw
}
finally {
    if ($null -ne $certificate) { $certificate.Dispose() }
    if ($null -ne $secureCertificatePassword) { $secureCertificatePassword.Dispose() }
    if ($null -ne $clearBytes) { [Security.Cryptography.CryptographicOperations]::ZeroMemory($clearBytes) }
    if ($null -ne $protectedBytes) { [Security.Cryptography.CryptographicOperations]::ZeroMemory($protectedBytes) }
    $connectionString = $null
    $dbPassword = $null
    $bootstrapSecret = $null
    $certificatePassword = $null
    $settings = $null
}
