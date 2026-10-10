[CmdletBinding()]
param(
    [Parameter()]
    [string]$ServiceName = "GameNet 5 Server",

    [Parameter()]
    [string]$ManagerRoot = (Join-Path $env:ProgramData "GameNet Manager")
)

# Read-only preflight for an existing GameNet Manager PC.
# This script never changes services, certificates, ACLs, listeners, configuration or secret files.
# It does not decrypt, display or parse any protected secret payload.

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$checks = [System.Collections.Generic.List[object]]::new()
$configDirectory = Join-Path $ManagerRoot "Config"
$secretDirectory = Join-Path $ManagerRoot "Secrets"
$serverConfigPath = Join-Path $configDirectory "server.json"
$setupSettingsPath = Join-Path $configDirectory "server-secrets.bin"
$runtimeSecretsPath = Join-Path $secretDirectory "server-secrets.v1.dpapi"

function Add-PreflightCheck {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)]
        [ValidateSet("PASS", "WARN", "FAIL", "INFO")]
        [string]$Status,
        [Parameter(Mandatory = $true)][string]$Details
    )

    $script:checks.Add([pscustomobject]@{
        Check = $Name
        Status = $Status
        Details = $Details
    })
}

function Get-ExecutablePathOnly {
    param([AllowNull()][string]$PathName)

    if ([string]::IsNullOrWhiteSpace($PathName)) {
        return $null
    }

    if ($PathName -match '^\s*"([^"]+\.exe)"') {
        return $Matches[1]
    }

    if ($PathName -match '^\s*(\S+\.exe)(?:\s|$)') {
        return $Matches[1]
    }

    # Do not print an unparsed service command line because it could contain arguments.
    return "<path not safely parsed; command line withheld>"
}

function Add-PathAndAclReport {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][bool]$ExpectedToExist,
        [Parameter(Mandatory = $true)][string]$Kind
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        $status = if ($ExpectedToExist) { "WARN" } else { "INFO" }
        Add-PreflightCheck -Name "$Kind exists" -Status $status -Details "$Path is absent."
        return
    }

    Add-PreflightCheck -Name "$Kind exists" -Status "PASS" -Details $Path

    try {
        $acl = Get-Acl -LiteralPath $Path
        $rules = @($acl.Access | ForEach-Object {
            "{0} | {1} | {2} | Inherited={3} | Inheritance={4}" -f
                $_.IdentityReference,
                $_.AccessControlType,
                $_.FileSystemRights,
                $_.IsInherited,
                $_.InheritanceFlags
        })
        $ruleText = if ($rules.Count -gt 0) { $rules -join "; " } else { "<no access rules returned>" }
        $details = "Owner=$($acl.Owner); InheritanceProtected=$($acl.AreAccessRulesProtected); Explicit/inherited ACL entries: $ruleText"

        # This is descriptive evidence only, not a certification that the effective DACL is safe.
        Add-PreflightCheck -Name "$Kind ACL" -Status "INFO" -Details $details
    }
    catch {
        Add-PreflightCheck -Name "$Kind ACL" -Status "WARN" -Details "Could not read this ACL; run the preflight from an elevated session."
    }
}

Write-Host "GameNet Manager read-only preflight"
Write-Host ("Computer: {0}" -f $env:COMPUTERNAME)
Write-Host ("Operating system: {0}" -f [Environment]::OSVersion.VersionString)
Write-Host ("Current identity: {0}" -f [Security.Principal.WindowsIdentity]::GetCurrent().Name)
Write-Host "Protected payload contents and service command-line arguments will not be displayed."
Write-Host ""

