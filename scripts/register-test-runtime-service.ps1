[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet("Server", "Agent")]
    [string]$Component,

    [Parameter(Mandatory = $true)]
    [string]$PayloadRoot,

    [Parameter(Mandatory = $true)]
    [switch]$TestOnly,

    [switch]$PreflightReviewed,

    [switch]$ValidateOnly
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

if ($env:OS -ne "Windows_NT") {
    throw "Test-only service registration is supported only on Windows."
}

$root = [System.IO.Path]::GetFullPath($PayloadRoot)
function Assert-NotReparsePoint {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][bool]$ExpectDirectory
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        throw "Required payload path is missing: $Path"
    }
    $item = Get-Item -LiteralPath $Path -Force
    if (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "Refusing a payload path that is a reparse point: $Path"
    }
    if ($ExpectDirectory -and -not $item.PSIsContainer) {
        throw "Expected a payload directory: $Path"
    }
    if (-not $ExpectDirectory -and $item.PSIsContainer) {
        throw "Expected a payload file: $Path"
    }
}

Assert-NotReparsePoint -Path $root -ExpectDirectory $true
$manifestPath = Join-Path $root "manifest.json"
Assert-NotReparsePoint -Path $manifestPath -ExpectDirectory $false
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json

if ([string]$manifest.artifactType -cne "temporary-runtime-lan-smoke-payload" -or
    $manifest.isInstaller -ne $false -or
    $manifest.productionCertified -ne $false) {
    throw "Only the explicitly marked non-production Runtime LAN Smoke payload may register a test service."
}
$testedSha = [string]$manifest.source.testedCheckoutSha
if ($testedSha -notmatch '^[0-9a-f]{40}$') {
    throw "The payload manifest does not contain a valid exact tested source SHA."
}

if (-not $TestOnly.IsPresent) {
    throw "This helper requires explicit -TestOnly consent. No Windows service was changed."
}

$serverServiceName = "GameNet 5 Server"
$agentServiceName = "GameNet 5 Agent"
if ($Component -eq "Server") {
    $serviceName = $serverServiceName
    $serviceAccount = "NT SERVICE\GameNet 5 Server"
    $executableRelativePath = "Server/GameNet.Server.exe"
    $executablePath = Join-Path $root "Server\GameNet.Server.exe"
}
else {
    $serviceName = $agentServiceName
    $serviceAccount = "NT AUTHORITY\LocalService"
    $executableRelativePath = "Agent/GameNet.Agent.exe"
    $executablePath = Join-Path $root "Agent\GameNet.Agent.exe"
}

Assert-NotReparsePoint -Path (Join-Path $root "Server") -ExpectDirectory $true
Assert-NotReparsePoint -Path (Join-Path $root "Agent") -ExpectDirectory $true
Assert-NotReparsePoint -Path (Join-Path $root "Tools") -ExpectDirectory $true
Assert-NotReparsePoint -Path $executablePath -ExpectDirectory $false
$registrationScriptInPayload = Join-Path $root "Tools\register-test-runtime-service.ps1"
Assert-NotReparsePoint -Path $registrationScriptInPayload -ExpectDirectory $false

function Assert-ManifestHash {
    param(
        [Parameter(Mandatory = $true)][string]$RelativePath,
        [Parameter(Mandatory = $true)][string]$FullPath
    )

    $manifestEntries = @($manifest.files | Where-Object {
        [string]::Equals([string]$_.path, $RelativePath, [StringComparison]::OrdinalIgnoreCase)
    })
    if ($manifestEntries.Count -ne 1) {
        throw "The package manifest must contain exactly one SHA-256 entry for '$RelativePath'."
    }
    $expected = ([string]$manifestEntries[0].sha256).Trim().ToLowerInvariant()
    if ($expected -notmatch '^[0-9a-f]{64}$') {
        throw "The package manifest contains an invalid SHA-256 entry for '$RelativePath'."
    }
    $actual = (Get-FileHash -LiteralPath $FullPath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actual -ne $expected) {
        throw "The packaged file hash differs from the manifest for '$RelativePath'."
    }
}

Assert-ManifestHash -RelativePath $executableRelativePath -FullPath $executablePath
Assert-ManifestHash -RelativePath "Tools/register-test-runtime-service.ps1" -FullPath $registrationScriptInPayload

if ($ValidateOnly) {
    [pscustomobject]@{
        ValidationOnly = $true
        Component = $Component
        ServiceName = $serviceName
        ServiceAccount = $serviceAccount
        ExecutableRelativePath = $executableRelativePath
        TestedSourceSha = $testedSha
        ServiceChanged = $false
    }
    return
}

if (-not $PreflightReviewed) {
    throw "Review scripts/inspect-manager-runtime-state.ps1 on this disposable PC first, then explicitly pass -PreflightReviewed."
}

$principal = [Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw "Run actual test-service registration from an elevated PowerShell session."
}

$sc = Join-Path $env:SystemRoot "System32\sc.exe"
if (-not (Test-Path -LiteralPath $sc -PathType Leaf)) {
    throw "sc.exe is not present; no service was registered."
}

