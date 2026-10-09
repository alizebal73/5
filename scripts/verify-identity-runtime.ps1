$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$dotnetPath = Join-Path $env:ProgramFiles "dotnet\dotnet.exe"
if (-not (Test-Path -LiteralPath $dotnetPath -PathType Leaf)) {
    $dotnetPath = (Get-Command dotnet -ErrorAction Stop).Source
}

if ([string]::IsNullOrWhiteSpace($env:GAMENET_DATABASE_CONNECTION)) {
    throw "Identity runtime verification requires the isolated clean PostgreSQL connection from verify-postgresql.ps1."
}

$envNames = @(
    "GAMENET_DATABASE_CONNECTION",
    "GameNet__DatabaseConnectionString",
    "ASPNETCORE_URLS",
    "ASPNETCORE_ENVIRONMENT",
    "DOTNET_ENVIRONMENT",
    "GameNet__Authentication__Enabled",
    "GameNet__Authentication__Issuer",
    "GameNet__Authentication__Audience",
    "GameNet__Authentication__SigningKey",
    "GameNet__Agent__ProvisioningKey",
    "GAMENET_BOOTSTRAP_SECRET",
    "GameNet__Setup__BootstrapSecret"
)
$oldEnvironment = @{}
foreach ($name in $envNames) {
    $oldEnvironment[$name] = [Environment]::GetEnvironmentVariable($name, "Process")
}

function Get-EphemeralPort {
    $listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, 0)
    try {
        $listener.Start()
        return ([System.Net.IPEndPoint]$listener.LocalEndpoint).Port
    }
    finally {
        $listener.Stop()
    }
}

function Invoke-IdentityRequest(
    [string]$Method,
    [string]$Path,
    [hashtable]$Headers,
    [AllowNull()][object]$Body = $null
) {
    $request = @{
        Method = $Method
        Uri = "$serverUrl$Path"
        Headers = $Headers
        TimeoutSec = 10
        SkipHttpErrorCheck = $true
    }
    if ($null -ne $Body) {
        $request.ContentType = "application/json"
        $request.Body = $Body | ConvertTo-Json -Depth 10 -Compress
    }

    $response = Invoke-WebRequest @request
    $json = $null
    if (-not [string]::IsNullOrWhiteSpace($response.Content)) {
        try { $json = $response.Content | ConvertFrom-Json -Depth 20 }
        catch { $json = $null }
    }

    return [pscustomobject]@{
        StatusCode = [int]$response.StatusCode
        Json = $json
    }
}

function Assert-Status($Response, [int]$Expected, [string]$Operation) {
    if ($Response.StatusCode -ne $Expected) {
        $errorCode = $Response.Json.error.code
        throw "$Operation expected HTTP $Expected but received $($Response.StatusCode) (code=$errorCode)."
    }
}

function Assert-ErrorCode($Response, [int]$ExpectedStatus, [string]$ExpectedCode, [string]$Operation) {
    Assert-Status $Response $ExpectedStatus $Operation
    if (-not [string]::Equals([string]$Response.Json.error.code, $ExpectedCode, [StringComparison]::Ordinal)) {
        throw "$Operation returned an unexpected contract error code."
    }
}

$serverUrl = "http://127.0.0.1:$(Get-EphemeralPort)"
$root = Join-Path ([IO.Path]::GetTempPath()) ("gamenet5-identity-runtime-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Force -Path $root | Out-Null
$serverLog = Join-Path $root "server.log"
$serverErrorLog = Join-Path $root "server.err"
$diagnosticRoot = Join-Path (Get-Location) "artifacts/foundation"
$runToken = [Guid]::NewGuid().ToString("N")
$server = $null
$failureMessage = $null
$testSucceeded = $false
$bootstrapSecret = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(48))
$signingKey = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(48))
$provisioningKey = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(48))
$username = "ci-owner-" + [Guid]::NewGuid().ToString("N").Substring(0, 16)
$password = "CI-owner-" + [Guid]::NewGuid().ToString("N")
$wrongPassword = "wrong-" + [Guid]::NewGuid().ToString("N")
$accessToken = $null

