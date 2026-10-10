[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$principal = [Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw "Run this script from an elevated PowerShell session."
}

$common = [Environment]::GetFolderPath([Environment+SpecialFolder]::CommonApplicationData)
$managerRoot = [System.IO.Path]::GetFullPath((Join-Path $common "GameNet Manager"))
$configDirectory = Join-Path $managerRoot "Config"
$agentStateRoot = Join-Path $managerRoot "Agent"
$serverSettingsPath = Join-Path $configDirectory "server-secrets.bin"
$serverConfigPath = Join-Path $configDirectory "server.json"
$serverSecretStorePath = Join-Path (Join-Path $managerRoot "Secrets") "server-secrets.v1.dpapi"
$agentServiceName = "GameNet 5 Agent"
$serverServiceName = "GameNet 5 Server"

function Assert-NotReparsePoint {
    param([Parameter(Mandatory = $true)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        return
    }

    $item = Get-Item -LiteralPath $Path -Force
    if (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "Refusing to configure a path containing a reparse point: $Path"
    }
}

# This helper is intended for a dedicated Agent client. The Server uses a stricter
# DACL on the same product root; never replace that ACL from an Agent setup step.
$serverService = Get-CimInstance -ClassName Win32_Service |
    Where-Object { $_.Name -eq $serverServiceName -or $_.DisplayName -eq $serverServiceName } |
    Select-Object -First 1
$serverMarkers = @($serverConfigPath, $serverSettingsPath, $serverSecretStorePath) |
    Where-Object { Test-Path -LiteralPath $_ }
if ($null -ne $serverService -or $serverMarkers.Count -gt 0) {
    throw "This appears to be a Server/Manager machine. Run Agent state provisioning only on a dedicated Agent client; no ACLs were changed."
}

$agentService = Get-CimInstance -ClassName Win32_Service |
    Where-Object { $_.Name -eq $agentServiceName -or $_.DisplayName -eq $agentServiceName } |
    Select-Object -First 1
if ($null -eq $agentService) {
    throw "The '$agentServiceName' Windows service must be registered before Agent state provisioning."
}
if ($agentService.State -ne "Stopped") {
    throw "Stop '$agentServiceName' before configuring its service identity and state ACLs. This script will not stop a running service."
}

$logonAccount = ([string]$agentService.StartName).Replace("/", "\").Trim().ToLowerInvariant()
if ($logonAccount -notin @("nt authority\localservice", "localservice")) {
    throw "The Agent service must run as NT AUTHORITY\LocalService so DPAPI CurrentUser remains bound to the intended least-privilege service identity."
}

$sc = Join-Path $env:SystemRoot "System32\sc.exe"
if (-not (Test-Path -LiteralPath $sc -PathType Leaf)) {
    throw "sc.exe was not found; unable to verify the Agent service SID."
}
$serviceSid = ([System.Security.Principal.NTAccount]::new("NT SERVICE\$agentServiceName")).Translate(
    [System.Security.Principal.SecurityIdentifier])
$sidOutput = (& $sc showsid $agentService.Name 2>&1 | Out-String)
$sidExitCode = $LASTEXITCODE
if ($sidExitCode -ne 0 -or $sidOutput -notmatch "SERVICE SID:\s*(S-1-5-80-(?:\d+-?)+)") {
    throw "Windows did not return the expected dedicated Agent service SID."
}
if (-not [string]::Equals($Matches[1], $serviceSid.Value, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Windows' Agent service SID did not match the canonical NT SERVICE identity."
}

# Preflight all existing paths before changing service or ACL configuration.
Assert-NotReparsePoint -Path $managerRoot
Assert-NotReparsePoint -Path $configDirectory
Assert-NotReparsePoint -Path $agentStateRoot
if (Test-Path -LiteralPath $agentStateRoot -PathType Container) {
    $existingStateItems = @(Get-ChildItem -LiteralPath $agentStateRoot -Force -Recurse -ErrorAction Stop)
    foreach ($item in $existingStateItems) {
        if (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Refusing to change Agent state ACLs because a reparse point exists below the state directory."
        }
    }
}

# DPAPI CurrentUser uses the service logon account, while the filesystem ACL is granted
# to the dedicated service SID. Enable that SID before the service is next started.
& $sc sidtype $agentService.Name unrestricted | Out-Null
if ($LASTEXITCODE -ne 0) {
    throw "Could not enable the dedicated service SID for '$agentServiceName'."
}
$sidTypeOutput = (& $sc qsidtype $agentService.Name 2>&1 | Out-String)
$sidTypeExitCode = $LASTEXITCODE
if ($sidTypeExitCode -ne 0 -or $sidTypeOutput -notmatch "SERVICE_SID_TYPE:\s*UNRESTRICTED") {
    throw "The Agent service SID is not enabled as expected; refusing to write state ACLs."
}

$systemSid = [System.Security.Principal.SecurityIdentifier]::new("S-1-5-18")
$administratorsSid = [System.Security.Principal.SecurityIdentifier]::new("S-1-5-32-544")
$usersSid = [System.Security.Principal.SecurityIdentifier]::new("S-1-5-32-545")
$inheritance = [System.Security.AccessControl.InheritanceFlags]::ContainerInherit -bor
    [System.Security.AccessControl.InheritanceFlags]::ObjectInherit
$noPropagation = [System.Security.AccessControl.PropagationFlags]::None
$allow = [System.Security.AccessControl.AccessControlType]::Allow

$rootGrants = @(
    @{ Sid = $systemSid; Rights = [System.Security.AccessControl.FileSystemRights]::FullControl },
    @{ Sid = $administratorsSid; Rights = [System.Security.AccessControl.FileSystemRights]::FullControl },
    @{ Sid = $usersSid; Rights = [System.Security.AccessControl.FileSystemRights]::ReadAndExecute },
    @{ Sid = $serviceSid; Rights = [System.Security.AccessControl.FileSystemRights]::ReadAndExecute }
)
$stateGrants = @(
    @{ Sid = $systemSid; Rights = [System.Security.AccessControl.FileSystemRights]::FullControl },
    @{ Sid = $administratorsSid; Rights = [System.Security.AccessControl.FileSystemRights]::FullControl },
    @{ Sid = $serviceSid; Rights = [System.Security.AccessControl.FileSystemRights]::Modify }
)

function New-ProtectedDirectorySecurity {
    param([Parameter(Mandatory = $true)][hashtable[]]$Grants)

    $security = [System.Security.AccessControl.DirectorySecurity]::new()
    $security.SetAccessRuleProtection($true, $false)
    foreach ($grant in $Grants) {
        $rule = [System.Security.AccessControl.FileSystemAccessRule]::new(
            $grant.Sid,
            $grant.Rights,
            $inheritance,
            $noPropagation,
            $allow)
        $security.AddAccessRule($rule)
    }
    return $security
}

function New-ProtectedFileSecurity {
    param([Parameter(Mandatory = $true)][hashtable[]]$Grants)

    $security = [System.Security.AccessControl.FileSecurity]::new()
    $security.SetAccessRuleProtection($true, $false)
    foreach ($grant in $Grants) {
        $rule = [System.Security.AccessControl.FileSystemAccessRule]::new(
            $grant.Sid,
            $grant.Rights,
            $allow)
        $security.AddAccessRule($rule)
    }
    return $security
}

function Set-DirectoryAcl {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][hashtable[]]$Grants
    )

    $security = New-ProtectedDirectorySecurity -Grants $Grants
    Set-Acl -LiteralPath $Path -AclObject $security
}

function Set-FileAcl {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][hashtable[]]$Grants
    )

    $security = New-ProtectedFileSecurity -Grants $Grants
    Set-Acl -LiteralPath $Path -AclObject $security
}

