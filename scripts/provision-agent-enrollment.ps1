[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateLength(1, 128)]
    [string]$DeviceId
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest
if ($DeviceId -ne $DeviceId.Trim() -or $DeviceId -notmatch '^[A-Za-z0-9._-]{1,128}$') {
    throw "DeviceId must contain 1-128 ASCII letters, digits, dots, dashes or underscores."
}
$principal = [Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw "Run this script elevated on the dedicated Agent PC."
}
if (-not [System.OperatingSystem]::IsWindows()) {
    throw "Agent enrollment-token provisioning requires Windows."
}
try { Add-Type -AssemblyName System.Security.Cryptography.ProtectedData -ErrorAction Stop }
catch { Add-Type -AssemblyName System.Security -ErrorAction Stop }

$common = [Environment]::GetFolderPath([Environment+SpecialFolder]::CommonApplicationData)
$managerRoot = [System.IO.Path]::GetFullPath((Join-Path $common "GameNet Manager"))
$configDirectory = [System.IO.Path]::GetFullPath((Join-Path $managerRoot "Config"))
$agentStateRoot = [System.IO.Path]::GetFullPath((Join-Path $managerRoot "Agent"))
$agentConfigurationPath = Join-Path $configDirectory "agent.json"
$identityPath = Join-Path $agentStateRoot "identity.json"
$credentialPath = Join-Path $agentStateRoot "credential.bin"
$tokenPath = Join-Path $agentStateRoot "enrollment-token.dpapi"
$serviceName = "GameNet 5 Agent"
$serviceSidName = "NT SERVICE" + [char]92 + $serviceName

function Assert-NoReparsePoint([string]$Path, [bool]$IsDirectory) {
    if (-not (Test-Path -LiteralPath $Path)) { throw "A required Agent setup path is missing; no token was issued." }
    $item = Get-Item -LiteralPath $Path -Force
    if (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0 -or $IsDirectory -ne $item.PSIsContainer) {
        throw "A required Agent setup path is an unexpected filesystem object; no token was issued."
    }
}
function Assert-ExactDacl([string]$Path, [hashtable]$ExpectedRights) {
    $acl = Get-Acl -LiteralPath $Path
    if (-not $acl.AreAccessRulesProtected) { throw "Agent state ACL inheritance must be disabled before enrollment; no token was issued." }
    $rules = @($acl.GetAccessRules($true, $false, [System.Security.Principal.SecurityIdentifier]))
    if ($rules.Count -ne $ExpectedRights.Count) { throw "Agent setup ACL does not match the approved service-state policy; no token was issued." }
    foreach ($rule in $rules) {
        $sid = $rule.IdentityReference.Value
        if ($rule.AccessControlType -ne [System.Security.AccessControl.AccessControlType]::Allow -or -not $ExpectedRights.ContainsKey($sid) -or [int]$rule.FileSystemRights -ne [int]$ExpectedRights[$sid]) {
            throw "Agent setup ACL includes an unexpected principal or permission; no token was issued."
        }
    }
}
function ConvertFrom-SecurePrompt([System.Security.SecureString]$Value) {
    $pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($Value)
    try { return [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer) }
    finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer) }
}
function Clear-ByteArray([byte[]]$Bytes) {
    if ($null -ne $Bytes -and $Bytes.Length -gt 0) { [System.Security.Cryptography.CryptographicOperations]::ZeroMemory($Bytes) }
}
function New-EnrollmentTokenFileSecurity {
    $acl = [System.Security.AccessControl.FileSecurity]::new()
    $acl.SetAccessRuleProtection($true, $false)
    $acl.AddAccessRule([System.Security.AccessControl.FileSystemAccessRule]::new($systemSid, [System.Security.AccessControl.FileSystemRights]::FullControl, [System.Security.AccessControl.AccessControlType]::Allow))
    $acl.AddAccessRule([System.Security.AccessControl.FileSystemAccessRule]::new($administratorsSid, [System.Security.AccessControl.FileSystemRights]::FullControl, [System.Security.AccessControl.AccessControlType]::Allow))
    $acl.AddAccessRule([System.Security.AccessControl.FileSystemAccessRule]::new($agentServiceSid, [System.Security.AccessControl.FileSystemRights]::Read, [System.Security.AccessControl.AccessControlType]::Allow))
    return $acl
}

