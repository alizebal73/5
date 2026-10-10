# Read-only preflight for the GameNet Manager Server PC.
# Run from an elevated PowerShell 7 session on the manager PC.
# This script does not start/stop services, change ACLs, touch certificate stores,
# read the protected secret file contents, or print secret configuration values.

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$programData = [Environment]::GetFolderPath([Environment+SpecialFolder]::CommonApplicationData)
if ([string]::IsNullOrWhiteSpace($programData)) {
    throw "The Windows ProgramData directory could not be resolved."
}

$managerRoot = Join-Path $programData "GameNet Manager"
$configDirectory = Join-Path $managerRoot "Config"
$serverConfigPath = Join-Path $configDirectory "server.json"
$secretsDirectory = Join-Path $managerRoot "Secrets"
$secretStorePath = Join-Path $secretsDirectory "server-secrets.v1.dpapi"
$auditDirectory = Join-Path $managerRoot "Audit"

$script:SensitiveConfigFindings = [System.Collections.Generic.List[string]]::new()

function Write-Section([string]$Name) {
    Write-Output ""
    Write-Output "=== $Name ==="
}

function Get-MapValue {
    param([object]$Map, [string]$Name)

    if ($null -eq $Map -or $Map -isnot [System.Collections.IDictionary]) {
        return $null
    }

    foreach ($key in $Map.Keys) {
        if ([string]::Equals([string]$key, $Name, [StringComparison]::OrdinalIgnoreCase)) {
            return $Map[$key]
        }
    }

    return $null
}

function Test-ValuePresent([object]$Value) {
    if ($null -eq $Value) { return $false }
    if ($Value -is [string]) { return -not [string]::IsNullOrWhiteSpace($Value) }
    return $true
}

function Find-SensitiveConfigKeys {
    param([object]$Node, [string]$Prefix = "")

    if ($null -eq $Node) { return }

    if ($Node -is [System.Collections.IDictionary]) {
        foreach ($key in $Node.Keys) {
            $keyText = [string]$key
            $path = if ([string]::IsNullOrWhiteSpace($Prefix)) { $keyText } else { "{0}:{1}" -f $Prefix, $keyText }
            $value = $Node[$key]

            if ($keyText -match '(?i)(password|connection.?string|signing.?key|provisioning.?key|secret|token|credential|client.?secret)') {
                if (Test-ValuePresent $value) {
                    $script:SensitiveConfigFindings.Add("$path=<present; value redacted>")
                }
                continue
            }

            Find-SensitiveConfigKeys -Node $value -Prefix $path
        }
        return
    }

    if ($Node -is [System.Collections.IEnumerable] -and $Node -isnot [string]) {
        $index = 0
        foreach ($entry in $Node) {
            Find-SensitiveConfigKeys -Node $entry -Prefix ("{0}[{1}]" -f $Prefix, $index)
            $index++
        }
    }
}

function Write-PathFacts([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path)) {
        Write-Output "path=$Path exists=false"
        return
    }

    try {
        $item = Get-Item -LiteralPath $Path -Force
        $type = if ($item.PSIsContainer) { "directory" } else { "file" }
        $length = if ($item.PSIsContainer) { "-" } else { [string]$item.Length }
        Write-Output "path=$Path exists=true type=$type length=$length lastWriteUtc=$($item.LastWriteTimeUtc.ToString('O'))"

        try {
            $acl = Get-Acl -LiteralPath $Path
            Write-Output "  aclOwner=$($acl.Owner) daclProtected=$($acl.AreAccessRulesProtected)"
            foreach ($rule in $acl.Access) {
                $inherited = if ($rule.IsInherited) { "inherited" } else { "explicit" }
                Write-Output "  acl=$($rule.IdentityReference)|$($rule.AccessControlType)|$($rule.FileSystemRights)|$inherited"
            }
        }
        catch {
            Write-Output "  acl=unavailable errorType=$($_.Exception.GetType().Name)"
        }
    }
    catch {
        Write-Output "path=$Path metadata=unavailable errorType=$($_.Exception.GetType().Name)"
    }
}

