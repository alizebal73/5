 $ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$dotnet = Join-Path $env:ProgramFiles "dotnet\dotnet.exe"
if (-not (Test-Path $dotnet)) { $dotnet = (Get-Command dotnet -ErrorAction Stop).Source }

function Get-ConnectionValue([System.Data.Common.DbConnectionStringBuilder]$builder, [string[]]$names) {
    foreach ($name in $names) {
        foreach ($key in $builder.Keys) {
            if ([string]::Equals([string]$key, $name, [StringComparison]::OrdinalIgnoreCase)) {
                return [string]$builder[$key]
            }
        }
    }
    return $null
}

function Get-PgNameValueFromText([string]$text, [string[]]$names) {
    foreach ($name in $names) {
        $pattern = "(?i)(?:^|[;\s])" + [Regex]::Escape($name) + "\s*=\s*([^;\s]+)"
        $match = [Regex]::Match($text, $pattern)
        if ($match.Success) { return $match.Groups[1].Value.Trim().Trim('"') }
    }
    return $null
}

function Get-NpgsqlConnectionBuilder([string]$connectionString) {
    $assemblyPath = Join-Path (Get-Location) "src\Server\bin\Release\net10.0\Npgsql.dll"
    if (-not (Test-Path -LiteralPath $assemblyPath -PathType Leaf)) { return $null }

    try {
        $loaded = [AppDomain]::CurrentDomain.GetAssemblies() | Where-Object { $_.GetName().Name -eq "Npgsql" }
        if (-not $loaded) { Add-Type -Path $assemblyPath }
        return [Npgsql.NpgsqlConnectionStringBuilder]::new($connectionString)
    }
    catch {
        return $null
    }
}

function Get-PgCliBase([string]$connection, [ref]$database, [ref]$oldPassword, [ref]$oldSslMode) {
    $oldPassword.Value = [Environment]::GetEnvironmentVariable("PGPASSWORD","Process")
    $oldSslMode.Value = [Environment]::GetEnvironmentVariable("PGSSLMODE","Process")
    $normalized = $connection.Trim().Trim('"')

    if ($normalized -match '^(?i)postgres(?:ql)?://') {
        $uri = [Uri]$normalized
        if ([string]::IsNullOrWhiteSpace($uri.UserInfo)) { throw "PostgreSQL URI must include credentials." }
        $parts = $uri.UserInfo.Split(':',2)
        $user = [Uri]::UnescapeDataString($parts[0])
        $password = if ($parts.Count -eq 2) { [Uri]::UnescapeDataString($parts[1]) } else { $null }
        $db = $uri.AbsolutePath.TrimStart("/")
        if ([string]::IsNullOrWhiteSpace($db)) { $db = $user }
        if ([string]::IsNullOrWhiteSpace($password)) {
            foreach ($part in $uri.Query.TrimStart("?").Split("&",[StringSplitOptions]::RemoveEmptyEntries)) {
                $kv = $part.Split("=",2)
                if ($kv[0] -ieq "password" -and $kv.Count -eq 2) { $password = [Uri]::UnescapeDataString($kv[1]) }
            }
        }
        if (-not [string]::IsNullOrWhiteSpace($password)) { $env:PGPASSWORD = $password }
        $port = if ($uri.Port -gt 0) { $uri.Port } else { 5432 }
        $args = @("--host=$($uri.Host)","--port=$port","--username=$user","--dbname=$db")
        $database.Value = $db
        return $args
    }

    $npgsql = Get-NpgsqlConnectionBuilder $normalized
    if ($npgsql) {
        $pgHost = $npgsql.Host
        $port = $npgsql.Port
        $username = $npgsql.Username
        $password = $npgsql.Password
        $db = $npgsql.Database
        $sslMode = $npgsql.SslMode.ToString()
        if (-not [string]::IsNullOrWhiteSpace($sslMode)) { $env:PGSSLMODE = $sslMode.ToLowerInvariant() }
        if ([string]::IsNullOrWhiteSpace($username)) { throw "Npgsql parser returned an empty PostgreSQL username." }
        if ([string]::IsNullOrWhiteSpace($db)) { $db = $username }
        if ([string]::IsNullOrWhiteSpace($pgHost)) { $pgHost = "localhost" }
        if (-not [string]::IsNullOrWhiteSpace($password)) { $env:PGPASSWORD = $password }
        $args = @("--host=$pgHost","--port=$port","--username=$username","--dbname=$db")
        $database.Value = $db
        return $args
    }

    throw "Could not parse GAMENET_DATABASE_CONNECTION with NpgsqlConnectionStringBuilder."
}