# The service must already be installed, stopped, and provisioned by the reviewed Agent state helper.
Assert-NoReparsePoint $managerRoot $true
Assert-NoReparsePoint $configDirectory $true
Assert-NoReparsePoint $agentStateRoot $true
Assert-NoReparsePoint $agentConfigurationPath $false
$service = @(Get-CimInstance -ClassName Win32_Service | Where-Object { $_.Name -eq $serviceName -or $_.DisplayName -eq $serviceName } | Select-Object -First 1)
if ($service.Count -ne 1) { throw "Register the Agent service using the reviewed test-only procedure first; this script never registers services." }
$service = $service[0]
if ($service.State -ne "Stopped") { throw "Stop the Agent service before provisioning its one-time token. This script will not stop a running service." }
$slash = [string][char]92
$logonAccount = ([string]$service.StartName).Replace("/", $slash).Trim().ToLowerInvariant()
if ($logonAccount -notin @("nt authority" + $slash + "localservice", "localservice")) { throw "The Agent service must run as NT AUTHORITY\LocalService." }
$sc = Join-Path $env:SystemRoot "System32\sc.exe"
if (-not (Test-Path -LiteralPath $sc -PathType Leaf)) { throw "Windows sc.exe is unavailable; no token was issued." }
$agentServiceSid = ([System.Security.Principal.NTAccount]::new($serviceSidName)).Translate([System.Security.Principal.SecurityIdentifier])
$sidOutput = (& $sc qsidtype $service.Name 2>&1 | Out-String)
if ($LASTEXITCODE -ne 0 -or $sidOutput -notmatch 'SERVICE_SID_TYPE:\s*UNRESTRICTED') {
    throw "The Agent service SID is not enabled as UNRESTRICTED. Run the reviewed Agent state-provisioning helper first."
}
$systemSid = [System.Security.Principal.SecurityIdentifier]::new("S-1-5-18")
$administratorsSid = [System.Security.Principal.SecurityIdentifier]::new("S-1-5-32-544")
$usersSid = [System.Security.Principal.SecurityIdentifier]::new("S-1-5-32-545")
$expectedParentAcl = @{}
$expectedParentAcl[$systemSid.Value] = [System.Security.AccessControl.FileSystemRights]::FullControl
$expectedParentAcl[$administratorsSid.Value] = [System.Security.AccessControl.FileSystemRights]::FullControl
$expectedParentAcl[$usersSid.Value] = [System.Security.AccessControl.FileSystemRights]::ReadAndExecute
$expectedParentAcl[$agentServiceSid.Value] = [System.Security.AccessControl.FileSystemRights]::ReadAndExecute
$expectedStateAcl = @{}
$expectedStateAcl[$systemSid.Value] = [System.Security.AccessControl.FileSystemRights]::FullControl
$expectedStateAcl[$administratorsSid.Value] = [System.Security.AccessControl.FileSystemRights]::FullControl
$expectedStateAcl[$agentServiceSid.Value] = [System.Security.AccessControl.FileSystemRights]::Modify
Assert-ExactDacl $managerRoot $expectedParentAcl
Assert-ExactDacl $configDirectory $expectedParentAcl
Assert-ExactDacl $agentStateRoot $expectedStateAcl

# Do not overwrite or silently re-enroll an existing identity, credential, or potentially consumed token.
foreach ($path in @($identityPath, $credentialPath, $tokenPath)) {
    if (Test-Path -LiteralPath $path) {
        Assert-NoReparsePoint $path $false
        throw "Existing Agent identity, credential, or enrollment-token state was found. No token was issued; use the reviewed recovery procedure."
    }
}