function Write-ServiceSidType([string]$ServiceName) {
    $scPath = Join-Path $env:SystemRoot "System32\sc.exe"
    if (-not (Test-Path -LiteralPath $scPath -PathType Leaf)) {
        Write-Output "service=$ServiceName sidType=inspection-unavailable"
        return
    }

    try {
        $lines = @(& $scPath qsidtype $ServiceName 2>&1)
        $exitCode = $LASTEXITCODE
        $sidLine = $lines | Where-Object { "$_" -match 'SERVICE_SID_TYPE' } | Select-Object -First 1
        if ($exitCode -eq 0 -and $null -ne $sidLine) {
            Write-Output "service=$ServiceName sidType=$("$sidLine".Split(':', 2)[1].Trim())"
        }
        else {
            Write-Output "service=$ServiceName sidType=unavailable exitCode=$exitCode"
        }
    }
    catch {
        Write-Output "service=$ServiceName sidType=unavailable errorType=$($_.Exception.GetType().Name)"
    }
}

Write-Output "GameNet Manager — READ-ONLY PREFLIGHT"
Write-Output "No services, files, certificates, firewall rules, registry settings or ACLs will be changed."
Write-Output "timestampUtc=$([DateTimeOffset]::UtcNow.ToString('O'))"
Write-Output "computerName=$env:COMPUTERNAME"
Write-Output "programData=$programData"

try {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    $isElevatedAdmin = $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
    Write-Output "currentIdentity=$($identity.Name)"
    Write-Output "elevatedAdministrator=$isElevatedAdmin"
}
catch {
    Write-Output "identityInspection=unavailable errorType=$($_.Exception.GetType().Name)"
}

Write-Section "Operating system"
try {
    $os = Get-CimInstance -ClassName Win32_OperatingSystem
    Write-Output "caption=$($os.Caption)"
    Write-Output "version=$($os.Version)"
    Write-Output "build=$($os.BuildNumber)"
    Write-Output "lastBootUtc=$(([DateTime]$os.LastBootUpTime).ToUniversalTime().ToString('O'))"
}
catch {
    Write-Output "osInspection=unavailable errorType=$($_.Exception.GetType().Name)"
}

Write-Section "Relevant Windows services"
$services = @()
try {
    $services = @(Get-CimInstance -ClassName Win32_Service | Where-Object {
        $_.Name -match '(?i)GameNet.*(Server|Agent)' -or
        $_.DisplayName -match '(?i)GameNet.*(Server|Agent)' -or
        $_.Name -match '(?i)^postgresql'
    })

    if ($services.Count -eq 0) {
        Write-Output "No GameNet Server/Agent or PostgreSQL Windows services were found."
    }

    foreach ($service in $services) {
        Write-Output "name=$($service.Name) displayName=$($service.DisplayName) state=$($service.State) startMode=$($service.StartMode) logon=$($service.StartName) pid=$($service.ProcessId)"
        if ($service.Name -match '(?i)GameNet.*(Server|Agent)' -or $service.DisplayName -match '(?i)GameNet.*(Server|Agent)') {
            Write-ServiceSidType -ServiceName ([string]$service.Name)
        }
    }
}
catch {
    Write-Output "serviceInspection=unavailable errorType=$($_.Exception.GetType().Name)"
}

Write-Section "GameNet runtime processes"
try {
    $processes = @(Get-CimInstance -ClassName Win32_Process | Where-Object {
        $_.Name -match '(?i)^GameNet\.(Server|Agent|Manager\.Desktop)\.(exe|dll)$' -or
        ($_.Name -ieq "dotnet.exe" -and $_.CommandLine -match '(?i)GameNet\.(Server|Agent)\.dll')
    })

    if ($processes.Count -eq 0) {
        Write-Output "No directly identifiable GameNet runtime processes were found."
    }

    foreach ($process in $processes) {
        $kind = if ($process.CommandLine -match '(?i)GameNet\.Server') {
            "Server"
        }
        elseif ($process.CommandLine -match '(?i)GameNet\.Agent') {
            "Agent"
        }
        elseif ($process.Name -match '(?i)Desktop') {
            "Desktop"
        }
        else {
            "GameNet"
        }

        $owner = "unavailable"
        try {
            $ownerResult = Invoke-CimMethod -InputObject $process -MethodName GetOwner
            if ($ownerResult.ReturnValue -eq 0) {
                $owner = "$($ownerResult.Domain)\$($ownerResult.User)"
            }
        }
        catch {
            $owner = "access-unavailable"
        }

        Write-Output "kind=$kind processName=$($process.Name) pid=$($process.ProcessId) owner=$owner executablePath=$($process.ExecutablePath) creationDate=$($process.CreationDate)"
        # CommandLine is intentionally never emitted: it may contain operational arguments.
    }
}
catch {
    Write-Output "processInspection=unavailable errorType=$($_.Exception.GetType().Name)"
}