try {
    # This runs only while verify-postgresql.ps1 points this process to a brand-new,
    # migrated PostgreSQL database. Never fall back to the machine-level database.
    $env:GameNet__DatabaseConnectionString = $env:GAMENET_DATABASE_CONNECTION
    $env:ASPNETCORE_URLS = $serverUrl
    $env:ASPNETCORE_ENVIRONMENT = "Production"
    $env:DOTNET_ENVIRONMENT = "Production"
    $env:GameNet__Authentication__Enabled = "true"
    $env:GameNet__Authentication__Issuer = "GameNet5.Identity.RuntimeCertification"
    $env:GameNet__Authentication__Audience = "GameNet5.Identity.RuntimeCertification.Client"
    $env:GameNet__Authentication__SigningKey = $signingKey
    $env:GameNet__Agent__ProvisioningKey = $provisioningKey
    $env:GAMENET_BOOTSTRAP_SECRET = $bootstrapSecret
    $env:GameNet__Setup__BootstrapSecret = $bootstrapSecret

    $startArguments = @{
        FilePath = $dotnetPath
        ArgumentList = @("run", "--project", "src/Server/GameNet.Server.csproj", "--configuration", "Release", "--no-build", "--no-restore")
        WorkingDirectory = (Get-Location)
        RedirectStandardOutput = $serverLog
        RedirectStandardError = $serverErrorLog
        PassThru = $true
    }
    $server = Start-Process @startArguments

    $ready = $false
    $deadlineUtc = [DateTime]::UtcNow.AddSeconds(45)
    while ([DateTime]::UtcNow -lt $deadlineUtc) {
        $server.Refresh()
        if ($server.HasExited) {
            throw "Identity smoke Server exited before readiness with exit code $($server.ExitCode)."
        }

        try {
            $health = Invoke-RestMethod -Uri "$serverUrl/health" -TimeoutSec 2
            if ($health.data.readiness -eq "Ready") {
                $ready = $true
                break
            }
        }
        catch { }
        Start-Sleep -Milliseconds 500
    }
    if (-not $ready) { throw "Identity smoke Server did not become ready within 45 seconds." }

    $contractHeaders = @{ "X-GameNet-Contract" = "v1" }
    $status = Invoke-IdentityRequest "GET" "/api/v1/bootstrap/status" $contractHeaders
    Assert-Status $status 200 "Bootstrap status before setup"
    if (-not $status.Json.data.required) { throw "The clean database did not report that initial bootstrap is required." }

    $ownerBody = @{ username = $username; displayName = "CI Identity Owner"; password = $password }
    $missingSecret = Invoke-IdentityRequest "POST" "/api/v1/bootstrap/admin" $contractHeaders $ownerBody
    Assert-ErrorCode $missingSecret 401 "bootstrap.secret_invalid" "Bootstrap without setup secret"

    $badSecretHeaders = @{
        "X-GameNet-Contract" = "v1"
        "X-GameNet-Bootstrap-Secret" = ("wrong-" + $bootstrapSecret)
    }
    $wrongSecret = Invoke-IdentityRequest "POST" "/api/v1/bootstrap/admin" $badSecretHeaders $ownerBody
    Assert-ErrorCode $wrongSecret 401 "bootstrap.secret_invalid" "Bootstrap with incorrect setup secret"

    $setupHeaders = @{
        "X-GameNet-Contract" = "v1"
        "X-GameNet-Bootstrap-Secret" = $bootstrapSecret
    }
    $bootstrap = Invoke-IdentityRequest "POST" "/api/v1/bootstrap/admin" $setupHeaders $ownerBody
    Assert-Status $bootstrap 201 "First-owner bootstrap"
    if ($bootstrap.Json.data.username -ne $username) { throw "The bootstrap response did not return the created owner identity." }

    $statusAfterSetup = Invoke-IdentityRequest "GET" "/api/v1/bootstrap/status" $contractHeaders
    Assert-Status $statusAfterSetup 200 "Bootstrap status after setup"
    if ($statusAfterSetup.Json.data.required) { throw "Bootstrap still reports required after creating the first owner." }

    $secondBootstrap = Invoke-IdentityRequest "POST" "/api/v1/bootstrap/admin" $setupHeaders $ownerBody
    Assert-ErrorCode $secondBootstrap 409 "bootstrap.not_required" "Repeated first-owner bootstrap"

    $loginBody = @{ username = $username; password = $wrongPassword }
    $invalidLogin = Invoke-IdentityRequest "POST" "/api/v1/auth/login" $contractHeaders $loginBody
    Assert-ErrorCode $invalidLogin 401 "auth.invalid_credentials" "Login with incorrect password"

    $loginBody.password = $password
    $login = Invoke-IdentityRequest "POST" "/api/v1/auth/login" $contractHeaders $loginBody
    Assert-Status $login 200 "Valid operator login"
    $accessToken = [string]$login.Json.data.accessToken
    if ([string]::IsNullOrWhiteSpace($accessToken)) { throw "Successful login did not issue an access token." }
    if (-not ($login.Json.data.permissions -contains "identity.users.read")) {
        throw "The initial owner did not receive the expected Identity permission."
    }

    $operatorHeaders = @{ "X-GameNet-Contract" = "v1"; Authorization = "Bearer $accessToken" }
    $current = Invoke-IdentityRequest "GET" "/api/v1/auth/me" $operatorHeaders
    Assert-Status $current 200 "Authenticated current-operator request"
    if ($current.Json.data.username -ne $username) { throw "The current-operator endpoint returned the wrong identity." }

    # Station mutations must be idempotent and Station online state must not be inferred from credential existence.
    $stationCode = "CI-" + [Guid]::NewGuid().ToString("N").Substring(0, 12)
    $stationCreateKey = "ci-station-create-" + [Guid]::NewGuid().ToString("N")
    $stationCreateHeaders = @{
        "X-GameNet-Contract" = "v1"
        Authorization = "Bearer $accessToken"
        "Idempotency-Key" = $stationCreateKey
    }
    $stationCreateBody = @{ code = $stationCode; name = "CI Station"; type = 1 }
    $stationCreate = Invoke-IdentityRequest "POST" "/api/v1/stations" $stationCreateHeaders $stationCreateBody
    Assert-Status $stationCreate 201 "Create station"
    $stationId = [string]$stationCreate.Json.data.id
    if ([string]::IsNullOrWhiteSpace($stationId) -or $stationCreate.Json.data.code -ne $stationCode) {
        throw "Station create did not return the expected station identity."
    }

    $stationReplay = Invoke-IdentityRequest "POST" "/api/v1/stations" $stationCreateHeaders $stationCreateBody
    Assert-Status $stationReplay 201 "Replay identical station create"
    if ([string]$stationReplay.Json.data.id -ne $stationId) {
        throw "Idempotency replay returned a different station."
    }

    $stationDifferentBody = @{ code = $stationCode; name = "Different Station"; type = 1 }
    $stationKeyConflict = Invoke-IdentityRequest "POST" "/api/v1/stations" $stationCreateHeaders $stationDifferentBody
    Assert-ErrorCode $stationKeyConflict 409 "idempotency.key_reused" "Reuse station idempotency key with different payload"

    $stationDeviceId = "ci-station-agent-" + [Guid]::NewGuid().ToString("N")
    $provisionHeaders = @{
        "X-GameNet-Contract" = "v1"
        "X-GameNet-Agent-Provisioning-Key" = $provisioningKey
    }
    $provisionedAgent = Invoke-IdentityRequest "POST" "/api/v1/agent/credentials/provision" $provisionHeaders @{
        deviceId = $stationDeviceId
    }
    Assert-Status $provisionedAgent 200 "Provision Agent credential for station binding"

    $bindHeaders = @{
        "X-GameNet-Contract" = "v1"
        Authorization = "Bearer $accessToken"
        "Idempotency-Key" = ("ci-station-bind-" + [Guid]::NewGuid().ToString("N"))
    }
    $boundStation = Invoke-IdentityRequest "PUT" "/api/v1/stations/$stationId/agent" $bindHeaders @{
        deviceId = $stationDeviceId
        expectedVersion = 1
    }
    Assert-Status $boundStation 200 "Bind provisioned Agent to station"
    if ($boundStation.Json.data.agentDeviceId -ne $stationDeviceId -or
        $boundStation.Json.data.agentOnline -ne $false -or
        $boundStation.Json.data.version -ne 2) {
        throw "Station binding or server-derived offline state was incorrect before Agent connection."
    }

    $stationList = Invoke-IdentityRequest "GET" "/api/v1/stations" $operatorHeaders
    Assert-Status $stationList 200 "List stations"
    $listedStation = @($stationList.Json.data | Where-Object { $_.id -eq $stationId }) | Select-Object -First 1
    if (-not $listedStation -or $listedStation.agentOnline -ne $false) {
        throw "Station list did not reflect the authoritative offline state."
    }

    $secondStationCode = "CI2-" + [Guid]::NewGuid().ToString("N").Substring(0, 10)
    $secondStationHeaders = @{
        "X-GameNet-Contract" = "v1"
        Authorization = "Bearer $accessToken"
        "Idempotency-Key" = ("ci-station-create-" + [Guid]::NewGuid().ToString("N"))
    }
    $secondStation = Invoke-IdentityRequest "POST" "/api/v1/stations" $secondStationHeaders @{
        code = $secondStationCode
        name = "Second CI Station"
        type = 1
    }
    Assert-Status $secondStation 201 "Create second station"
    $duplicateBinding = Invoke-IdentityRequest "PUT" "/api/v1/stations/$($secondStation.Json.data.id)/agent" @{
        "X-GameNet-Contract" = "v1"
        Authorization = "Bearer $accessToken"
        "Idempotency-Key" = ("ci-station-bind-" + [Guid]::NewGuid().ToString("N"))
    } @{
        deviceId = $stationDeviceId
        expectedVersion = 1
    }
    Assert-ErrorCode $duplicateBinding 409 "stations.device_already_bound" "Prevent Agent binding to two stations"

    $staleRename = Invoke-IdentityRequest "PUT" "/api/v1/stations/$stationId" @{
        "X-GameNet-Contract" = "v1"
        Authorization = "Bearer $accessToken"
        "Idempotency-Key" = ("ci-station-stale-" + [Guid]::NewGuid().ToString("N"))
    } @{
        name = "Stale rename"
        expectedVersion = 1
    }
    Assert-ErrorCode $staleRename 409 "stations.version_conflict" "Reject stale station version"

    $maintenance = Invoke-IdentityRequest "PUT" "/api/v1/stations/$stationId/status" @{
        "X-GameNet-Contract" = "v1"
        Authorization = "Bearer $accessToken"
        "Idempotency-Key" = ("ci-station-status-" + [Guid]::NewGuid().ToString("N"))
    } @{
        status = 3
        expectedVersion = 2
    }
    Assert-Status $maintenance 200 "Set station administrative maintenance status"
    if ($maintenance.Json.data.status -ne 3 -or $maintenance.Json.data.agentOnline -ne $false) {
        throw "Administrative status was not kept separate from Agent runtime state."
    }

    $logout = Invoke-IdentityRequest "POST" "/api/v1/auth/logout" $operatorHeaders
    Assert-Status $logout 200 "Operator logout"
    if (-not $logout.Json.data.success) { throw "The logout endpoint did not report success." }

    $revokedRequest = Invoke-IdentityRequest "GET" "/api/v1/auth/me" $operatorHeaders
    Assert-Status $revokedRequest 401 "Request with a revoked session token"

    for ($attempt = 1; $attempt -le 5; $attempt++) {
        $failed = Invoke-IdentityRequest "POST" "/api/v1/auth/login" $contractHeaders @{
            username = $username
            password = $wrongPassword
        }
        Assert-ErrorCode $failed 401 "auth.invalid_credentials" "Failed login attempt $attempt"
    }
    $locked = Invoke-IdentityRequest "POST" "/api/v1/auth/login" $contractHeaders @{
        username = $username
        password = $wrongPassword
    }
    Assert-ErrorCode $locked 423 "auth.locked" "Login after lockout threshold"

    Write-Host "IDENTITY / STATIONS / IDEMPOTENCY / AGENT BINDING / SESSION REVOCATION / LOCKOUT CERTIFICATION PASSED."
    $testSucceeded = $true
}
catch {
    $failureMessage = $_.Exception.ToString()
    throw
}
finally {
    if ($server) {
        $server.Refresh()
        if (-not $server.HasExited) {
            Stop-Process -Id $server.Id -Force -ErrorAction SilentlyContinue
        }
        $server.Dispose()
    }

    if ($testSucceeded) {
        Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
    }
    else {
        $stdout = if (Test-Path -LiteralPath $serverLog -PathType Leaf) { [System.IO.File]::ReadAllText($serverLog) } else { "" }
        $stderr = if (Test-Path -LiteralPath $serverErrorLog -PathType Leaf) { [System.IO.File]::ReadAllText($serverErrorLog) } else { "" }
        foreach ($secretValue in @($bootstrapSecret, $signingKey, $provisioningKey, $password, $wrongPassword, $accessToken)) {
            if (-not [string]::IsNullOrWhiteSpace([string]$secretValue)) {
                $stdout = $stdout.Replace([string]$secretValue, "<redacted>")
                $stderr = $stderr.Replace([string]$secretValue, "<redacted>")
                $failureMessage = $failureMessage.Replace([string]$secretValue, "<redacted>")
            }
        }
        $stdout = [Regex]::Replace($stdout, '(?i)(password|secret|token|signingkey|provisioningkey)\s*([=:])\s*("[^"]*"|[^;\s,}]+)', '$1$2<redacted>')
        $stderr = [Regex]::Replace($stderr, '(?i)(password|secret|token|signingkey|provisioningkey)\s*([=:])\s*("[^"]*"|[^;\s,}]+)', '$1$2<redacted>')

        New-Item -ItemType Directory -Force -Path $diagnosticRoot | Out-Null
        $diagnosticPath = Join-Path $diagnosticRoot "identity-runtime-diagnostic-$runToken.txt"
        @(
            "commit=$((git rev-parse HEAD 2>$null))"
            "branch=$((git branch --show-current 2>$null))"
            "timestampUtc=$([DateTime]::UtcNow.ToString('O'))"
            "failure=$failureMessage"
            "serverStdout:"
            $stdout.Substring(0, [Math]::Min($stdout.Length, 30000))
            "serverStderr:"
            $stderr.Substring(0, [Math]::Min($stderr.Length, 30000))
        ) | Set-Content -LiteralPath $diagnosticPath -Encoding utf8
        Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
        Write-Host "Identity runtime diagnostic written to $diagnosticPath."
    }

    foreach ($name in $envNames) {
        [Environment]::SetEnvironmentVariable($name, $oldEnvironment[$name], "Process")
    }
}