$connection = [Environment]::GetEnvironmentVariable("GAMENET_DATABASE_CONNECTION", "Process")
if ([string]::IsNullOrWhiteSpace($connection)) {
    $connection = [Environment]::GetEnvironmentVariable("GAMENET_DATABASE_CONNECTION", "Machine")
}
if ([string]::IsNullOrWhiteSpace($connection)) { throw "GAMENET_DATABASE_CONNECTION is required." }

$root = Join-Path ([IO.Path]::GetTempPath()) ("gamenet5-agent-cert-" + [Guid]::NewGuid().ToString("N"))
$identityRoot = Join-Path $root "identity"
$identityRoot2 = Join-Path $root "identity-2"
New-Item -ItemType Directory -Force -Path $identityRoot,$identityRoot2 | Out-Null

$deviceId = "cert-" + [Guid]::NewGuid().ToString("N")
$identityJson = @{ DeviceId = $deviceId } | ConvertTo-Json
$identityJson | Set-Content -LiteralPath (Join-Path $identityRoot "identity.json") -Encoding utf8
$identityJson | Set-Content -LiteralPath (Join-Path $identityRoot2 "identity.json") -Encoding utf8

$serverUrl = "http://127.0.0.1:5095"
$signingKey = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(48))
$provisioningKey = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(48))
$envNames = @(
    "ASPNETCORE_URLS",
    "GameNet__Authentication__Enabled",
    "GameNet__Authentication__Issuer",
    "GameNet__Authentication__Audience",
    "GameNet__Authentication__SigningKey",
    "GameNet__Agent__ProvisioningKey",
    "GameNet__Agent__LeaseDurationSeconds",
    "GameNet__Agent__HeartbeatIntervalSeconds",
    "GameNet__Agent__AccessTokenLifetimeSeconds",
    "GameNet__AgentIdentity__RootPath",
    "GameNet__AgentTransport__ServerBaseUrl",
    "GAMENET_AGENT_BOOTSTRAP_SECRET"
)
$old = @{}
foreach ($name in $envNames) { $old[$name] = [Environment]::GetEnvironmentVariable($name, "Process") }

$serverLog = Join-Path $root "server.log"
$serverErr = $serverLog + ".err"
$agentLog = Join-Path $root "agent.log"
$agentErr = $agentLog + ".err"
$agent2Log = Join-Path $root "agent2.log"
$agent2Err = $agent2Log + ".err"
$server = $null
$agent = $null
$agent2 = $null
$testSucceeded = $false
$failureMessage = $null
$diagnosticRoot = Join-Path (Get-Location) "artifacts/foundation"
$runToken = [Guid]::NewGuid().ToString("N")
$pgDatabase = $null
$oldPgPassword = $null
$oldPgSslMode = $null
$pgBase = $null