Write-Section "Listener sockets"
foreach ($port in @(5080, 5432)) {
    try {
        $listeners = @(Get-NetTCPConnection -State Listen -LocalPort $port -ErrorAction Stop)
        if ($listeners.Count -eq 0) {
            Write-Output "port=$port listening=false"
            continue
        }

        foreach ($listener in $listeners) {
            $processName = "unknown"
            try {
                $processName = (Get-Process -Id $listener.OwningProcess -ErrorAction Stop).ProcessName
            }
            catch {
                $processName = "not-resolved"
            }

            $localOnly = $listener.LocalAddress -in @("127.0.0.1", "::1")
            Write-Output "port=$port localAddress=$($listener.LocalAddress) owningPid=$($listener.OwningProcess) process=$processName loopbackOnly=$localOnly"
        }
    }
    catch {
        Write-Output "port=$port listenerInspection=unavailable-or-none"
    }
}

Write-Section "PostgreSQL configuration and inbound firewall (read-only)"
$postgresService = $null
try {
    $postgresService = Get-CimInstance -ClassName Win32_Service |
        Where-Object { $_.Name -eq "postgresql-x64-17" } |
        Select-Object -First 1
}
catch {
    Write-Output "postgresServiceInspection=unavailable errorType=$($_.Exception.GetType().Name)"
}

$dataDirectory = $null
if ($null -ne $postgresService) {
    $dataMatch = [Regex]::Match([string]$postgresService.PathName, '(?i)(?:^|\s)-D\s+(?:"([^"]+)"|([^\s]+))')
    if ($dataMatch.Success) {
        $dataDirectory = if (-not [string]::IsNullOrWhiteSpace($dataMatch.Groups[1].Value)) {
            $dataMatch.Groups[1].Value
        }
        else {
            $dataMatch.Groups[2].Value
        }
    }
}
if ([string]::IsNullOrWhiteSpace($dataDirectory)) {
    $defaultDataDirectory = Join-Path $env:ProgramFiles "PostgreSQL\17\data"
    if (Test-Path -LiteralPath $defaultDataDirectory -PathType Container) {
        $dataDirectory = $defaultDataDirectory
    }
}

$configFile = if (-not [string]::IsNullOrWhiteSpace($dataDirectory)) {
    Join-Path $dataDirectory "postgresql.conf"
}
else { $null }
$hbaFile = if (-not [string]::IsNullOrWhiteSpace($dataDirectory)) {
    Join-Path $dataDirectory "pg_hba.conf"
}
else { $null }