function Get-CombinedRights {
    param(
        [Parameter(Mandatory = $true)][System.Security.AccessControl.AuthorizationRule[]]$Rules,
        [Parameter(Mandatory = $true)][System.Security.Principal.SecurityIdentifier]$Sid
    )

    [int]$bits = 0
    foreach ($rule in $Rules) {
        if ($rule.IdentityReference -is [System.Security.Principal.SecurityIdentifier] -and
            $rule.IdentityReference.Value -eq $Sid.Value) {
            $bits = $bits -bor [int]$rule.FileSystemRights
        }
    }
    return [System.Security.AccessControl.FileSystemRights]$bits
}

function Assert-DirectoryAcl {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][System.Security.Principal.SecurityIdentifier[]]$AllowedSids,
        [Parameter(Mandatory = $true)][hashtable[]]$RequiredRights
    )

    $acl = Get-Acl -LiteralPath $Path
    if (-not $acl.AreAccessRulesProtected) {
        throw "The DACL on '$Path' must have inheritance disabled."
    }

    $rules = @($acl.GetAccessRules(
        $true, $false, [System.Security.Principal.SecurityIdentifier]) |
        ForEach-Object { $_ })
    if ($rules.Count -eq 0) {
        throw "The DACL on '$Path' contains no explicit access rules."
    }
    foreach ($rule in $rules) {
        if ($rule.AccessControlType -ne $allow -or
            $rule.IdentityReference -isnot [System.Security.Principal.SecurityIdentifier] -or
            $AllowedSids.Value -notcontains $rule.IdentityReference.Value) {
            throw "The DACL on '$Path' contains an unapproved principal or ACE."
        }
    }
    foreach ($required in $RequiredRights) {
        $actual = Get-CombinedRights -Rules $rules -Sid $required.Sid
        if (([int]$actual -band [int]$required.Rights) -ne [int]$required.Rights) {
            throw "The DACL on '$Path' is missing a required access grant."
        }
    }

    $writeRights = [System.Security.AccessControl.FileSystemRights]::WriteData -bor
        [System.Security.AccessControl.FileSystemRights]::AppendData -bor
        [System.Security.AccessControl.FileSystemRights]::WriteAttributes -bor
        [System.Security.AccessControl.FileSystemRights]::WriteExtendedAttributes -bor
        [System.Security.AccessControl.FileSystemRights]::Delete -bor
        [System.Security.AccessControl.FileSystemRights]::ChangePermissions -bor
        [System.Security.AccessControl.FileSystemRights]::TakeOwnership -bor
        [System.Security.AccessControl.FileSystemRights]::CreateFiles -bor
        [System.Security.AccessControl.FileSystemRights]::CreateDirectories -bor
        [System.Security.AccessControl.FileSystemRights]::DeleteSubdirectoriesAndFiles
    foreach ($sid in @($usersSid, $serviceSid)) {
        $actual = Get-CombinedRights -Rules $rules -Sid $sid
        if (([int]$actual -band [int]$writeRights) -ne 0) {
            throw "The DACL on '$Path' grants write/ownership rights to ordinary users or the Agent service where not expected."
        }
    }
}