try {
    $env:ASPNETCORE_URLS = $serverUrl
    $env:GameNet__Authentication__Enabled = "true"
    $env:GameNet__Authentication__Issuer = "GameNet5.Foundation.Certification"
    $env:GameNet__Authentication__Audience = "GameNet5.Agent"
    $env:GameNet__Authentication__SigningKey = $signingKey
    $env:GameNet__Agent__ProvisioningKey = $provisioningKey
    $env:GameNet__Agent__LeaseDurationSeconds = "15"
    $env:GameNet__Agent__HeartbeatIntervalSeconds = "5"
    $env:GameNet__Agent__AccessTokenLifetimeSeconds = "300"

    $pgBase = Get-PgCliBase $connection ([ref]$pgDatabase) ([ref]$oldPgPassword) ([ref]$oldPgSslMode)

    $server = Start-Process -FilePath $dotnet -ArgumentList @("run","--project","src/Server/GameNet.Server.csproj","--configuration","Release","--no-build","--no-restore") -RedirectStandardOutput $serverLog -RedirectStandardError $serverErr -PassThru

    $ready = $false
    for ($i = 0; $i -lt 60; $i++) {
        Start-Sleep -Seconds 1
        try {
            $health = Invoke-RestMethod -Uri "$serverUrl/health" -TimeoutSec 2
            if ($health.data.readiness -eq "Ready") { $ready = $true; break }
        } catch {}
    }
    if (-not $ready) { throw "Certification Server did not become ready. See $serverLog." }

    $headers = @{
        "X-GameNet-Agent-Provisioning-Key" = $provisioningKey
        "X-GameNet-Contract" = "v1"
    }
    $body = @{ deviceId = $deviceId } | ConvertTo-Json
    $issued = Invoke-RestMethod -Method Post -Uri "$serverUrl/api/v1/agent/credentials/provision" -Headers $headers -ContentType "application/json" -Body $body
    $secret = $issued.secret

    $env:GameNet__AgentIdentity__RootPath = $identityRoot
    $env:GameNet__AgentTransport__ServerBaseUrl = $serverUrl
    $env:GAMENET_AGENT_BOOTSTRAP_SECRET = $secret

    $agent = Start-Process -FilePath $dotnet -ArgumentList @("run","--project","src/Client/GameNet.Agent.csproj","--configuration","Release","--no-build","--no-restore") -RedirectStandardOutput $agentLog -RedirectStandardError $agentErr -PassThru

    $leaseConnectionId = $null
    for ($i = 0; $i -lt 60; $i++) {
        Start-Sleep -Seconds 1
        $query = "SELECT connection_id FROM agent_connection_leases WHERE device_id = '$deviceId';"
        $value = & (Join-Path $env:ProgramFiles "PostgreSQL\17\bin\psql.exe") @pgBase "--dbname=$pgDatabase" "-Atc" $query 2>$null
        if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace(($value -join ""))) {
            $leaseConnectionId = ($value -join "").Trim()
            break
        }
    }
    if ([string]::IsNullOrWhiteSpace($leaseConnectionId)) { throw "Agent did not acquire an authoritative lease. See $agentLog." }

    $healthQuery = @"
SELECT CASE
  WHEN last_heartbeat_at_utc IS NOT NULL
   AND last_heartbeat_at_utc >= NOW() - INTERVAL '20 seconds'
   AND last_heartbeat_at_utc <= NOW() + INTERVAL '2 seconds'
   AND NULLIF(agent_version, '') IS NOT NULL
   AND NULLIF(station_state, '') IS NOT NULL
  THEN 'fresh'
  ELSE 'stale'