if ($null -eq $postgresService) {
    Write-Output "postgresService=not-found"
}
else {
    Write-Output "postgresServiceState=$($postgresService.State) startMode=$($postgresService.StartMode) logon=$($postgresService.StartName) pid=$($postgresService.ProcessId)"
}
if ([string]::IsNullOrWhiteSpace($dataDirectory)) {
    Write-Output "postgresDataDirectory=not-resolved"
}
else {
    Write-Output "postgresDataDirectory=$dataDirectory"
    if (Test-Path -LiteralPath $configFile -PathType Leaf) {
        $settings = @(Get-Content -LiteralPath $configFile | ForEach-Object {
            if ($_ -match '^\s*(listen_addresses|port|hba_file)\s*=') { $_.Trim() }
        })
        if ($settings.Count -eq 0) {
            Write-Output "postgresqlConfigSettings=not-found-in-main-file (included files may override settings)"
        }
        else {
            foreach ($setting in $settings) {
                # These are network/config-path settings only; no credentials are read or emitted.
                Write-Output "postgresqlConfig=$setting"
                if ($setting -match '(?i)^\s*hba_file\s*=\s*(.+?)\s*(?:#.*)?
    Write-PathFacts -Path $path
}

Write-Section "Server endpoint/TLS configuration (non-secret fields only)"
$config = $null
$certificateThumbprint = $null
if (-not (Test-Path -LiteralPath $serverConfigPath -PathType Leaf)) {
    Write-Output "serverConfig=missing"
}
else {
    try {
        $config = Get-Content -LiteralPath $serverConfigPath -Raw | ConvertFrom-Json -AsHashtable
        $urls = Get-MapValue -Map $config -Name "urls"
        Write-Output "serverConfig=parsed"
        Write-Output "configuredUrls=$urls"

        $gameNet = Get-MapValue -Map $config -Name "GameNet"
        $tls = Get-MapValue -Map $gameNet -Name "ServerTls"
        $certificateThumbprint = Get-MapValue -Map $tls -Name "CertificateThumbprint"
        if (-not [string]::IsNullOrWhiteSpace([string]$certificateThumbprint)) {
            $certificateThumbprint = ([string]$certificateThumbprint -replace '\s', '').ToUpperInvariant()
            Write-Output "configuredCertificateThumbprint=$certificateThumbprint"
        }
        else {
            Write-Output "configuredCertificateThumbprint=missing"
        }

        $script:SensitiveConfigFindings.Clear()
        Find-SensitiveConfigKeys -Node $config
        if ($script:SensitiveConfigFindings.Count -eq 0) {
            Write-Output "nonemptySensitiveNamedSettings=none"
        }
        else {
            Write-Output "nonemptySensitiveNamedSettings=present; values redacted"
            foreach ($finding in $script:SensitiveConfigFindings) {
                Write-Output "  $finding"
            }
        }
    }
    catch {
        Write-Output "serverConfig=parse-failed errorType=$($_.Exception.GetType().Name); contents were not printed."
    }
}

Write-Section "Server certificate metadata"
if ([string]$certificateThumbprint -match '^[0-9A-F]{40}$') {
    $certificate = $null
    try {
        $certificate = Get-ChildItem -Path Cert:\LocalMachine\My -ErrorAction Stop |
            Where-Object { $_.Thumbprint -eq $certificateThumbprint } |
            Select-Object -First 1
    }
    catch {
        Write-Output "personalCertificateStore=unavailable errorType=$($_.Exception.GetType().Name)"
    }

    if ($null -eq $certificate) {
        Write-Output "personalCertificate=not-found"
    }
    else {
        Write-Output "personalCertificate=found thumbprint=$($certificate.Thumbprint)"
        Write-Output "subject=$($certificate.Subject)"
        Write-Output "hasPrivateKey=$($certificate.HasPrivateKey)"
        Write-Output "notBeforeUtc=$($certificate.NotBefore.ToUniversalTime().ToString('O'))"
        Write-Output "notAfterUtc=$($certificate.NotAfter.ToUniversalTime().ToString('O'))"
        $san = $certificate.Extensions | Where-Object { $_.Oid.Value -eq "2.5.29.17" } | Select-Object -First 1
        if ($null -ne $san) {
            Write-Output "subjectAlternativeNames=$($san.Format($false))"
        }
        else {
            Write-Output "subjectAlternativeNames=missing"
        }
        Write-Output "privateKeyAccessUnderServiceToken=not-tested-by-this-read-only-script"
    }

    try {
        $trustedCertificate = Get-ChildItem -Path Cert:\LocalMachine\Root -ErrorAction Stop |
            Where-Object { $_.Thumbprint -eq $certificateThumbprint } |
            Select-Object -First 1
        Write-Output "sameThumbprintInLocalMachineRoot=$($null -ne $trustedCertificate)"
    }
    catch {
        Write-Output "rootStoreInspection=unavailable errorType=$($_.Exception.GetType().Name)"
    }
}
else {
    Write-Output "certificateInspection=skipped (no valid SHA-1-format thumbprint configured; no certificate was modified)."
}

Write-Section "Interpretation"
Write-Output "This report only gathers read-only observations and does not prove service-token DPAPI access or TLS private-key access."
Write-Output "Do not run --provision-secrets or any TLS/configuration/cleanup script based on this report alone."
Write-Output "Preserve this output with the exact branch/commit used for the planned isolated Windows service test."
) {
                    $configuredHba = $Matches[1].Trim().Trim('"').Trim("'")
                    if (-not [string]::IsNullOrWhiteSpace($configuredHba)) {
                        $hbaFile = if ([IO.Path]::IsPathRooted($configuredHba)) {
                            $configuredHba
                        }
                        else {
                            Join-Path $dataDirectory $configuredHba
                        }
                    }
                }
            }
        }
    }
    else {
        Write-Output "postgresqlConfig=not-found"
    }

    if ($null -ne $hbaFile -and (Test-Path -LiteralPath $hbaFile -PathType Leaf)) {
        Write-Output "pgHbaRulesBegin (database and role names redacted)"
        $lineNumber = 0
        foreach ($line in Get-Content -LiteralPath $hbaFile) {
            $lineNumber++
            $ruleText = ($line -replace '#.*
    Write-PathFacts -Path $path
}

Write-Section "Server endpoint/TLS configuration (non-secret fields only)"
$config = $null
$certificateThumbprint = $null
if (-not (Test-Path -LiteralPath $serverConfigPath -PathType Leaf)) {
    Write-Output "serverConfig=missing"
}
else {
    try {
        $config = Get-Content -LiteralPath $serverConfigPath -Raw | ConvertFrom-Json -AsHashtable
        $urls = Get-MapValue -Map $config -Name "urls"
        Write-Output "serverConfig=parsed"
        Write-Output "configuredUrls=$urls"

        $gameNet = Get-MapValue -Map $config -Name "GameNet"
        $tls = Get-MapValue -Map $gameNet -Name "ServerTls"
        $certificateThumbprint = Get-MapValue -Map $tls -Name "CertificateThumbprint"
        if (-not [string]::IsNullOrWhiteSpace([string]$certificateThumbprint)) {
            $certificateThumbprint = ([string]$certificateThumbprint -replace '\s', '').ToUpperInvariant()
            Write-Output "configuredCertificateThumbprint=$certificateThumbprint"
        }
        else {
            Write-Output "configuredCertificateThumbprint=missing"
        }

        $script:SensitiveConfigFindings.Clear()
        Find-SensitiveConfigKeys -Node $config
        if ($script:SensitiveConfigFindings.Count -eq 0) {
            Write-Output "nonemptySensitiveNamedSettings=none"
        }
        else {
            Write-Output "nonemptySensitiveNamedSettings=present; values redacted"
            foreach ($finding in $script:SensitiveConfigFindings) {
                Write-Output "  $finding"
            }
        }
    }
    catch {
        Write-Output "serverConfig=parse-failed errorType=$($_.Exception.GetType().Name); contents were not printed."
    }
}

Write-Section "Server certificate metadata"
if ([string]$certificateThumbprint -match '^[0-9A-F]{40}$') {
    $certificate = $null
    try {
        $certificate = Get-ChildItem -Path Cert:\LocalMachine\My -ErrorAction Stop |
            Where-Object { $_.Thumbprint -eq $certificateThumbprint } |
            Select-Object -First 1
    }
    catch {
        Write-Output "personalCertificateStore=unavailable errorType=$($_.Exception.GetType().Name)"
    }

    if ($null -eq $certificate) {
        Write-Output "personalCertificate=not-found"
    }
    else {
        Write-Output "personalCertificate=found thumbprint=$($certificate.Thumbprint)"
        Write-Output "subject=$($certificate.Subject)"
        Write-Output "hasPrivateKey=$($certificate.HasPrivateKey)"
        Write-Output "notBeforeUtc=$($certificate.NotBefore.ToUniversalTime().ToString('O'))"
        Write-Output "notAfterUtc=$($certificate.NotAfter.ToUniversalTime().ToString('O'))"
        $san = $certificate.Extensions | Where-Object { $_.Oid.Value -eq "2.5.29.17" } | Select-Object -First 1
        if ($null -ne $san) {
            Write-Output "subjectAlternativeNames=$($san.Format($false))"
        }
        else {
            Write-Output "subjectAlternativeNames=missing"
        }
        Write-Output "privateKeyAccessUnderServiceToken=not-tested-by-this-read-only-script"
    }

    try {
        $trustedCertificate = Get-ChildItem -Path Cert:\LocalMachine\Root -ErrorAction Stop |
            Where-Object { $_.Thumbprint -eq $certificateThumbprint } |
            Select-Object -First 1
        Write-Output "sameThumbprintInLocalMachineRoot=$($null -ne $trustedCertificate)"
    }
    catch {
        Write-Output "rootStoreInspection=unavailable errorType=$($_.Exception.GetType().Name)"
    }
}
else {
    Write-Output "certificateInspection=skipped (no valid SHA-1-format thumbprint configured; no certificate was modified)."
}

Write-Section "Interpretation"
Write-Output "This report only gathers read-only observations and does not prove service-token DPAPI access or TLS private-key access."
Write-Output "Do not run --provision-secrets or any TLS/configuration/cleanup script based on this report alone."
Write-Output "Preserve this output with the exact branch/commit used for the planned isolated Windows service test."
, '').Trim()
            if ([string]::IsNullOrWhiteSpace($ruleText)) { continue }
            $tokens = @($ruleText -split '\s+')
            $ruleType = $tokens[0]
            $address = "-"
            $method = "unparsed"
            if ($ruleType -like "host*") {
                if ($tokens.Count -ge 5) {
                    $address = $tokens[3]
                    $method = $tokens[4]
                }
            }
            elseif ($ruleType -eq "local") {
                if ($tokens.Count -ge 4) { $method = $tokens[3] }
            }
            else {
                # Do not print configuration directives or any values from them.
                $method = if ($ruleType -like "include*") { "include-directive" } else { "unparsed" }
            }
            Write-Output "pgHbaRuleLine=$lineNumber type=$ruleType address=$address authMethod=$method database=<redacted> role=<redacted>"
        }
        Write-Output "pgHbaRulesEnd"
    }
    else {
        Write-Output "pgHbaFile=not-found-at-resolved-path"
    }
}