# Create/secure the parent before creating the Agent state directory. The Server-specific
# secret files and Server service were checked above; this routine never removes files.
if (-not (Test-Path -LiteralPath $managerRoot -PathType Container)) {
    New-Item -ItemType Directory -Path $managerRoot -Force | Out-Null
}
Assert-NotReparsePoint -Path $managerRoot
Set-DirectoryAcl -Path $managerRoot -Grants $rootGrants

if (-not (Test-Path -LiteralPath $configDirectory -PathType Container)) {
    New-Item -ItemType Directory -Path $configDirectory -Force | Out-Null
}
Assert-NotReparsePoint -Path $configDirectory
Set-DirectoryAcl -Path $configDirectory -Grants $rootGrants

if (-not (Test-Path -LiteralPath $agentStateRoot -PathType Container)) {
    New-Item -ItemType Directory -Path $agentStateRoot -Force | Out-Null
}
Assert-NotReparsePoint -Path $agentStateRoot
Set-DirectoryAcl -Path $agentStateRoot -Grants $stateGrants

# Repair existing state-file ACLs without deleting/re-enrolling an existing DeviceId or credential.
$stateItems = @(Get-ChildItem -LiteralPath $agentStateRoot -Force -Recurse -ErrorAction Stop)
foreach ($item in $stateItems) {
    if (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "A reparse point appeared in Agent state during provisioning; inspect the directory before starting the service."
    }
    if ($item.PSIsContainer) {
        Set-DirectoryAcl -Path $item.FullName -Grants $stateGrants
    }
    else {
        Set-FileAcl -Path $item.FullName -Grants $stateGrants
    }
}

$rootAllowed = @($systemSid, $administratorsSid, $usersSid, $serviceSid)
$stateAllowed = @($systemSid, $administratorsSid, $serviceSid)
$readExecute = [System.Security.AccessControl.FileSystemRights]::ReadAndExecute
$fullControl = [System.Security.AccessControl.FileSystemRights]::FullControl
$modify = [System.Security.AccessControl.FileSystemRights]::Modify
Assert-DirectoryAcl -Path $managerRoot -AllowedSids $rootAllowed -RequiredRights @(
    @{ Sid = $systemSid; Rights = $fullControl },
    @{ Sid = $administratorsSid; Rights = $fullControl },
    @{ Sid = $usersSid; Rights = $readExecute },
    @{ Sid = $serviceSid; Rights = $readExecute }
)
Assert-DirectoryAcl -Path $configDirectory -AllowedSids $rootAllowed -RequiredRights @(
    @{ Sid = $systemSid; Rights = $fullControl },
    @{ Sid = $administratorsSid; Rights = $fullControl },
    @{ Sid = $usersSid; Rights = $readExecute },
    @{ Sid = $serviceSid; Rights = $readExecute }
)
Assert-DirectoryAcl -Path $agentStateRoot -AllowedSids $stateAllowed -RequiredRights @(
    @{ Sid = $systemSid; Rights = $fullControl },
    @{ Sid = $administratorsSid; Rights = $fullControl },
    @{ Sid = $serviceSid; Rights = $modify }
)

Write-Host "Agent state directory configured with inheritance-protected ACLs."
Write-Host "Windows service: $($agentService.Name)"
Write-Host "Service logon account: $($agentService.StartName)"
Write-Host "Dedicated service SID: $($serviceSid.Value) (UNRESTRICTED)"
Write-Host "Agent endpoint/config directory: $configDirectory"
Write-Host "Protected DPAPI CurrentUser state directory: $agentStateRoot"
Write-Host "Existing Agent identity/credential files were retained; contents were never read or printed."
Write-Host "Start the Agent service and verify enrollment, credential persistence, and reconnect using the exact candidate SHA."
Write-Host "This helper is not an installer and does not certify the Windows service or physical LAN boundary."