END
FROM agent_connection_leases
WHERE device_id = '$deviceId';
"@
    $heartbeatHealthy = $false
    for ($i = 0; $i -lt 30; $i++) {
        Start-Sleep -Seconds 1
        $health = (& (Join-Path $env:ProgramFiles "PostgreSQL\17\bin\psql.exe") @pgBase "--dbname=$pgDatabase" "-Atc" $healthQuery 2>$null) -join ""
        if ($LASTEXITCODE -eq 0 -and $health.Trim() -eq "fresh") {
            $heartbeatHealthy = $true
            break
        }
    }
    if (-not $heartbeatHealthy) { throw "Agent lease exists, but Server-observed heartbeat/version/state did not become fresh. See $agentLog." }

    $heartbeatTimeQuery = "SELECT last_heartbeat_at_utc::text FROM agent_connection_leases WHERE device_id = '$deviceId';"
    $heartbeatBefore = (& (Join-Path $env:ProgramFiles "PostgreSQL\17\bin\psql.exe") @pgBase "--dbname=$pgDatabase" "-Atc" $heartbeatTimeQuery 2>$null) -join ""
    Start-Sleep -Seconds 6
    $heartbeatAfter = (& (Join-Path $env:ProgramFiles "PostgreSQL\17\bin\psql.exe") @pgBase "--dbname=$pgDatabase" "-Atc" $heartbeatTimeQuery 2>$null) -join ""
    if ([string]::IsNullOrWhiteSpace($heartbeatBefore) -or [string]::IsNullOrWhiteSpace($heartbeatAfter) -or $heartbeatBefore.Trim() -eq $heartbeatAfter.Trim()) {
        throw "Agent heartbeat timestamp did not advance while the Agent remained connected. See $agentLog."
    }

    $healthFieldsQuery = "SELECT COALESCE(agent_version,'') || '|' || COALESCE(station_state,'') FROM agent_connection_leases WHERE device_id = '$deviceId';"
    $healthFields = (& (Join-Path $env:ProgramFiles "PostgreSQL\17\bin\psql.exe") @pgBase "--dbname=$pgDatabase" "-Atc" $healthFieldsQuery 2>$null) -join ""
    if ($healthFields -notmatch '^.+\|Ready$') {
        throw "Agent version or reported station state is missing or invalid: $healthFields"
    }
    $env:GameNet__AgentIdentity__RootPath = $identityRoot2
    $agent2 = Start-Process -FilePath $dotnet -ArgumentList @("run","--project","src/Client/GameNet.Agent.csproj","--configuration","Release","--no-build","--no-restore") -RedirectStandardOutput $agent2Log -RedirectStandardError $agent2Err -PassThru
    Start-Sleep -Seconds 8

    $query = "SELECT connection_id FROM agent_connection_leases WHERE device_id = '$deviceId';"
    $afterSecond = (& (Join-Path $env:ProgramFiles "PostgreSQL\17\bin\psql.exe") @pgBase "--dbname=$pgDatabase" "-Atc" $query 2>$null) -join ""
    if ($afterSecond.Trim() -ne $leaseConnectionId) {
        throw "Agent fencing failed: a second connection replaced the authoritative lease."
    }

    $authoritativeHeartbeat = (& (Join-Path $env:ProgramFiles "PostgreSQL\17\bin\psql.exe") @pgBase "--dbname=$pgDatabase" "-Atc" $heartbeatTimeQuery 2>$null) -join ""
    if ([string]::IsNullOrWhiteSpace($authoritativeHeartbeat.Trim())) {
        throw "A competing Agent cleared or invalidated the authoritative heartbeat."
    }

    Stop-Process -Id $agent2.Id -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 2

    Stop-Process -Id $agent.Id -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 4

    $query = "SELECT connection_id FROM agent_connection_leases WHERE device_id = '$deviceId';"
    $released = (& (Join-Path $env:ProgramFiles "PostgreSQL\17\bin\psql.exe") @pgBase "--dbname=$pgDatabase" "-Atc" $query 2>$null) -join ""
    if (-not [string]::IsNullOrWhiteSpace($released.Trim())) {
        throw "Agent lease was not released by the owning connection."
    }

    $env:GameNet__AgentIdentity__RootPath = $identityRoot
    $agent = Start-Process -FilePath $dotnet -ArgumentList @("run","--project","src/Client/GameNet.Agent.csproj","--configuration","Release","--no-build","--no-restore") -RedirectStandardOutput $agentLog -RedirectStandardError $agentErr -PassThru

    $reconnected = $false
    for ($i = 0; $i -lt 40; $i++) {
        Start-Sleep -Seconds 1
        $value = (& (Join-Path $env:ProgramFiles "PostgreSQL\17\bin\psql.exe") @pgBase "--dbname=$pgDatabase" "-Atc" $query 2>$null) -join ""
        if (-not [string]::IsNullOrWhiteSpace($value.Trim()) -and $value.Trim() -ne $leaseConnectionId) {
            $reconnected = $true
            break
        }
    }
    if (-not $reconnected) { throw "Agent reconnect did not establish a new authoritative connection." }

    Write-Host "AGENT RUNTIME / AUTH / HEARTBEAT / FENCING / RECONNECT CERTIFICATION PASSED."
    $testSucceeded = $true
}
catch {
    $failureMessage = $_.Exception.ToString()
    throw
}
finally {
    if ($agent) { Stop-Process -Id $agent.Id -Force -ErrorAction SilentlyContinue }
    if ($agent2) { Stop-Process -Id $agent2.Id -Force -ErrorAction SilentlyContinue }
    if ($server) { Stop-Process -Id $server.Id -Force -ErrorAction SilentlyContinue }

    foreach ($name in $envNames) {
        $previous = $old[$name]
        if ($null -eq $previous) {
            Remove-Item "Env:$name" -ErrorAction SilentlyContinue
        }
        else {
            Set-Item "Env:$name" $previous
        }
    }

    if ($null -eq $oldPgPassword) { Remove-Item Env:PGPASSWORD -ErrorAction SilentlyContinue } else { $env:PGPASSWORD = $oldPgPassword }
    if ($null -eq $oldPgSslMode) { Remove-Item Env:PGSSLMODE -ErrorAction SilentlyContinue } else { $env:PGSSLMODE = $oldPgSslMode }
    if (-not $testSucceeded) {
        New-Item -ItemType Directory -Force -Path $diagnosticRoot | Out-Null

        $logFiles = @(
            @{ Path = $serverLog; Name = "server.log" },
            @{ Path = $serverErr; Name = "server.err" },
            @{ Path = $agentLog; Name = "agent.log" },
            @{ Path = $agentErr; Name = "agent.err" },
            @{ Path = $agent2Log; Name = "agent-second.log" },
            @{ Path = $agent2Err; Name = "agent-second.err" }
        )

        foreach ($entry in $logFiles) {
            if (-not (Test-Path -LiteralPath $entry.Path -PathType Leaf)) { continue }
            $content = [System.IO.File]::ReadAllText($entry.Path)
            $content = [Regex]::Replace($content, '(?i)(password|pwd|signingkey|provisioningkey|access_token|refresh_token|client_secret|token|GAMENET_AGENT_BOOTSTRAP_SECRET)\s*([=:])\s*("[^"]*"|[^;\s,}]+)', '$1$2<redacted>')
            $content = [Regex]::Replace($content, '(?i)("(?:secret|access_token|accessToken|refresh_token|client_secret|token|authorization)"\s*:\s*")[^"]*(")', '$1<redacted>$2')
            $content = [Regex]::Replace($content, '(?i)(Bearer\s+)[A-Za-z0-9._~+/\-=]+', '$1<redacted>')
            $content = [Regex]::Replace($content, '\beyJ[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}\b', '<redacted-jwt>')
            $destination = Join-Path $diagnosticRoot ("agent-runtime-" + $runToken + "-" + $entry.Name)
            Set-Content -LiteralPath $destination -Value $content -Encoding utf8
        }

        $safeFailure = [string]$failureMessage
        $safeFailure = [Regex]::Replace($safeFailure, '(?i)(password|pwd|signingkey|provisioningkey|access_token|refresh_token|client_secret|token|GAMENET_AGENT_BOOTSTRAP_SECRET)\s*([=:])\s*("[^"]*"|[^;\s,}]+)', '$1$2<redacted>')
        $safeFailure = [Regex]::Replace($safeFailure, '(?i)(Bearer\s+)[A-Za-z0-9._~+/\-=]+', '$1<redacted>')
        $diagnosticLogPaths = @(
            $logFiles |
                ForEach-Object { Join-Path $diagnosticRoot ('agent-runtime-' + $runToken + '-' + $_.Name) } |
                Where-Object { Test-Path -LiteralPath $_ }
        )
        $diagnosticPath = Join-Path $diagnosticRoot ("agent-runtime-diagnostic-" + $runToken + ".txt")
        @(
            "commit=$((git rev-parse HEAD 2>$null))"
            "branch=$((git branch --show-current 2>$null))"
            "timestampUtc=$([DateTime]::UtcNow.ToString('O'))"
            "failure=$safeFailure"
            "diagnosticLogs=$($diagnosticLogPaths -join ';')"
        ) | Set-Content -LiteralPath $diagnosticPath -Encoding utf8

        Write-Host "Agent runtime diagnostics preserved under $diagnosticRoot."
    }

    Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
}