# Service details: show only the executable path, never its raw command line.
try {
    $service = Get-CimInstance -ClassName Win32_Service |
        Where-Object { $_.Name -eq $ServiceName -or $_.DisplayName -eq $ServiceName } |
        Select-Object -First 1

    if ($null -eq $service) {
        Add-PreflightCheck -Name "Server Windows service" -Status "WARN" -Details "Service '$ServiceName' is not registered."
    }
    else {
        $executablePath = Get-ExecutablePathOnly -PathName ([string]$service.PathName)
        Add-PreflightCheck -Name "Server Windows service" -Status "INFO" -Details (
            "Name={0}; State={1}; StartMode={2}; LogonAccount={3}; ProcessId={4}; Executable={5}" -f
            $service.Name, $service.State, $service.StartMode, $service.StartName, $service.ProcessId, $executablePath
        )

        $sc = Join-Path $env:SystemRoot "System32\sc.exe"
        if (Test-Path -LiteralPath $sc -PathType Leaf) {
            $actualServiceName = [string]$service.Name
            $sidTypeOutput = (& $sc qsidtype $actualServiceName 2>&1 | Out-String)
            $sidTypeExit = $LASTEXITCODE
            if ($sidTypeExit -eq 0 -and $sidTypeOutput -match "SERVICE_SID_TYPE:\s*(\w+)") {
                $sidType = $Matches[1]
                $sidStatus = if ($sidType -in @("UNRESTRICTED", "RESTRICTED")) { "PASS" } else { "WARN" }
                Add-PreflightCheck -Name "Server service SID type" -Status $sidStatus -Details $sidType
            }
            else {
                Add-PreflightCheck -Name "Server service SID type" -Status "WARN" -Details "Could not confirm that the service SID is enabled."
            }

            $sidOutput = (& $sc showsid $actualServiceName 2>&1 | Out-String)
            $sidExit = $LASTEXITCODE
            if ($sidExit -eq 0 -and $sidOutput -match "SERVICE SID:\s*(S-1-5-80-(?:\d+-?)+)") {
                Add-PreflightCheck -Name "Server service SID" -Status "INFO" -Details $Matches[1]
            }
            else {
                Add-PreflightCheck -Name "Server service SID" -Status "WARN" -Details "Windows did not return a service SID for this service name."
            }
        }
        else {
            Add-PreflightCheck -Name "Server service SID" -Status "WARN" -Details "sc.exe was not found; service SID state could not be inspected."
        }
    }
}
catch {
    Add-PreflightCheck -Name "Server Windows service" -Status "WARN" -Details "Could not inspect service metadata. Run from an elevated PowerShell session."
}

# Directory and file paths only. The protected files are not opened or decrypted.
Add-PathAndAclReport -Path $configDirectory -ExpectedToExist $true -Kind "Config directory"
Add-PathAndAclReport -Path $serverConfigPath -ExpectedToExist $true -Kind "server.json"
Add-PathAndAclReport -Path $setupSettingsPath -ExpectedToExist $false -Kind "Protected setup settings file"
Add-PathAndAclReport -Path $secretDirectory -ExpectedToExist $false -Kind "Runtime secrets directory"
Add-PathAndAclReport -Path $runtimeSecretsPath -ExpectedToExist $false -Kind "DPAPI runtime secret store"