try {
    $inboundRules = @(Get-NetFirewallRule -PolicyStore ActiveStore -Enabled True -Direction Inbound)
    $matchingRules = 0
    foreach ($rule in $inboundRules) {
        try {
            $portFilter = Get-NetFirewallPortFilter -AssociatedNetFirewallRule $rule -ErrorAction Stop
            $localPorts = @($portFilter.LocalPort | ForEach-Object { [string]$_ })
            $relevantPorts = @($localPorts | Where-Object { $_ -in @("5432", "5080", "Any") })
            if ($relevantPorts.Count -eq 0) { continue }
            $addressFilter = Get-NetFirewallAddressFilter -AssociatedNetFirewallRule $rule -ErrorAction Stop
            Write-Output "firewallRule=$($rule.DisplayName) enabled=$($rule.Enabled) action=$($rule.Action) profile=$($rule.Profile) protocol=$($portFilter.Protocol) localPort=$($portFilter.LocalPort -join ',') localAddress=$($addressFilter.LocalAddress -join ',') remoteAddress=$($addressFilter.RemoteAddress -join ',')"
            $matchingRules++
        }
        catch {
            # A single rule that cannot be inspected must not hide the remaining rules.
        }
    }
    if ($matchingRules -eq 0) {
        Write-Output "inboundFirewallRulesForPorts5080Or5432=none-found-or-inspection-unavailable"
    }
}
catch {
    Write-Output "inboundFirewallInspection=unavailable errorType=$($_.Exception.GetType().Name)"
}