$config = Get-Content -LiteralPath $agentConfigurationPath -Raw | ConvertFrom-Json -AsHashtable
if ($null -eq $config -or -not ($config -is [System.Collections.IDictionary])) { throw "agent.json is invalid; no enrollment token was issued." }
if (-not $config.Contains("GameNet") -or $null -eq $config["GameNet"]) { $config["GameNet"] = [ordered]@{} }
if (-not ($config["GameNet"] -is [System.Collections.IDictionary])) { throw "agent.json GameNet section is invalid; no enrollment token was issued." }
if (-not $config["GameNet"].Contains("AgentIdentity") -or $null -eq $config["GameNet"]["AgentIdentity"]) { $config["GameNet"]["AgentIdentity"] = [ordered]@{} }
if (-not ($config["GameNet"]["AgentIdentity"] -is [System.Collections.IDictionary])) { throw "agent.json AgentIdentity section is invalid; no enrollment token was issued." }
$existingDeviceId = [string]$config["GameNet"]["AgentIdentity"]["DeviceId"]
if (-not [string]::IsNullOrWhiteSpace($existingDeviceId) -and -not [string]::Equals($existingDeviceId, $DeviceId, [StringComparison]::Ordinal)) {
    throw "agent.json already declares a different DeviceId; no enrollment token was issued."
}
$transportSection = $config["GameNet"]["AgentTransport"]
if ($null -eq $transportSection) { throw "agent.json is missing GameNet.AgentTransport; no enrollment token was issued." }
$serverBaseUrl = [string]$transportSection["ServerBaseUrl"]
try { $serverUri = [Uri]::new($serverBaseUrl, [UriKind]::Absolute) }
catch { throw "agent.json does not contain a valid ServerBaseUrl; no enrollment token was issued." }
if ($serverUri.Scheme -ne [Uri]::UriSchemeHttps -or -not [string]::IsNullOrEmpty($serverUri.UserInfo) -or $serverUri.AbsolutePath -notin @("", "/") -or -not [string]::IsNullOrEmpty($serverUri.Query) -or -not [string]::IsNullOrEmpty($serverUri.Fragment)) {
    throw "Agent enrollment requires the exact root HTTPS origin already configured in agent.json."
}
$baseUrl = $serverUri.GetLeftPart([UriPartial]::Authority).TrimEnd("/")

$securePassword = $null
$username = $null
$password = $null
$accessToken = $null
$loginPayload = $null
$issuePayload = $null
$enrollment = $null
$tokenText = $null
$clearBytes = $null
$entropy = $null
$protectedBytes = $null
$verificationBytes = $null
$verificationClearBytes = $null
$tempPath = $null
$configTempPath = $null
$tokenCommitted = $false
$tokenId = $null
$tokenExpires = $null
$headers = @{ "X-GameNet-Contract" = "v1" }

