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
    "GameNet__AgentIdentity__RootPath",
    "GameNet__AgentTransport__ServerBaseUrl",
    "GAMENET_AGENT_BOOTSTRAP_SECRET",
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

function Read-DiagnosticText([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return "" }

    $lastError = ""
    for ($attempt = 1; $attempt -le 6; $attempt++) {
        try {
            return [System.IO.File]::ReadAllText($Path)
        }
        catch {
            $lastError = $_.Exception.Message
            Start-Sleep -Milliseconds 250
        }
    }

    return "DIAGNOSTIC_READ_FAILED after retries: $lastError"
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
$agentIdentityRoot = Join-Path $root "agent-identity"
$agentLog = Join-Path $root "agent.log"
$agentErrorLog = Join-Path $root "agent.err"
$agentProcess = $null
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
$basicOperatorPassword = $null
$basicToken = $null
$managerPassword = $null
$managerToken = $null
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

    $oversizedUsernameLogin = Invoke-IdentityRequest "POST" "/api/v1/auth/login" $contractHeaders @{
        username = ("u" * 65)
        password = $password
    }
    Assert-ErrorCode $oversizedUsernameLogin 401 "auth.invalid_credentials" "Reject overlong operator username"

    $oversizedPasswordLogin = Invoke-IdentityRequest "POST" "/api/v1/auth/login" $contractHeaders @{
        username = $username
        password = ("x" * 257)
    }
    Assert-ErrorCode $oversizedPasswordLogin 401 "auth.invalid_credentials" "Reject overlong operator password"

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

    # Exercise a real Server-to-Agent command against this isolated runtime Server.
    New-Item -ItemType Directory -Force -Path $agentIdentityRoot | Out-Null
    @{ DeviceId = $stationDeviceId } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $agentIdentityRoot "identity.json") -Encoding utf8
    $env:GameNet__AgentIdentity__RootPath = $agentIdentityRoot
    $env:GameNet__AgentTransport__ServerBaseUrl = $serverUrl
    $env:GAMENET_AGENT_BOOTSTRAP_SECRET = [string]$provisionedAgent.Json.data.secret
    $agentProcess = Start-Process -FilePath $dotnetPath -ArgumentList @("run","--project","src/Client/GameNet.Agent.csproj","--configuration","Release","--no-build","--no-restore") -WorkingDirectory (Get-Location) -RedirectStandardOutput $agentLog -RedirectStandardError $agentErrorLog -PassThru

    $agentOnline = $false
    for ($attempt = 1; $attempt -le 30; $attempt++) {
        Start-Sleep -Seconds 1
        $liveStations = Invoke-IdentityRequest "GET" "/api/v1/stations" $operatorHeaders
        Assert-Status $liveStations 200 "Read station state while Agent starts"
        $liveStation = @($liveStations.Json.data | Where-Object { $_.id -eq $stationId }) | Select-Object -First 1
        if ($liveStation -and $liveStation.agentOnline -eq $true -and -not [string]::IsNullOrWhiteSpace([string]$liveStation.lastHeartbeatAtUtc)) { $agentOnline = $true; break }
    }
    if (-not $agentOnline) { throw "The real Agent did not become online with a server-observed heartbeat." }

    $healthProbe = Invoke-IdentityRequest "POST" "/api/v1/stations/$stationId/agent/health-probe" $operatorHeaders
    Assert-Status $healthProbe 200 "Dispatch safe Agent health-probe command"
    if ($healthProbe.Json.data.status -ne 1 -or [string]$healthProbe.Json.data.commandId -eq [Guid]::Empty.ToString() -or
        [string]$healthProbe.Json.data.stationId -ne $stationId -or $healthProbe.Json.data.deviceId -ne $stationDeviceId -or
        [string]::IsNullOrWhiteSpace([string]$healthProbe.Json.data.agentVersion) -or $healthProbe.Json.data.stationState -ne "Ready") {
        throw "The Agent health-probe command did not return a valid success acknowledgement."
    }
    }

    
    # Operator/role management security boundary tests.
    $roles = Invoke-IdentityRequest "GET" "/api/v1/identity/roles" $operatorHeaders
    Assert-Status $roles 200 "List operator roles"
    $ownerRole = @($roles.Json.data | Where-Object { $_.code -eq "owner" }) | Select-Object -First 1
    if (-not $ownerRole -or -not ($ownerRole.permissions -contains "identity.roles.manage")) { throw "Owner role is missing current permission catalog." }

    $basicRole = Invoke-IdentityRequest "POST" "/api/v1/identity/roles" @{
        "X-GameNet-Contract" = "v1"; Authorization = "Bearer $accessToken"
        "Idempotency-Key" = ("ci-role-" + [Guid]::NewGuid().ToString("N"))
    } @{ code = "readonly-" + [Guid]::NewGuid().ToString("N").Substring(0, 8); name = "CI Readonly"; permissions = @("stations.read") }
    Assert-Status $basicRole 201 "Create least-privilege role"
    $basicRoleId = [string]$basicRole.Json.data.id
    $basicUsername = "ci-basic-" + [Guid]::NewGuid().ToString("N").Substring(0, 10)
    $basicOperatorPassword = "CI-basic-" + [Guid]::NewGuid().ToString("N")
    $basicUserKey = "ci-user-" + [Guid]::NewGuid().ToString("N")
    $basicUserHeaders = @{ "X-GameNet-Contract" = "v1"; Authorization = "Bearer $accessToken"; "Idempotency-Key" = $basicUserKey }
    $basicUserBody = @{ username = $basicUsername; displayName = "CI Basic"; password = $basicOperatorPassword; roleId = $basicRoleId }
    $basicUser = Invoke-IdentityRequest "POST" "/api/v1/identity/users" $basicUserHeaders $basicUserBody
    Assert-Status $basicUser 201 "Create limited operator"
    $basicUserId = [string]$basicUser.Json.data.id
    $basicReplay = Invoke-IdentityRequest "POST" "/api/v1/identity/users" $basicUserHeaders $basicUserBody
    Assert-Status $basicReplay 201 "Replay operator creation with same idempotency key"
    if ([string]$basicReplay.Json.data.id -ne $basicUserId) { throw "Operator idempotency replay returned a different user." }

    $basicLogin = Invoke-IdentityRequest "POST" "/api/v1/auth/login" $contractHeaders @{ username = $basicUsername; password = $basicOperatorPassword }
    Assert-Status $basicLogin 200 "Login limited operator"
    $basicToken = [string]$basicLogin.Json.data.accessToken
    if ($basicLogin.Json.data.permissions -contains "identity.roles.manage" -or $basicLogin.Json.data.permissions -contains "identity.users.write") {
        throw "Limited operator received privileges not granted by its role."
    }
    $basicHeaders = @{ "X-GameNet-Contract" = "v1"; Authorization = "Bearer $basicToken" }
    $missingPermission = Invoke-IdentityRequest "GET" "/api/v1/identity/users" $basicHeaders
    Assert-Status $missingPermission 403 "Deny identity user listing without permission"

    $basicPermissionUpdate = Invoke-IdentityRequest "PUT" "/api/v1/identity/roles/$basicRoleId/permissions" @{
        "X-GameNet-Contract" = "v1"; Authorization = "Bearer $accessToken"
        "Idempotency-Key" = ("ci-role-perm-" + [Guid]::NewGuid().ToString("N"))
    } @{ permissions = @("stations.read", "identity.users.read") }
    Assert-Status $basicPermissionUpdate 200 "Owner updates role permissions"
    $basicNowCanRead = Invoke-IdentityRequest "GET" "/api/v1/identity/users" $basicHeaders
    Assert-Status $basicNowCanRead 200 "Refresh role permissions from authoritative Server state"

    # Self-service password change: verify credential ownership, no-op rejection,
    # idempotent retry, current-session continuity, and the new credential.
    $newBasicPassword = "CI-basic-updated-" + [Guid]::NewGuid().ToString("N")
    $badCurrentPassword = Invoke-IdentityRequest "PUT" "/api/v1/identity/me/password" @{
        "X-GameNet-Contract" = "v1"; Authorization = "Bearer $basicToken"
        "Idempotency-Key" = ("ci-bad-current-" + [Guid]::NewGuid().ToString("N"))
    } @{ currentPassword = ("incorrect-" + [Guid]::NewGuid().ToString("N")); newPassword = $newBasicPassword }
    Assert-ErrorCode $badCurrentPassword 403 "identity.current_password_invalid" "Reject password change with incorrect current password"

    $unchangedPassword = Invoke-IdentityRequest "PUT" "/api/v1/identity/me/password" @{
        "X-GameNet-Contract" = "v1"; Authorization = "Bearer $basicToken"
        "Idempotency-Key" = ("ci-unchanged-password-" + [Guid]::NewGuid().ToString("N"))
    } @{ currentPassword = $basicOperatorPassword; newPassword = $basicOperatorPassword }
    Assert-ErrorCode $unchangedPassword 409 "identity.password_unchanged" "Reject unchanged operator password"

    $selfPasswordChangeKey = "ci-self-password-" + [Guid]::NewGuid().ToString("N")
    $selfPasswordBody = @{ currentPassword = $basicOperatorPassword; newPassword = $newBasicPassword }
    $selfPasswordHeaders = @{
        "X-GameNet-Contract" = "v1"; Authorization = "Bearer $basicToken"; "Idempotency-Key" = $selfPasswordChangeKey
    }
    $selfPasswordChange = Invoke-IdentityRequest "PUT" "/api/v1/identity/me/password" $selfPasswordHeaders $selfPasswordBody
    Assert-Status $selfPasswordChange 200 "Change own operator password"
    if ($selfPasswordChange.Json.data.otherSessionsRevoked -ne $true) {
        throw "Self-service password change did not report revocation of other sessions."
    }
    $selfPasswordReplay = Invoke-IdentityRequest "PUT" "/api/v1/identity/me/password" $selfPasswordHeaders $selfPasswordBody
    Assert-Status $selfPasswordReplay 200 "Replay identical own-password change"
    $selfPasswordDifferentPayload = Invoke-IdentityRequest "PUT" "/api/v1/identity/me/password" $selfPasswordHeaders @{
        currentPassword = $basicOperatorPassword; newPassword = ("different-" + [Guid]::NewGuid().ToString("N"))
    }
    Assert-ErrorCode $selfPasswordDifferentPayload 409 "idempotency.key_reused" "Reject reuse of a password idempotency key with different content"

    $currentSessionAfterPasswordChange = Invoke-IdentityRequest "GET" "/api/v1/auth/me" $basicHeaders
    Assert-Status $currentSessionAfterPasswordChange 200 "Keep the current session active after self-service password change"
    $oldPasswordLoginAfterChange = Invoke-IdentityRequest "POST" "/api/v1/auth/login" $contractHeaders @{
        username = $basicUsername; password = $basicOperatorPassword
    }
    Assert-ErrorCode $oldPasswordLoginAfterChange 401 "auth.invalid_credentials" "Reject old operator password after self-service change"
    $newPasswordLogin = Invoke-IdentityRequest "POST" "/api/v1/auth/login" $contractHeaders @{
        username = $basicUsername; password = $newBasicPassword
    }
    Assert-Status $newPasswordLogin 200 "Login with changed operator password"
    $basicTokenPostChange = [string]$newPasswordLogin.Json.data.accessToken
    $basicHeadersPostChange = @{
        "X-GameNet-Contract" = "v1"; Authorization = "Bearer $basicTokenPostChange"
    }

    $basicLogout = Invoke-IdentityRequest "POST" "/api/v1/auth/logout" $basicHeaders
    Assert-Status $basicLogout 200 "Logout limited operator"

    $managerRole = Invoke-IdentityRequest "POST" "/api/v1/identity/roles" @{
        "X-GameNet-Contract" = "v1"; Authorization = "Bearer $accessToken"
        "Idempotency-Key" = ("ci-mgr-role-" + [Guid]::NewGuid().ToString("N"))
    } @{
        code = "manager-" + [Guid]::NewGuid().ToString("N").Substring(0, 8)
        name = "CI Restricted Manager"
        permissions = @("identity.roles.manage", "identity.users.read", "identity.users.write", "stations.read")
    }
    Assert-Status $managerRole 201 "Create constrained role manager"
    $managerUsername = "ci-manager-" + [Guid]::NewGuid().ToString("N").Substring(0, 10)
    $managerPassword = "CI-manager-" + [Guid]::NewGuid().ToString("N")
    $managerUser = Invoke-IdentityRequest "POST" "/api/v1/identity/users" @{
        "X-GameNet-Contract" = "v1"; Authorization = "Bearer $accessToken"
        "Idempotency-Key" = ("ci-mgr-user-" + [Guid]::NewGuid().ToString("N"))
    } @{ username = $managerUsername; displayName = "CI Manager"; password = $managerPassword; roleId = [string]$managerRole.Json.data.id }
    Assert-Status $managerUser 201 "Create constrained role manager account"
    $managerTokenResponse = Invoke-IdentityRequest "POST" "/api/v1/auth/login" $contractHeaders @{ username = $managerUsername; password = $managerPassword }
    Assert-Status $managerTokenResponse 200 "Login constrained role manager"
    $managerToken = [string]$managerTokenResponse.Json.data.accessToken
    $managerHeaders = @{ "X-GameNet-Contract" = "v1"; Authorization = "Bearer $managerToken" }

    $managerEscalates = Invoke-IdentityRequest "POST" "/api/v1/identity/roles" @{
        "X-GameNet-Contract" = "v1"; Authorization = "Bearer $managerToken"
        "Idempotency-Key" = ("ci-escalate-" + [Guid]::NewGuid().ToString("N"))
    } @{ code = "blocked-" + [Guid]::NewGuid().ToString("N").Substring(0, 8); name = "No escalation"; permissions = @("settings.write") }
    Assert-ErrorCode $managerEscalates 403 "identity.permission_escalation" "Deny privilege escalation when creating role"

    $managerEditsOwner = Invoke-IdentityRequest "PUT" "/api/v1/identity/users/$($bootstrap.Json.data.userId)/active" @{
        "X-GameNet-Contract" = "v1"; Authorization = "Bearer $managerToken"
        "Idempotency-Key" = ("ci-disable-owner-" + [Guid]::NewGuid().ToString("N"))
    } @{ isActive = $false }
    Assert-ErrorCode $managerEditsOwner 403 "identity.owner_protected" "Deny non-owner from disabling Owner"


    # Password reset is a separate privilege and Owner accounts remain protected.
    $managerResetDenied = Invoke-IdentityRequest "PUT" "/api/v1/identity/users/$basicUserId/password" @{
        "X-GameNet-Contract" = "v1"; Authorization = "Bearer $managerToken"
        "Idempotency-Key" = ("ci-manager-reset-denied-" + [Guid]::NewGuid().ToString("N"))
    } @{ newPassword = ("Denied-" + [Guid]::NewGuid().ToString("N")); reason = "CI permission boundary" }
    Assert-Status $managerResetDenied 403 "Deny password reset without its dedicated permission"

    $grantPasswordReset = Invoke-IdentityRequest "PUT" "/api/v1/identity/roles/$($managerRole.Json.data.id)/permissions" @{
        "X-GameNet-Contract" = "v1"; Authorization = "Bearer $accessToken"
        "Idempotency-Key" = ("ci-grant-password-reset-" + [Guid]::NewGuid().ToString("N"))
    } @{ permissions = @("identity.roles.manage", "identity.users.read", "identity.users.write", "identity.users.password-reset", "stations.read") }
    Assert-Status $grantPasswordReset 200 "Grant dedicated password-reset permission"

    $managerResetOwner = Invoke-IdentityRequest "PUT" "/api/v1/identity/users/$($bootstrap.Json.data.userId)/password" @{
        "X-GameNet-Contract" = "v1"; Authorization = "Bearer $managerToken"
        "Idempotency-Key" = ("ci-manager-reset-owner-" + [Guid]::NewGuid().ToString("N"))
    } @{ newPassword = ("Blocked-Owner-" + [Guid]::NewGuid().ToString("N")); reason = "CI Owner protection test" }
    Assert-ErrorCode $managerResetOwner 403 "identity.owner_protected" "Prevent a non-Owner from resetting an Owner password"

    $systemRoleEdit = Invoke-IdentityRequest "PUT" "/api/v1/identity/roles/$($ownerRole.id)/permissions" @{
        "X-GameNet-Contract" = "v1"; Authorization = "Bearer $accessToken"
        "Idempotency-Key" = ("ci-system-role-" + [Guid]::NewGuid().ToString("N"))
    } @{ permissions = @("stations.read") }
    Assert-ErrorCode $systemRoleEdit 409 "identity.system_role_immutable" "Protect system Owner role"

    $managerLogout = Invoke-IdentityRequest "POST" "/api/v1/auth/logout" $managerHeaders
    Assert-Status $managerLogout 200 "Logout constrained role manager"


    # Owner administrative reset: reasoned/audited operation, complete session revocation,
    # and safe idempotent replay without repeating revocation side effects.
    $adminResetPassword = "CI-reset-" + [Guid]::NewGuid().ToString("N")
    $adminResetKey = "ci-admin-reset-" + [Guid]::NewGuid().ToString("N")
    $adminResetBody = @{ newPassword = $adminResetPassword; reason = "CI verified operator recovery" }
    $adminResetHeaders = @{
        "X-GameNet-Contract" = "v1"; Authorization = "Bearer $accessToken"; "Idempotency-Key" = $adminResetKey
    }
    $adminPasswordReset = Invoke-IdentityRequest "PUT" "/api/v1/identity/users/$basicUserId/password" $adminResetHeaders $adminResetBody
    Assert-Status $adminPasswordReset 200 "Owner resets another operator password"
    if ($adminPasswordReset.Json.data.operatorId -ne $basicUserId -or
        $adminPasswordReset.Json.data.sessionsRevoked -ne $true) {
        throw "Administrative reset response did not identify the target or confirm session revocation."
    }

    $revokedBasicSession = Invoke-IdentityRequest "GET" "/api/v1/auth/me" $basicHeadersPostChange
    Assert-Status $revokedBasicSession 401 "Administrative reset revokes target's pre-existing sessions"
    $previousPasswordAfterReset = Invoke-IdentityRequest "POST" "/api/v1/auth/login" $contractHeaders @{
        username = $basicUsername; password = $newBasicPassword
    }
    Assert-ErrorCode $previousPasswordAfterReset 401 "auth.invalid_credentials" "Reject pre-reset password after administrative reset"
    $resetPasswordLogin = Invoke-IdentityRequest "POST" "/api/v1/auth/login" $contractHeaders @{
        username = $basicUsername; password = $adminResetPassword
    }
    Assert-Status $resetPasswordLogin 200 "Login with administratively reset password"
    $basicTokenAfterReset = [string]$resetPasswordLogin.Json.data.accessToken
    $basicHeadersAfterReset = @{
        "X-GameNet-Contract" = "v1"; Authorization = "Bearer $basicTokenAfterReset"
    }

    $adminResetReplay = Invoke-IdentityRequest "PUT" "/api/v1/identity/users/$basicUserId/password" $adminResetHeaders $adminResetBody
    Assert-Status $adminResetReplay 200 "Replay identical administrative password reset"
    $adminResetDifferentPayload = Invoke-IdentityRequest "PUT" "/api/v1/identity/users/$basicUserId/password" $adminResetHeaders @{
        newPassword = ("different-reset-" + [Guid]::NewGuid().ToString("N")); reason = "CI verified operator recovery"
    }
    Assert-ErrorCode $adminResetDifferentPayload 409 "idempotency.key_reused" "Reject reset idempotency key reuse with a different password"
    $resetSessionAfterReplay = Invoke-IdentityRequest "GET" "/api/v1/auth/me" $basicHeadersAfterReset
    Assert-Status $resetSessionAfterReplay 200 "Idempotent reset replay preserves a newer session"

$stationList = Invoke-IdentityRequest "GET" "/api/v1/stations" $operatorHeaders
    Assert-Status $stationList 200 "List stations"
    $listedStation = @($stationList.Json.data | Where-Object { $_.id -eq $stationId }) | Select-Object -First 1
    if (-not $listedStation -or $listedStation.agentOnline -ne $true) {
        throw "Station list did not reflect the authoritative online Agent state after the health-probe test."
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
    if ($maintenance.Json.data.status -ne 3 -or $maintenance.Json.data.agentOnline -ne $true) {
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
    if ($agentProcess) { Stop-Process -Id $agentProcess.Id -Force -ErrorAction SilentlyContinue; $agentProcess.Dispose() }
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
        $stdout = Read-DiagnosticText $serverLog
        $stderr = Read-DiagnosticText $serverErrorLog
        foreach ($secretValue in @($bootstrapSecret, $signingKey, $provisioningKey, $password, $wrongPassword, $accessToken, $basicOperatorPassword, $basicToken, $managerPassword, $managerToken)) {
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