Write-Section "ProgramData path metadata and access rules"
foreach ($path in @(
    $managerRoot,
    $configDirectory,
    $serverConfigPath,
    (Join-Path $managerRoot "Server"),
    (Join-Path (Join-Path $managerRoot "Server") "Data"),
    (Join-Path $managerRoot "Backups"),
    (Join-Path $managerRoot "Agent"),
    (Join-Path $managerRoot "Logs"),
    (Join-Path $managerRoot "State"),
    (Join-Path $managerRoot "Updates"),
    $secretsDirectory,
    $secretStorePath,
    $auditDirectory
)) {
    Write-PathFacts -Path $path
}

Write-Section "Server endpoint/TLS configuration (non-secret fields only)"
$config = $null
$certificateThumbprint = $null
if (-not (Test-Path -LiteralPath $serverConfigPath -PathType Leaf)) {
    Write-Output "serverConfig=missing"
}
else {
    try {
        $config = Get-Content -LiteralPath $serverConfigPath -Raw | ConvertFrom-Json -AsHashtable
        $urls = Get-MapValue -Map $config -Name "urls"
        Write-Output "serverConfig=parsed"
        Write-Output "configuredUrls=$urls"

        $gameNet = Get-MapValue -Map $config -Name "GameNet"
        $tls = Get-MapValue -Map $gameNet -Name "ServerTls"
        $certificateThumbprint = Get-MapValue -Map $tls -Name "CertificateThumbprint"
        if (-not [string]::IsNullOrWhiteSpace([string]$certificateThumbprint)) {
            $certificateThumbprint = ([string]$certificateThumbprint -replace '\s', '').ToUpperInvariant()
            Write-Output "configuredCertificateThumbprint=$certificateThumbprint"
        }
        else {
            Write-Output "configuredCertificateThumbprint=missing"
        }

        $script:SensitiveConfigFindings.Clear()
        Find-SensitiveConfigKeys -Node $config
        if ($script:SensitiveConfigFindings.Count -eq 0) {
            Write-Output "nonemptySensitiveNamedSettings=none"
        }
        else {
            Write-Output "nonemptySensitiveNamedSettings=present; values redacted"
            foreach ($finding in $script:SensitiveConfigFindings) {
                Write-Output "  $finding"
            }
        }
    }
    catch {
        Write-Output "serverConfig=parse-failed errorType=$($_.Exception.GetType().Name); contents were not printed."
    }
}

