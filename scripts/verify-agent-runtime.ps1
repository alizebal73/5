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
$serverConfigurationRoot = Join-Path $root "server-config"
$agentConfigurationRoot = Join-Path $root "agent-config"
$agent2ConfigurationRoot = Join-Path $root "agent2-config"
New-Item -ItemType Directory -Force -Path $identityRoot,$identityRoot2,$serverConfigurationRoot,$agentConfigurationRoot,$agent2ConfigurationRoot | Out-Null

$deviceId = "cert-" + [Guid]::NewGuid().ToString("N")
$identityJson = @{ DeviceId = $deviceId } | ConvertTo-Json
$identityJson | Set-Content -LiteralPath (Join-Path $identityRoot "identity.json") -Encoding utf8
$identityJson | Set-Content -LiteralPath (Join-Path $identityRoot2 "identity.json") -Encoding utf8

$serverUrl = "http://127.0.0.1:5095"

function Write-JsonConfiguration([string]$Path, [object]$Value) {
    $json = ($Value | ConvertTo-Json -Depth 12) + [Environment]::NewLine
    [System.IO.File]::WriteAllText($Path, $json, [System.Text.UTF8Encoding]::new($false))
}

function Write-AgentRuntimeConfiguration([string]$ConfigurationRoot, [string]$StateRoot, [string]$BaseUrl) {
    $configuration = [ordered]@{
        GameNet = [ordered]@{
            AgentTransport = [ordered]@{ ServerBaseUrl = $BaseUrl }
            AgentIdentity = [ordered]@{ RootPath = $StateRoot }
        }
    }
    Write-JsonConfiguration (Join-Path $ConfigurationRoot "agent.json") $configuration
}
$signingKey = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(48))
$provisioningKey = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(48))
$envNames = @(
    "ASPNETCORE_URLS",
    "ASPNETCORE_ENVIRONMENT",
    "DOTNET_ENVIRONMENT",
    "GAMENET_TEST_RUNTIME_CONFIG_DIRECTORY",
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
    $env:ASPNETCORE_ENVIRONMENT = "Development"
    $env:DOTNET_ENVIRONMENT = "Development"
    Remove-Item Env:ASPNETCORE_URLS -ErrorAction SilentlyContinue
    Remove-Item Env:GameNet__AgentIdentity__RootPath -ErrorAction SilentlyContinue
    Remove-Item Env:GameNet__AgentTransport__ServerBaseUrl -ErrorAction SilentlyContinue

    $serverConfiguration = [ordered]@{ urls = $serverUrl }
    Write-JsonConfiguration (Join-Path $serverConfigurationRoot "server.json") $serverConfiguration
    $env:GAMENET_TEST_RUNTIME_CONFIG_DIRECTORY = $serverConfigurationRoot
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

    Write-AgentRuntimeConfiguration $agentConfigurationRoot $identityRoot $serverUrl
    $env:GAMENET_TEST_RUNTIME_CONFIG_DIRECTORY = $agentConfigurationRoot
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

    Write-AgentRuntimeConfiguration $agent2ConfigurationRoot $identityRoot2 $serverUrl
    $env:GAMENET_TEST_RUNTIME_CONFIG_DIRECTORY = $agent2ConfigurationRoot
    $agent2 = Start-Process -FilePath $dotnet -ArgumentList @("run","--project","src/Client/GameNet.Agent.csproj","--configuration","Release","--no-build","--no-restore") -RedirectStandardOutput $agent2Log -RedirectStandardError $agent2Err -PassThru
    Start-Sleep -Seconds 8

    $query = "SELECT connection_id FROM agent_connection_leases WHERE device_id = '$deviceId';"
    $afterSecond = (& (Join-Path $env:ProgramFiles "PostgreSQL\17\bin\psql.exe") @pgBase "--dbname=$pgDatabase" "-Atc" $query 2>$null) -join ""
    if ($afterSecond.Trim() -ne $leaseConnectionId) {
        throw "Agent fencing failed: a second connection replaced the authoritative lease."
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

    $env:GAMENET_TEST_RUNTIME_CONFIG_DIRECTORY = $agentConfigurationRoot
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
    # dotnet run owns child host processes which can keep redirected log files locked.
    # Terminate each certification process tree before collecting failure diagnostics.
    foreach ($processToStop in @($agent, $agent2, $server)) {
        if ($null -eq $processToStop) { continue }
        try {
            & (Join-Path $env:SystemRoot "System32\\taskkill.exe") /PID $processToStop.Id /T /F 2>$null | Out-Null
        }
        catch {
            # Continue cleanup and preserve the original certification failure.
        }
        try { $processToStop.WaitForExit(5000) | Out-Null } catch {}
        try { $processToStop.Dispose() } catch {}
    }

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
            try {
                $content = [System.IO.File]::ReadAllText($entry.Path)
            }
            catch {
                # Diagnostic collection must never hide the original Agent/runtime failure.
                $content = "DIAGNOSTIC_READ_FAILURE path=$($entry.Name) message=$($_.Exception.Message)"
            }
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
        Write-Host "Agent runtime failed (redacted summary): $safeFailure"
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
