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
        [Array]::Clear($Bytes, 0, $Bytes.Length)
    }
}

function New-RandomSecret([int]$ByteCount = 48) {
    $bytes = New-Object byte[] $ByteCount
    $random = [Security.Cryptography.RandomNumberGenerator]::Create()
    try {
        $random.GetBytes($bytes)
        $value = [Convert]::ToBase64String($bytes)
        return $value.TrimEnd('=').Replace('+', '-').Replace('/', '_')
    }
    finally {
        $random.Dispose()
        Clear-ByteArray $bytes
    }
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

$certificateInfo = $null
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

    Import-Module -Name (Join-Path $PSScriptRoot "modules/ServerTlsCertificate.psm1") -Force
    $certificateOptions = @{
        ServerAddress = $serverAddress
        PfxPath = $certificatePath
        PublicCertificatePath = $publicCertificatePath
        Password = $secureCertificatePassword
        CertificateStoreLocation = "Cert:\LocalMachine\My"
    }
    $certificateInfo = New-GameNetServerTlsCertificate @certificateOptions
    $certificatePfxCreated = $true
    $publicCertificateCreated = $true

    foreach ($certificateFile in @($certificatePath, $publicCertificatePath)) {
        & icacls.exe $certificateFile /inheritance:r /grant:r '*S-1-5-18:(F)' '*S-1-5-32-544:(F)' | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "Could not restrict Server TLS certificate file ACLs." }
    }

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
    Write-Host ("SHA-256 certificate fingerprint: {0}" -f $certificateInfo.Sha256Fingerprint)
    Write-Host ("Certificate expires (UTC): {0}" -f $certificateInfo.ExpiresUtc)
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
    throw
}
finally {
    if ($null -ne $secureCertificatePassword) { $secureCertificatePassword.Dispose() }
    Clear-ByteArray $clearBytes
    Clear-ByteArray $protectedBytes
    $connectionString = $null
    $dbPassword = $null
    $bootstrapSecret = $null
    $certificatePassword = $null
    $certificateInfo = $null
    $settings = $null
}