try {
    $username = (Read-Host "Operator username").Trim()
    if ([string]::IsNullOrWhiteSpace($username) -or $username.Length -gt 64) { throw "Operator username is invalid." }
    $securePassword = Read-Host "Operator password (requires agents.enrollment.manage permission)" -AsSecureString
    $password = ConvertFrom-SecurePrompt $securePassword
    if ([string]::IsNullOrEmpty($password) -or $password.Length -gt 256) { throw "Operator password is invalid." }
    $loginPayload = @{ username = $username; password = $password } | ConvertTo-Json -Compress
    try { $login = Invoke-RestMethod -Method Post -Uri "$baseUrl/api/v1/auth/login" -Headers $headers -ContentType "application/json" -Body $loginPayload -TimeoutSec 15 }
    catch { throw "Operator sign-in failed or HTTPS was unavailable. No Agent enrollment token was issued." }
    $accessToken = [string]$login.data.accessToken
    if ([string]::IsNullOrWhiteSpace($accessToken)) { throw "Server did not return an operator access token. No Agent enrollment token was issued." }

    $authorizationHeaders = @{ "X-GameNet-Contract" = "v1"; Authorization = "Bearer $accessToken" }
    $issuePayload = @{ deviceId = $DeviceId } | ConvertTo-Json -Compress
    try { $enrollment = Invoke-RestMethod -Method Post -Uri "$baseUrl/api/v1/agent/enrollment-tokens" -Headers $authorizationHeaders -ContentType "application/json" -Body $issuePayload -TimeoutSec 15 }
    catch { throw "Server refused to issue an enrollment token. Confirm agents.enrollment.manage permission; no local token was written." }

    $tokenId = [Guid]$enrollment.tokenId
    $issuedDeviceId = [string]$enrollment.deviceId
    $tokenText = [string]$enrollment.token
    $tokenExpires = [DateTimeOffset]::Parse([string]$enrollment.expiresAtUtc).ToUniversalTime()
    if ($tokenId -eq [Guid]::Empty -or -not [string]::Equals($issuedDeviceId, $DeviceId, [StringComparison]::Ordinal) -or $tokenText -notmatch '^[A-Za-z0-9_-]{43}$' -or $tokenExpires -le [DateTimeOffset]::UtcNow -or $tokenExpires -gt [DateTimeOffset]::UtcNow.AddMinutes(16)) {
        throw "Server returned an invalid or unexpected enrollment response; local token was not written."
    }

    $config["GameNet"]["AgentIdentity"]["DeviceId"] = $DeviceId
    $configJson = ConvertTo-Json -InputObject $config -Depth 12
    $configTempPath = $agentConfigurationPath + "." + [Guid]::NewGuid().ToString("N") + ".tmp"
    [System.IO.File]::WriteAllText($configTempPath, $configJson + [Environment]::NewLine, [System.Text.UTF8Encoding]::new($false))
    [System.IO.File]::Replace($configTempPath, $agentConfigurationPath, $null)
    $configTempPath = $null
    $configJson = $null

    $payload = [ordered]@{ formatVersion = 1; deviceId = $DeviceId; token = $tokenText; expiresAtUtc = $tokenExpires.ToString("O") }
    $clearBytes = [System.Text.Encoding]::UTF8.GetBytes(($payload | ConvertTo-Json -Compress))
    $entropy = [System.Text.Encoding]::UTF8.GetBytes("GameNet.AgentEnrollmentToken.v1|" + $DeviceId)
    $protectedBytes = [System.Security.Cryptography.ProtectedData]::Protect($clearBytes, $entropy, [System.Security.Cryptography.DataProtectionScope]::LocalMachine)

    $tempPath = Join-Path $agentStateRoot (".enrollment-token-" + [Guid]::NewGuid().ToString("N") + ".tmp")
    $tokenAcl = New-EnrollmentTokenFileSecurity
    $stream = [System.IO.FileSystemAclExtensions]::Create([System.IO.FileInfo]::new($tempPath), [System.IO.FileMode]::CreateNew, ([System.Security.AccessControl.FileSystemRights]::WriteData -bor [System.Security.AccessControl.FileSystemRights]::ReadAttributes), [System.IO.FileShare]::None, 4096, [System.IO.FileOptions]::WriteThrough, $tokenAcl)
    try { $stream.Write($protectedBytes, 0, $protectedBytes.Length); $stream.Flush($true) }
    finally { $stream.Dispose() }

    $expectedTokenAcl = @{}
    $expectedTokenAcl[$systemSid.Value] = [System.Security.AccessControl.FileSystemRights]::FullControl
    $expectedTokenAcl[$administratorsSid.Value] = [System.Security.AccessControl.FileSystemRights]::FullControl
    $expectedTokenAcl[$agentServiceSid.Value] = [System.Security.AccessControl.FileSystemRights]::Read
    Assert-ExactDacl $tempPath $expectedTokenAcl
    $verificationBytes = [System.IO.File]::ReadAllBytes($tempPath)
    $verificationClearBytes = [System.Security.Cryptography.ProtectedData]::Unprotect($verificationBytes, $entropy, [System.Security.Cryptography.DataProtectionScope]::LocalMachine)
    $verifiedPayload = [System.Text.Encoding]::UTF8.GetString($verificationClearBytes) | ConvertFrom-Json
    if ($verifiedPayload.formatVersion -ne 1 -or -not [string]::Equals([string]$verifiedPayload.deviceId, $DeviceId, [StringComparison]::Ordinal) -or -not [string]::Equals([string]$verifiedPayload.token, $tokenText, [StringComparison]::Ordinal) -or [DateTimeOffset]::Parse([string]$verifiedPayload.expiresAtUtc) -ne $tokenExpires) {
        throw "Protected Agent enrollment token verification failed; the token was not installed."
    }
    $verifiedPayload = $null
    Clear-ByteArray $verificationBytes
    Clear-ByteArray $verificationClearBytes
    $verificationBytes = $null
    $verificationClearBytes = $null

    [System.IO.File]::Move($tempPath, $tokenPath, $false)
    $tempPath = $null
    Assert-ExactDacl $tokenPath $expectedTokenAcl
    $tokenCommitted = $true

    Write-Host "Protected one-time enrollment token is ready for DeviceId $DeviceId."
    Write-Host ("Token expires at {0:u} UTC." -f $tokenExpires.UtcDateTime)
    Write-Host "Token material was not printed or placed in an environment variable."
    Write-Host "Start the already-registered Agent service only in the isolated test environment."
}
catch {
    # Do not echo unexpected exception details: HTTP/service errors can contain request
    # or response fragments. Preflight failures above this block remain specific.
    throw "Secure Agent enrollment-token provisioning failed. No token value was written to console output. Review the endpoint, service, ACL and operator permission, then retry."
}
finally {
    if (-not $tokenCommitted -and $null -ne $tokenId -and $null -ne $accessToken) {
        try {
            $revokeHeaders = @{ "X-GameNet-Contract" = "v1"; Authorization = "Bearer $accessToken" }
            $revokeBody = @{ reason = "Local DPAPI enrollment-token handoff failed; issue a new token after review." } | ConvertTo-Json -Compress
            Invoke-RestMethod -Method Post -Uri "$baseUrl/api/v1/agent/enrollment-tokens/$tokenId/revoke" -Headers $revokeHeaders -ContentType "application/json" -Body $revokeBody -TimeoutSec 10 | Out-Null
        }
        catch { }
    }
    if (-not $tokenCommitted -and (Test-Path -LiteralPath $tokenPath -PathType Leaf)) { Remove-Item -LiteralPath $tokenPath -Force -ErrorAction SilentlyContinue }
    if (-not [string]::IsNullOrWhiteSpace($accessToken)) {
        try {
            $logoutHeaders = @{ "X-GameNet-Contract" = "v1"; Authorization = "Bearer $accessToken" }
            Invoke-RestMethod -Method Post -Uri "$baseUrl/api/v1/auth/logout" -Headers $logoutHeaders -TimeoutSec 10 | Out-Null
        }
        catch { }
    }
    if ($securePassword) { $securePassword.Dispose() }
    $password = $null
    $accessToken = $null
    $loginPayload = $null
    $issuePayload = $null
    $enrollment = $null
    $tokenText = $null
    $payload = $null
    Clear-ByteArray $clearBytes
    Clear-ByteArray $entropy
    Clear-ByteArray $protectedBytes
    Clear-ByteArray $verificationBytes
    Clear-ByteArray $verificationClearBytes
    if ($tempPath -and (Test-Path -LiteralPath $tempPath -PathType Leaf)) { Remove-Item -LiteralPath $tempPath -Force -ErrorAction SilentlyContinue }
    if ($configTempPath -and (Test-Path -LiteralPath $configTempPath -PathType Leaf)) { Remove-Item -LiteralPath $configTempPath -Force -ErrorAction SilentlyContinue }
}