$knownServiceNames = @($serverServiceName, $agentServiceName)
$existingServices = @(Get-CimInstance -ClassName Win32_Service |
    Where-Object { $_.Name -in $knownServiceNames -or $_.DisplayName -in $knownServiceNames })
if ($existingServices.Count -gt 0) {
    $details = ($existingServices | ForEach-Object { "$($_.Name) [$($_.State)]" }) -join ", "
    throw "A GameNet service already exists ($details). This helper never replaces or reconfigures a service."
}

$common = [Environment]::GetFolderPath([Environment+SpecialFolder]::CommonApplicationData)
$managerRoot = Join-Path $common "GameNet Manager"
$configDirectory = Join-Path $managerRoot "Config"
$agentStateRoot = Join-Path $managerRoot "Agent"
$serverSecretDirectory = Join-Path $managerRoot "Secrets"
$knownStateMarkers = @(
    (Join-Path $configDirectory "server.json"),
    (Join-Path $configDirectory "server-secrets.bin"),
    (Join-Path $serverSecretDirectory "server-secrets.v1.dpapi"),
    (Join-Path $configDirectory "agent.json"),
    (Join-Path $agentStateRoot "identity.json"),
    (Join-Path $agentStateRoot "credential.bin"),
    (Join-Path $agentStateRoot "enrollment-token.dpapi")
)
$existingMarkers = @($knownStateMarkers | Where-Object { Test-Path -LiteralPath $_ })
if ($existingMarkers.Count -gt 0) {
    throw "GameNet runtime markers already exist under ProgramData. Stop and review the machine; this test helper will not reuse existing state."
}

$createdByThisInvocation = $false
try {
    # Demand start ensures registration never starts a service before TLS, protected
    # configuration, service-owned ACLs, and the disposable database are prepared.
    # Keep every SC option/value separate. The executable path is explicitly quoted
    # so a payload extracted below a path containing spaces remains unambiguous.
    $binaryPathValue = '"' + $executablePath + '"'
    $createOutput = @(
        & $sc create $serviceName "binPath=" $binaryPathValue "start=" "demand" "obj=" $serviceAccount "DisplayName=" $serviceName 2>&1
    )
    $createExitCode = $LASTEXITCODE
    if ($createExitCode -ne 0) {
        throw "sc.exe could not register the requested test service (exit $createExitCode): $($createOutput -join ' ')"
    }
    $createdByThisInvocation = $true

    $null = & $sc sidtype $serviceName unrestricted 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "Could not enable the dedicated Service SID for '$serviceName'."
    }

    $sidOutput = (& $sc showsid $serviceName 2>&1 | Out-String)
    $sidExitCode = $LASTEXITCODE
    $sidMatch = [regex]::Match($sidOutput, 'SERVICE SID:\s*(S-1-5-80-(?:\d+-){4}\d+)', [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)
    if ($sidExitCode -ne 0 -or -not $sidMatch.Success) {
        throw "Windows did not return a valid Service SID for '$serviceName'."
    }
    $expectedSid = ([System.Security.Principal.NTAccount]::new("NT SERVICE\$serviceName")).Translate(
        [System.Security.Principal.SecurityIdentifier])
    if (-not [string]::Equals($sidMatch.Groups[1].Value, $expectedSid.Value, [StringComparison]::OrdinalIgnoreCase)) {
        throw "The registered service SID differs from the canonical NT SERVICE identity."
    }

    $registeredService = Get-CimInstance -ClassName Win32_Service |
        Where-Object { $_.Name -eq $serviceName } |
        Select-Object -First 1
    if ($null -eq $registeredService) {
        throw "Windows did not return the newly registered service."
    }
    if (-not [string]::Equals(([string]$registeredService.StartName).Replace("/", "\").Trim(), $serviceAccount, [StringComparison]::OrdinalIgnoreCase)) {
        throw "The actual service logon account differs from the approved role-specific account."
    }
    if ($registeredService.State -ne "Stopped" -or $registeredService.StartMode -ne "Manual" -or [int]$registeredService.ProcessId -ne 0) {
        throw "The test service is not in the required stopped/manual state."
    }

    [pscustomobject]@{
        ValidationOnly = $false
        Component = $Component
        ServiceName = $serviceName
        ServiceAccount = $serviceAccount
        ServiceSid = $expectedSid.Value
        State = [string]$registeredService.State
        StartMode = [string]$registeredService.StartMode
        ExecutableRelativePath = $executableRelativePath
        TestedSourceSha = $testedSha
        ServiceStarted = $false
    }
}
catch {
    if ($createdByThisInvocation) {
        try {
            $cleanupOutput = @(& $sc delete $serviceName 2>&1)
            if ($LASTEXITCODE -ne 0) {
                Write-Warning "Cleanup of the service created by this invocation did not complete: $($cleanupOutput -join ' ')"
            }
        }
        catch {
            Write-Warning "Cleanup could not remove the service created by this invocation. Inspect only this disposable test machine."
        }
    }
    throw
}
