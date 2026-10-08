 $ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$dotnet = Join-Path $env:ProgramFiles "dotnet\dotnet.exe"
if (-not (Test-Path $dotnet)) { $dotnet = (Get-Command dotnet -ErrorAction Stop).Source }

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
        $value = & (Join-Path $env:ProgramFiles "PostgreSQL\17\bin\psql.exe") $connection "-Atc" $query 2>$null
        if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace(($value -join ""))) {
            $leaseConnectionId = ($value -join "").Trim()
            break
        }
    }
    if ([string]::IsNullOrWhiteSpace($leaseConnectionId)) { throw "Agent did not acquire an authoritative lease. See $agentLog." }

    $env:GameNet__AgentIdentity__RootPath = $identityRoot2
    $agent2 = Start-Process -FilePath $dotnet -ArgumentList @("run","--project","src/Client/GameNet.Agent.csproj","--configuration","Release","--no-build","--no-restore") -RedirectStandardOutput $agent2Log -RedirectStandardError $agent2Err -PassThru
    Start-Sleep -Seconds 8

    $query = "SELECT connection_id FROM agent_connection_leases WHERE device_id = '$deviceId';"
    $afterSecond = (& (Join-Path $env:ProgramFiles "PostgreSQL\17\bin\psql.exe") $connection "-Atc" $query 2>$null) -join ""
    if ($afterSecond.Trim() -ne $leaseConnectionId) {
        throw "Agent fencing failed: a second connection replaced the authoritative lease."
    }

    Stop-Process -Id $agent2.Id -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 2

    Stop-Process -Id $agent.Id -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 4

    $query = "SELECT connection_id FROM agent_connection_leases WHERE device_id = '$deviceId';"
    $released = (& (Join-Path $env:ProgramFiles "PostgreSQL\17\bin\psql.exe") $connection "-Atc" $query 2>$null) -join ""
    if (-not [string]::IsNullOrWhiteSpace($released.Trim())) {
        throw "Agent lease was not released by the owning connection."
    }

    $env:GameNet__AgentIdentity__RootPath = $identityRoot
    $agent = Start-Process -FilePath $dotnet -ArgumentList @("run","--project","src/Client/GameNet.Agent.csproj","--configuration","Release","--no-build","--no-restore") -RedirectStandardOutput $agentLog -RedirectStandardError $agentErr -PassThru

    $reconnected = $false
    for ($i = 0; $i -lt 40; $i++) {
        Start-Sleep -Seconds 1
        $value = (& (Join-Path $env:ProgramFiles "PostgreSQL\17\bin\psql.exe") $connection "-Atc" $query 2>$null) -join ""
        if (-not [string]::IsNullOrWhiteSpace($value.Trim()) -and $value.Trim() -ne $leaseConnectionId) {
            $reconnected = $true
            break
        }
    }
    if (-not $reconnected) { throw "Agent reconnect did not establish a new authoritative connection." }

    Write-Host "AGENT RUNTIME / AUTH / HEARTBEAT / FENCING / RECONNECT CERTIFICATION PASSED."
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

    Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
}
