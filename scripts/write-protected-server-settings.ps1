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

$settings = [ordered]@{
    "GameNet:DatabaseConnectionString" = $connectionString
    "GameNet:Authentication:Enabled" = "true"
    "GameNet:Authentication:Issuer" = "GameNet5.Server"
    "GameNet:Authentication:Audience" = "GameNet5.Desktop"
    "GameNet:Authentication:SigningKey" = (New-RandomSecret 48)
    "GameNet:Agent:ProvisioningKey" = (New-RandomSecret 48)
    "GameNet:Setup:BootstrapSecret" = $bootstrapSecret
}

$clearBytes = [System.Text.Encoding]::UTF8.GetBytes(($settings | ConvertTo-Json -Compress))
$protectedBytes = $null
try {
    $protectedBytes = [Security.Cryptography.ProtectedData]::Protect(
        $clearBytes,
        $null,
        [Security.Cryptography.DataProtectionScope]::LocalMachine)
    $stream = [System.IO.File]::Open(
        $fullDestination,
        [System.IO.FileMode]::CreateNew,
        [System.IO.FileAccess]::Write,
        [System.IO.FileShare]::None)
    try {
        $stream.Write($protectedBytes, 0, $protectedBytes.Length)
        $stream.Flush($true)
    }
    finally {
        $stream.Dispose()
    }
}
finally {
    [Security.Cryptography.CryptographicOperations]::ZeroMemory($clearBytes)
    if ($null -ne $protectedBytes) {
        [Security.Cryptography.CryptographicOperations]::ZeroMemory($protectedBytes)
    }
    $connectionString = $null
    $dbPassword = $null
    $bootstrapSecret = $null
    $settings = $null
}

# The Server service is initially installed as LocalSystem; service-account changes must update ACLs.
& icacls.exe $fullDestination /inheritance:r /grant:r '*S-1-5-18:(F)' '*S-1-5-32-544:(F)' | Out-Null
if ($LASTEXITCODE -ne 0) { throw "Could not restrict protected settings file ACLs." }

Write-Host "Protected Server settings were written and ACL-restricted."
Write-Host "No secret values were displayed or written to a plaintext settings file."
Write-Host "Re-enter the bootstrap secret when running scripts/bootstrap-admin.ps1."