Write-Section "Server certificate metadata"
if ([string]$certificateThumbprint -match '^[0-9A-F]{40}$') {
    $certificate = $null
    try {
        $certificate = Get-ChildItem -Path Cert:\LocalMachine\My -ErrorAction Stop |
            Where-Object { $_.Thumbprint -eq $certificateThumbprint } |
            Select-Object -First 1
    }
    catch {
        Write-Output "personalCertificateStore=unavailable errorType=$($_.Exception.GetType().Name)"
    }

    if ($null -eq $certificate) {
        Write-Output "personalCertificate=not-found"
    }
    else {
        Write-Output "personalCertificate=found thumbprint=$($certificate.Thumbprint)"
        Write-Output "subject=$($certificate.Subject)"
        Write-Output "hasPrivateKey=$($certificate.HasPrivateKey)"
        Write-Output "notBeforeUtc=$($certificate.NotBefore.ToUniversalTime().ToString('O'))"
        Write-Output "notAfterUtc=$($certificate.NotAfter.ToUniversalTime().ToString('O'))"
        $san = $certificate.Extensions | Where-Object { $_.Oid.Value -eq "2.5.29.17" } | Select-Object -First 1
        if ($null -ne $san) {
            Write-Output "subjectAlternativeNames=$($san.Format($false))"
        }
        else {
            Write-Output "subjectAlternativeNames=missing"
        }
        Write-Output "privateKeyAccessUnderServiceToken=not-tested-by-this-read-only-script"
    }

    try {
        $trustedCertificate = Get-ChildItem -Path Cert:\LocalMachine\Root -ErrorAction Stop |
            Where-Object { $_.Thumbprint -eq $certificateThumbprint } |
            Select-Object -First 1
        Write-Output "sameThumbprintInLocalMachineRoot=$($null -ne $trustedCertificate)"
    }
    catch {
        Write-Output "rootStoreInspection=unavailable errorType=$($_.Exception.GetType().Name)"
    }
}
else {
    Write-Output "certificateInspection=skipped (no valid SHA-1-format thumbprint configured; no certificate was modified)."
}

Write-Section "Interpretation"
Write-Output "This report only gathers read-only observations and does not prove service-token DPAPI access or TLS private-key access."
Write-Output "Do not run --provision-secrets or any TLS/configuration/cleanup script based on this report alone."
Write-Output "Preserve this output with the exact branch/commit used for the planned isolated Windows service test."