# Inspect the public configuration values necessary to find the TLS certificate and listener.
# No other configuration keys or protected settings are emitted.
if (Test-Path -LiteralPath $serverConfigPath -PathType Leaf) {
    try {
        $config = Get-Content -LiteralPath $serverConfigPath -Raw | ConvertFrom-Json -AsHashtable
        $urls = [string]$config["urls"]
        $gameNet = $config["GameNet"]
        $tls = if ($null -ne $gameNet) { $gameNet["ServerTls"] } else { $null }
        $thumbprint = if ($null -ne $tls) { ([string]$tls["CertificateThumbprint"]).Replace(" ", "").ToUpperInvariant() } else { "" }

        if ([string]::IsNullOrWhiteSpace($urls)) {
            Add-PreflightCheck -Name "Server listener configuration" -Status "WARN" -Details "The public urls value is missing."
        }
        else {
            Add-PreflightCheck -Name "Server listener configuration" -Status "INFO" -Details $urls

            foreach ($url in @($urls.Split(';', [StringSplitOptions]::RemoveEmptyEntries) | ForEach-Object { $_.Trim() })) {
                $uri = $null
                if ([Uri]::TryCreate($url, [UriKind]::Absolute, [ref]$uri) -and $uri.Scheme -eq "https") {
                    try {
                        $listeners = @(Get-NetTCPConnection -State Listen -LocalPort $uri.Port -ErrorAction SilentlyContinue)
                        $status = if ($listeners.Count -gt 0) { "INFO" } else { "WARN" }
                        $detail = if ($listeners.Count -gt 0) { "$($uri.Host):$($uri.Port) has a local listener." } else { "$($uri.Host):$($uri.Port) has no local listener at inspection time." }
                        Add-PreflightCheck -Name "HTTPS port $($uri.Port)" -Status $status -Details $detail
                    }
                    catch {
                        Add-PreflightCheck -Name "HTTPS port $($uri.Port)" -Status "INFO" -Details "Could not query local listener state."
                    }
                }
                else {
                    Add-PreflightCheck -Name "Listener URL" -Status "WARN" -Details "A configured listener is not an absolute HTTPS URL."
                }
            }
        }

        if ($thumbprint -notmatch '^[A-F0-9]{40}$') {
            Add-PreflightCheck -Name "TLS certificate thumbprint" -Status "WARN" -Details "A valid 40-character certificate thumbprint is not configured."
        }
        else {
            try {
                $certificate = Get-Item -LiteralPath ("Cert:\LocalMachine\My\" + $thumbprint) -ErrorAction Stop
                $certificateStatus = if ($certificate.HasPrivateKey -and $certificate.NotAfter -gt (Get-Date)) { "PASS" } else { "WARN" }
                Add-PreflightCheck -Name "TLS certificate" -Status $certificateStatus -Details (
                    "Thumbprint={0}; Subject={1}; HasPrivateKey={2}; NotAfter={3:u}" -f
                    $certificate.Thumbprint, $certificate.Subject, $certificate.HasPrivateKey, $certificate.NotAfter
                )
                $rsa = $null
                try {
                    $rsa = [System.Security.Cryptography.X509Certificates.RSACertificateExtensions]::GetRSAPrivateKey($certificate)
                    $keyPath = $null
                    if ($rsa -is [System.Security.Cryptography.RSACng]) {
                        $keyPath = Join-Path "$env:ProgramData\Microsoft\Crypto\Keys" $rsa.Key.UniqueName
                    }
                    elseif ($rsa -is [System.Security.Cryptography.RSACryptoServiceProvider]) {
                        $keyPath = Join-Path "$env:ProgramData\Microsoft\Crypto\RSA\MachineKeys" $rsa.CspKeyContainerInfo.UniqueKeyContainerName
                    }

                    if (-not [string]::IsNullOrWhiteSpace($keyPath) -and (Test-Path -LiteralPath $keyPath -PathType Leaf)) {
                        Add-PathAndAclReport -Path $keyPath -ExpectedToExist $true -Kind "TLS private-key file"
                    }
                    else {
                        Add-PreflightCheck -Name "TLS private-key file ACL" -Status "WARN" -Details "Could not safely resolve the certificate's machine private-key file."
                    }
                }
                catch {
                    Add-PreflightCheck -Name "TLS private-key file ACL" -Status "WARN" -Details "Could not inspect the certificate private-key file ACL."
                }
                finally {
                    if ($null -ne $rsa) { $rsa.Dispose() }
                    $certificate.Dispose()
                }
            }
            catch {
                Add-PreflightCheck -Name "TLS certificate" -Status "WARN" -Details "The configured certificate was not found in LocalMachine\My."
            }
        }
    }
    catch {
        Add-PreflightCheck -Name "Server public configuration" -Status "WARN" -Details "server.json could not be parsed. Its contents have not been printed."
    }
}

Write-Host ""
$checks | Format-Table -Property Check, Status, Details -AutoSize -Wrap
Write-Host ""
Write-Host "PREFLIGHT COMPLETE: review every WARN/FAIL and the displayed ACL entries before any provisioning or cleanup."
Write-Host "This report is evidence for review only; it does not change the machine and does not certify effective permissions, DPAPI access or TLS handshakes."
