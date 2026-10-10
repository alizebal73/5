[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ServerBaseUrl
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$principal = [Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw "Run this script from an elevated PowerShell session on the GameNet Server machine."
}

if ([string]::IsNullOrWhiteSpace($env:ProgramData)) {
    throw "ProgramData is unavailable; no settings file was changed."
}

$managerRoot = [System.IO.Path]::GetFullPath((Join-Path $env:ProgramData "GameNet Manager"))
$configDirectory = [System.IO.Path]::GetFullPath((Join-Path $managerRoot "Config"))
$settingsPath = [System.IO.Path]::GetFullPath((Join-Path $configDirectory "server-secrets.bin"))
$expectedPath = [System.IO.Path]::GetFullPath((Join-Path $env:ProgramData "GameNet Manager\Config\server-secrets.bin"))
if (-not [string]::Equals($settingsPath, $expectedPath, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Protected Server settings path is not canonical; no file was changed."
}

try { $serverUri = [Uri]::new($ServerBaseUrl, [UriKind]::Absolute) }
catch { throw "ServerBaseUrl must be an absolute HTTPS URL matching the local server.json listener." }
if ($serverUri.Scheme -ne [Uri]::UriSchemeHttps) {
    throw "Use the exact HTTPS listener configured in server.json; remote HTTP is not accepted."
}
if (-not [string]::IsNullOrEmpty($serverUri.UserInfo) -or $serverUri.AbsolutePath -notin @("", "/") -or
    -not [string]::IsNullOrEmpty($serverUri.Query) -or -not [string]::IsNullOrEmpty($serverUri.Fragment)) {
    throw "ServerBaseUrl must be the server origin without credentials, path, query, or fragment."
}

$serverConfigurationPath = Join-Path $configDirectory "server.json"
if (-not (Test-Path -LiteralPath $managerRoot -PathType Container) -or
    -not (Test-Path -LiteralPath $configDirectory -PathType Container) -or
    -not (Test-Path -LiteralPath $serverConfigurationPath -PathType Leaf) -or
    -not (Test-Path -LiteralPath $settingsPath -PathType Leaf)) {
    throw "The canonical Server settings path is unavailable. No settings were changed."
}
foreach ($candidate in @($managerRoot, $configDirectory, $serverConfigurationPath, $settingsPath)) {
    $item = Get-Item -LiteralPath $candidate -Force
    if (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "The Server configuration path contains a reparse point. No settings were changed."
    }
}

try {
    Add-Type -AssemblyName System.Security.Cryptography.ProtectedData -ErrorAction Stop
}
catch {
    Add-Type -AssemblyName System.Security -ErrorAction Stop
}

$systemSid = [System.Security.Principal.SecurityIdentifier]::new("S-1-5-18")
$administratorsSid = [System.Security.Principal.SecurityIdentifier]::new("S-1-5-32-544")
$serviceSid = ([System.Security.Principal.NTAccount]::new("NT SERVICE\GameNet 5 Server")).Translate(
    [System.Security.Principal.SecurityIdentifier])

function Assert-RestrictedSettingsAcl([string]$Path) {
    $acl = Get-Acl -LiteralPath $Path
    if (-not $acl.AreAccessRulesProtected) {
        throw "Protected settings ACL inheritance is enabled. No settings were changed."
    }

    $rules = @($acl.GetAccessRules($true, $false, [System.Security.Principal.SecurityIdentifier]))
    if ($rules.Count -ne 3) {
        throw "Protected settings ACL is not the expected restricted three-principal ACL. No settings were changed."
    }

    $expected = [System.Collections.Generic.Dictionary[string, System.Security.AccessControl.FileSystemRights]]::new([StringComparer]::Ordinal)
    $expected.Add($systemSid.Value, [System.Security.AccessControl.FileSystemRights]::FullControl)
    $expected.Add($administratorsSid.Value, [System.Security.AccessControl.FileSystemRights]::FullControl)
    $expected.Add($serviceSid.Value, [System.Security.AccessControl.FileSystemRights]::Read)

    foreach ($rule in $rules) {
        $sid = $rule.IdentityReference.Value
        if ($rule.AccessControlType -ne [System.Security.AccessControl.AccessControlType]::Allow -or
            -not $expected.ContainsKey($sid) -or
            [int]$rule.FileSystemRights -ne [int]$expected[$sid]) {
            throw "Protected settings ACL has an unexpected principal or permission. No settings were changed."
        }
        $expected.Remove($sid)
    }
    if ($expected.Count -ne 0) {
        throw "Protected settings ACL is missing a required principal. No settings were changed."
    }
}

function Assert-RestrictedDirectoryAcl([string]$Path) {
    $acl = Get-Acl -LiteralPath $Path
    if (-not $acl.AreAccessRulesProtected) {
        throw "Protected Server directory ACL inheritance is enabled. No settings were changed."
    }
    $rules = @($acl.GetAccessRules($true, $false, [System.Security.Principal.SecurityIdentifier]))
    if ($rules.Count -ne 4) {
        throw "Protected Server directory ACL is not the expected restricted four-principal ACL. No settings were changed."
    }
    $usersSid = [System.Security.Principal.SecurityIdentifier]::new("S-1-5-32-545")
    $expected = [System.Collections.Generic.Dictionary[string, System.Security.AccessControl.FileSystemRights]]::new([StringComparer]::Ordinal)
    $expected.Add($systemSid.Value, [System.Security.AccessControl.FileSystemRights]::FullControl)
    $expected.Add($administratorsSid.Value, [System.Security.AccessControl.FileSystemRights]::FullControl)
    $expected.Add($usersSid.Value, [System.Security.AccessControl.FileSystemRights]::ReadAndExecute)
    $expected.Add($serviceSid.Value, [System.Security.AccessControl.FileSystemRights]::ReadAndExecute)
    foreach ($rule in $rules) {
        $sid = $rule.IdentityReference.Value
        if ($rule.AccessControlType -ne [System.Security.AccessControl.AccessControlType]::Allow -or
            -not $expected.ContainsKey($sid) -or
            [int]$rule.FileSystemRights -ne [int]$expected[$sid]) {
            throw "Protected Server directory ACL has an unexpected principal or permission. No settings were changed."
        }
        $expected.Remove($sid)
    }
    if ($expected.Count -ne 0) {
        throw "Protected Server directory ACL is missing a required principal. No settings were changed."
    }
}

function New-RestrictedSettingsAcl {
    $acl = [System.Security.AccessControl.FileSecurity]::new()
    $acl.SetAccessRuleProtection($true, $false)
    $acl.AddAccessRule([System.Security.AccessControl.FileSystemAccessRule]::new(
        $systemSid, [System.Security.AccessControl.FileSystemRights]::FullControl,
        [System.Security.AccessControl.AccessControlType]::Allow))
    $acl.AddAccessRule([System.Security.AccessControl.FileSystemAccessRule]::new(
        $administratorsSid, [System.Security.AccessControl.FileSystemRights]::FullControl,
        [System.Security.AccessControl.AccessControlType]::Allow))
    $acl.AddAccessRule([System.Security.AccessControl.FileSystemAccessRule]::new(
        $serviceSid, [System.Security.AccessControl.FileSystemRights]::Read,
        [System.Security.AccessControl.AccessControlType]::Allow))
    return $acl
}

function Clear-ByteArray([byte[]]$Bytes) {
    if ($null -ne $Bytes -and $Bytes.Length -gt 0) {
        [System.Security.Cryptography.CryptographicOperations]::ZeroMemory($Bytes)
    }
}

Assert-RestrictedDirectoryAcl $managerRoot
Assert-RestrictedDirectoryAcl $configDirectory
Assert-RestrictedSettingsAcl $settingsPath

$item = Get-Item -LiteralPath $settingsPath -Force
if ($item.Length -le 0 -or $item.Length -gt 65536) {
    throw "Protected settings file size is invalid. No file was changed."
}

$serverConfiguration = Get-Content -LiteralPath $serverConfigurationPath -Raw | ConvertFrom-Json -AsHashtable
$listenerText = [string]$serverConfiguration["urls"]
$listenerUrls = @($listenerText -split ";" | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | ForEach-Object { $_.Trim() })
if ($listenerUrls.Count -ne 1) {
    throw "server.json must declare exactly one HTTPS listener before setup can be finalized."
}
try { $listenerUri = [Uri]::new($listenerUrls[0], [UriKind]::Absolute) }
catch { throw "The configured Server listener URL is invalid. No settings were changed." }
if ($listenerUri.Scheme -ne [Uri]::UriSchemeHttps -or
    -not [string]::Equals($listenerUri.Host, $serverUri.Host, [StringComparison]::OrdinalIgnoreCase) -or
    $listenerUri.Port -ne $serverUri.Port) {
    throw "ServerBaseUrl does not match the local server.json HTTPS listener. No settings were changed."
}

# Now that the local listener identity and protected paths are verified, query the exact Server.
Get-Service -Name "GameNet 5 Server" -ErrorAction Stop | Out-Null
$headers = @{ "X-GameNet-Contract" = "v1" }
$baseUrl = $serverUri.GetLeftPart([UriPartial]::Authority).TrimEnd("/")
try {
    $status = Invoke-RestMethod -Method Get -Uri "$baseUrl/api/v1/bootstrap/status" -Headers $headers -TimeoutSec 15
}
catch {
    throw "Could not verify bootstrap status over the configured endpoint. No local file was changed."
}
if ($null -eq $status -or $null -eq $status.data -or $status.data.required -isnot [bool]) {
    throw "The Server returned an invalid bootstrap-status response. No local file was changed."
}
if ($status.data.required) {
    throw "No Owner is registered yet. The bootstrap secret was preserved and no local file was changed."
}

$encryptedBytes = $null
$clearBytes = $null
$protectedBytes = $null
$verificationBytes = $null
$verificationClearBytes = $null
$settings = $null
$verifiedSettings = $null
$json = $null
$tempPath = $null
try {
    $encryptedBytes = [System.IO.File]::ReadAllBytes($settingsPath)
    try {
        $clearBytes = [System.Security.Cryptography.ProtectedData]::Unprotect(
            $encryptedBytes, $null, [System.Security.Cryptography.DataProtectionScope]::LocalMachine)
    }
    catch {
        throw "Protected settings could not be decrypted on this machine. No settings were changed."
    }
    finally {
        Clear-ByteArray $encryptedBytes
        $encryptedBytes = $null
    }

    $settings = [System.Text.Encoding]::UTF8.GetString($clearBytes) | ConvertFrom-Json -AsHashtable
    foreach ($requiredKey in @(
        "GameNet:Authentication:Enabled",
        "GameNet:Authentication:Issuer",
        "GameNet:Authentication:Audience",
        "GameNet:ServerTls:CertificateThumbprint"
    )) {
        if (-not $settings.Contains($requiredKey) -or [string]::IsNullOrWhiteSpace([string]$settings[$requiredKey])) {
            throw "A required non-secret setup setting is missing. No settings were changed."
        }
    }
    $thumbprint = ([string]$settings["GameNet:ServerTls:CertificateThumbprint"]).Replace(" ", "").ToUpperInvariant()
    if ($settings["GameNet:Authentication:Enabled"] -ne "true" -or
        $thumbprint -notmatch '^[A-F0-9]{40}$') {
        throw "A required non-secret setup setting is invalid. No settings were changed."
    }

    if (-not $settings.Contains("GameNet:Setup:BootstrapSecret")) {
        Write-Host "The bootstrap secret is already absent. The protected settings file was not rewritten."
        return
    }

    $secretValue = [string]$settings["GameNet:Setup:BootstrapSecret"]
    if ([string]::IsNullOrWhiteSpace($secretValue) -or [Text.Encoding]::UTF8.GetByteCount($secretValue) -lt 32) {
        throw "The stored bootstrap secret is invalid. No settings were changed."
    }
    $secretValue = $null
    [void]$settings.Remove("GameNet:Setup:BootstrapSecret")

    $json = ConvertTo-Json -InputObject $settings -Compress -Depth 8
    $newClearBytes = [System.Text.Encoding]::UTF8.GetBytes($json)
    try {
        $protectedBytes = [System.Security.Cryptography.ProtectedData]::Protect(
            $newClearBytes, $null, [System.Security.Cryptography.DataProtectionScope]::LocalMachine)
    }
    finally {
        Clear-ByteArray $newClearBytes
    }

    $tempPath = Join-Path $configDirectory (".server-secrets-" + [Guid]::NewGuid().ToString("N") + ".tmp")
    $temporaryAcl = New-RestrictedSettingsAcl
    $stream = [System.IO.FileSystemAclExtensions]::Create(
        [System.IO.FileInfo]::new($tempPath),
        [System.IO.FileMode]::CreateNew,
        ([System.Security.AccessControl.FileSystemRights]::WriteData -bor [System.Security.AccessControl.FileSystemRights]::ReadAttributes),
        [System.IO.FileShare]::None,
        4096,
        [System.IO.FileOptions]::WriteThrough,
        $temporaryAcl)
    try {
        $stream.Write($protectedBytes, 0, $protectedBytes.Length)
        $stream.Flush($true)
    }
    finally {
        $stream.Dispose()
    }

    Assert-RestrictedSettingsAcl $tempPath
    # Verify the temporary DPAPI payload before replacing the original. Any failure here
    # leaves the original settings file and its bootstrap secret untouched.
    $verificationBytes = [System.IO.File]::ReadAllBytes($tempPath)
    $verificationClearBytes = [System.Security.Cryptography.ProtectedData]::Unprotect(
        $verificationBytes, $null, [System.Security.Cryptography.DataProtectionScope]::LocalMachine)
    $verifiedSettings = [System.Text.Encoding]::UTF8.GetString($verificationClearBytes) | ConvertFrom-Json -AsHashtable
    if ($verifiedSettings.Contains("GameNet:Setup:BootstrapSecret") -or
        $verifiedSettings["GameNet:Authentication:Enabled"] -ne $settings["GameNet:Authentication:Enabled"] -or
        $verifiedSettings["GameNet:Authentication:Issuer"] -ne $settings["GameNet:Authentication:Issuer"] -or
        $verifiedSettings["GameNet:Authentication:Audience"] -ne $settings["GameNet:Authentication:Audience"] -or
        $verifiedSettings["GameNet:ServerTls:CertificateThumbprint"] -ne $settings["GameNet:ServerTls:CertificateThumbprint"]) {
        throw "Temporary settings verification failed. The original file was not replaced."
    }
    $verifiedSettings = $null
    Clear-ByteArray $verificationBytes
    Clear-ByteArray $verificationClearBytes
    $verificationBytes = $null
    $verificationClearBytes = $null

    # Same-directory replacement is atomic on the supported local Windows filesystem.
    # No backup file is created because that would retain the old protected secret.
    [System.IO.File]::Replace($tempPath, $settingsPath, $null)
    $tempPath = $null

    Assert-RestrictedSettingsAcl $settingsPath
    $verificationBytes = [System.IO.File]::ReadAllBytes($settingsPath)
    $verificationClearBytes = [System.Security.Cryptography.ProtectedData]::Unprotect(
        $verificationBytes, $null, [System.Security.Cryptography.DataProtectionScope]::LocalMachine)
    $verifiedSettings = [System.Text.Encoding]::UTF8.GetString($verificationClearBytes) | ConvertFrom-Json -AsHashtable

    if ($verifiedSettings.Contains("GameNet:Setup:BootstrapSecret") -or
        $verifiedSettings["GameNet:Authentication:Enabled"] -ne $settings["GameNet:Authentication:Enabled"] -or
        $verifiedSettings["GameNet:Authentication:Issuer"] -ne $settings["GameNet:Authentication:Issuer"] -or
        $verifiedSettings["GameNet:Authentication:Audience"] -ne $settings["GameNet:Authentication:Audience"] -or
        $verifiedSettings["GameNet:ServerTls:CertificateThumbprint"] -ne $settings["GameNet:ServerTls:CertificateThumbprint"]) {
        throw "Post-write verification failed. Stop and inspect the protected settings file and ACLs."
    }

    Write-Host "Bootstrap secret removed from the protected settings file."
    Write-Host "The remaining setup settings and restricted ACL were verified."
    Write-Host "No backup containing the former bootstrap secret was created."
    Write-Host "The Server service was not restarted; schedule a controlled restart after confirming the Owner sign-in."
}
finally {
    Clear-ByteArray $encryptedBytes
    Clear-ByteArray $clearBytes
    Clear-ByteArray $protectedBytes
    Clear-ByteArray $verificationBytes
    Clear-ByteArray $verificationClearBytes
    $settings = $null
    $verifiedSettings = $null
    $json = $null
    if ($tempPath -and (Test-Path -LiteralPath $tempPath -PathType Leaf)) {
        Remove-Item -LiteralPath $tempPath -Force -ErrorAction SilentlyContinue
    }
}
