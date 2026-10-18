#requires -Version 7.0
[CmdletBinding()]
param(
    [switch]$GuiMode,
    [string]$StopSignalPath
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$guiSettings = $null
if ($GuiMode) {
    if ([string]::IsNullOrWhiteSpace($StopSignalPath) -or
        $StopSignalPath.IndexOfAny([System.IO.Path]::GetInvalidPathChars()) -ge 0) {
        throw "GUI test mode requires a valid stop-signal path."
    }
    $guiJson = [Console]::In.ReadToEnd()
    if ([string]::IsNullOrWhiteSpace($guiJson)) {
        throw "The GUI launcher did not provide test settings. No GameNet process was started."
    }
    try { $guiSettings = $guiJson | ConvertFrom-Json -ErrorAction Stop }
    catch { throw "GUI test settings could not be read. No GameNet process was started." }
    finally { $guiJson = $null }

    if ($guiSettings.confirmedDisposableDatabase -ne $true -or
        [string]::IsNullOrWhiteSpace([string]$guiSettings.testedCheckoutSha) -or
        [string]::IsNullOrWhiteSpace([string]$guiSettings.connectionString) -or
        [string]::IsNullOrWhiteSpace([string]$guiSettings.ownerUsername) -or
        [string]::IsNullOrWhiteSpace([string]$guiSettings.ownerDisplayName) -or
        [string]::IsNullOrEmpty([string]$guiSettings.ownerPassword) -or
        $null -eq $guiSettings.hourlyRateToman) {
        throw "GUI test settings are incomplete or the disposable-database confirmation is missing."
    }
}

if (-not $IsWindows) {
    throw "Slice 1 process acceptance is supported only on Windows."
}

$bundleRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$serverExe = Join-Path $bundleRoot "Server\GameNet.Server.exe"
$agentExe = Join-Path $bundleRoot "Agent\GameNet.Agent.exe"
$desktopExe = Join-Path $bundleRoot "Desktop\GameNet.Manager.Desktop.exe"
$manifestPath = Join-Path $bundleRoot "manifest.json"

foreach ($path in @($serverExe, $agentExe, $desktopExe, $manifestPath)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "The process-acceptance bundle is incomplete. Missing required file: $path"
    }
}

$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if ($manifest.artifactType -ne "temporary-runtime-lan-smoke-payload" -or
    $manifest.isInstaller -ne $false -or
    $manifest.productionCertified -ne $false -or
    [string]$manifest.source.testedCheckoutSha -notmatch '^[0-9a-f]{40}$') {
    throw "Manifest identity/type is invalid. Refusing to start any process."
}

$manifestEntries = @($manifest.files)
if ($manifestEntries.Count -eq 0) {
    throw "Manifest contains no file hashes. Refusing to start any process."
}

$manifestPaths = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($entry in $manifestEntries) {
    $manifestRelative = ([string]$entry.path).Replace("\", "/")
    if ([string]::IsNullOrWhiteSpace($manifestRelative) -or
        $manifestRelative -match '(^/|^[A-Za-z]:|(^|/)\.\.?(/|$)|//|:)' -or
        [string]$entry.sha256 -notmatch '^[0-9a-fA-F]{64}$') {
        throw "Manifest contains an invalid relative path or SHA-256 value. Refusing to start any process."
    }
    if (-not $manifestPaths.Add($manifestRelative)) {
        throw "Manifest contains a duplicate file path. Refusing to start any process."
    }

    $relative = $manifestRelative.Replace("/", [System.IO.Path]::DirectorySeparatorChar)
    $file = [System.IO.Path]::GetFullPath((Join-Path $bundleRoot $relative))
    if (-not $file.StartsWith($bundleRoot + [System.IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or
        -not (Test-Path -LiteralPath $file -PathType Leaf)) {
        throw "Manifest path is invalid or missing. Refusing to start any process."
    }
    $actual = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actual -ne ([string]$entry.sha256).ToLowerInvariant()) {
        throw "Payload file hash mismatch at '$manifestRelative'. Refusing to start any process."
    }
}

$requiredManifestPaths = @(
    "Server/GameNet.Server.exe",
    "Server/Npgsql.dll",
    "Agent/GameNet.Agent.exe",
    "Desktop/GameNet.Manager.Desktop.exe",
    "Tools/inspect-manager-runtime-state.ps1",
    "Tools/run-slice1-process-acceptance.ps1",
    "Tools/slice1-process-acceptance.md",
    "Tools/manual-cash-sale-reversal-acceptance.md",
    "Tools/manual-session-timing-recovery-acceptance.md"
)
foreach ($requiredPath in $requiredManifestPaths) {
    if (-not $manifestPaths.Contains($requiredPath)) {
        throw "Required runtime/test file is not covered by manifest SHA-256: '$requiredPath'. Refusing to start any process."
    }
}

$managedPayloadRoots = @("Server", "Agent", "Desktop", "Tools", "Setup", "Launcher")
$actualPackagedFiles = Get-ChildItem -LiteralPath $bundleRoot -Recurse -File | Where-Object {
    $relativeForScope = [System.IO.Path]::GetRelativePath($bundleRoot, $_.FullName)
    $firstSegment = ($relativeForScope -split '[\\/]')[0]
    $managedPayloadRoots -contains $firstSegment
}
foreach ($actualFile in $actualPackagedFiles) {
    if ([string]::Equals($actualFile.FullName, $manifestPath, [StringComparison]::OrdinalIgnoreCase)) {
        continue
    }
    $actualRelative = [System.IO.Path]::GetRelativePath($bundleRoot, $actualFile.FullName).Replace("\", "/")
    if (-not $manifestPaths.Contains($actualRelative)) {
        throw "Unmanifested file found in the runtime/test payload: '$actualRelative'. Refusing to start any process."
    }
}


if ($GuiMode) {
    $shaConfirmation = ([string]$guiSettings.testedCheckoutSha).Trim()
}
else {
    $shaConfirmation = (Read-Host "Type the exact tested checkout SHA from manifest.json to confirm this candidate").Trim()
}
if (-not [string]::Equals($shaConfirmation, [string]$manifest.source.testedCheckoutSha, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Source SHA confirmation did not match manifest.json. No GameNet process or database migration was started."
}


function Convert-SecureStringToPlainText {
    param([Parameter(Mandatory)][Security.SecureString]$Value)
    $pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($Value)
    try {
        return [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer)
    }
    finally {
        [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer)
    }
}

function Find-Psql {
    $command = Get-Command "psql.exe" -ErrorAction SilentlyContinue
    if ($null -ne $command) {
        return $command.Source
    }

    $root = Join-Path $env:ProgramFiles "PostgreSQL"
    if (Test-Path -LiteralPath $root -PathType Container) {
        $candidates = Get-ChildItem -LiteralPath $root -Directory -ErrorAction Stop | Sort-Object Name -Descending
        foreach ($candidate in $candidates) {
            $path = Join-Path $candidate.FullName "bin\psql.exe"
            if (Test-Path -LiteralPath $path -PathType Leaf) {
                return $path
            }
        }
    }
    throw "psql.exe was not found. Install/use PostgreSQL command-line tools before acceptance; database isolation must be verified, not assumed."
}

function Assert-DisposableEmptyDatabase {
    param([Parameter(Mandatory)][string]$ConnectionString)

    $npgsqlAssemblyPath = Join-Path (Split-Path -Parent $serverExe) "Npgsql.dll"
    if (-not (Test-Path -LiteralPath $npgsqlAssemblyPath -PathType Leaf)) {
        throw "The published PostgreSQL driver is missing from the Server payload. No database command was run."
    }

    try {
        $npgsqlAssembly = [System.Reflection.Assembly]::LoadFrom($npgsqlAssemblyPath)
        $builderType = $npgsqlAssembly.GetType("Npgsql.NpgsqlConnectionStringBuilder", $true)
        $connectionStringConstructor = $builderType.GetConstructor([System.Type[]]@([string]))
        if ($null -eq $connectionStringConstructor) {
            throw "The bundled PostgreSQL driver does not expose its connection-string constructor."
        }
        # Invoke the real .NET constructor. PowerShell's dictionary adapter can
        # otherwise route ".ConnectionString =" to the IDictionary indexer as a
        # literal "ConnectionString" key instead of calling the CLR property setter.
        $builder = $connectionStringConstructor.Invoke([object[]]@($ConnectionString))
    }
    catch {
        throw "The supplied PostgreSQL connection string could not be parsed by the bundled PostgreSQL driver. No database command was run."
    }

    # Read values through CLR reflection to bypass PowerShell's dictionary adapter.
    # This exact access path passed the earlier payload-build parser gate.
    try {
        $hostName = [string]$builderType.GetProperty("Host").GetValue($builder)
        $database = [string]$builderType.GetProperty("Database").GetValue($builder)
        $username = [string]$builderType.GetProperty("Username").GetValue($builder)
        $password = [string]$builderType.GetProperty("Password").GetValue($builder)
        $port = [int]$builderType.GetProperty("Port").GetValue($builder)
        $sslMode = [string]$builderType.GetProperty("SslMode").GetValue($builder)
    }
    catch {
        throw "The bundled PostgreSQL driver parsed the connection string but its required values could not be read safely. No database command was run."
    }

    if ($port -lt 1 -or $port -gt 65535) {
        throw "PostgreSQL port is invalid. No database command was run."
    }

    if ($hostName -notin @("127.0.0.1", "localhost", "::1")) {
        throw "Only a PostgreSQL instance on this same PC (localhost/loopback) is allowed for early process-only acceptance."
    }
    if ($database -notmatch '^[A-Za-z0-9_.-]{3,63}$' -or
        $database -notmatch '(?i)(test|sandbox|disposable|slice.?1|acceptance)' -or
        $database -match '(?i)(production|prod|live|business|customer|gamenet.?manager)') {
        throw "Database name must clearly identify a disposable test database and must not identify business/production data."
    }
    if ([string]::IsNullOrWhiteSpace($username) -or
        $username -match '(?i)(^postgres$|superuser|administrator|admin|^root$)') {
        throw "Use a dedicated, non-administrator PostgreSQL test role, not postgres/root/admin."
    }
    if ([string]::IsNullOrEmpty($password)) {
        throw "A dedicated test role with a password is required. Do not use implicit/admin authentication for this test."
    }

    $psql = Find-Psql
    $query = @"
SELECT
  (SELECT count(*)
     FROM pg_catalog.pg_class c
     JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace
    WHERE n.nspname NOT IN ('pg_catalog', 'information_schema')
      AND n.nspname NOT LIKE 'pg_toast%'
      AND n.nspname NOT LIKE 'pg_temp_%'
      AND n.nspname NOT LIKE 'pg_toast_temp_%'
      AND c.relkind IN ('r', 'p', 'v', 'm', 'f', 'S')),
  (
    (SELECT count(*)
       FROM pg_catalog.pg_namespace n
      WHERE n.nspname NOT IN ('pg_catalog', 'information_schema', 'public')
        AND n.nspname NOT LIKE 'pg_toast%'
        AND n.nspname NOT LIKE 'pg_temp_%'
        AND n.nspname NOT LIKE 'pg_toast_temp_%')
    + (SELECT count(*) FROM pg_catalog.pg_proc p JOIN pg_catalog.pg_namespace n ON n.oid = p.pronamespace WHERE n.nspname NOT IN ('pg_catalog', 'information_schema') AND n.nspname NOT LIKE 'pg_toast%' AND n.nspname NOT LIKE 'pg_temp_%' AND n.nspname NOT LIKE 'pg_toast_temp_%')
    + (SELECT count(*) FROM pg_catalog.pg_type t JOIN pg_catalog.pg_namespace n ON n.oid = t.typnamespace WHERE n.nspname NOT IN ('pg_catalog', 'information_schema') AND n.nspname NOT LIKE 'pg_toast%' AND n.nspname NOT LIKE 'pg_temp_%' AND n.nspname NOT LIKE 'pg_toast_temp_%')
    + (SELECT count(*) FROM pg_catalog.pg_extension e JOIN pg_catalog.pg_namespace n ON n.oid = e.extnamespace WHERE n.nspname NOT IN ('pg_catalog', 'information_schema') AND n.nspname NOT LIKE 'pg_toast%' AND n.nspname NOT LIKE 'pg_temp_%' AND n.nspname NOT LIKE 'pg_toast_temp_%')
    + (SELECT count(*) FROM pg_catalog.pg_operator o JOIN pg_catalog.pg_namespace n ON n.oid = o.oprnamespace WHERE n.nspname NOT IN ('pg_catalog', 'information_schema') AND n.nspname NOT LIKE 'pg_toast%' AND n.nspname NOT LIKE 'pg_temp_%' AND n.nspname NOT LIKE 'pg_toast_temp_%')
    + (SELECT count(*) FROM pg_catalog.pg_opclass o JOIN pg_catalog.pg_namespace n ON n.oid = o.opcnamespace WHERE n.nspname NOT IN ('pg_catalog', 'information_schema') AND n.nspname NOT LIKE 'pg_toast%' AND n.nspname NOT LIKE 'pg_temp_%' AND n.nspname NOT LIKE 'pg_toast_temp_%')
    + (SELECT count(*) FROM pg_catalog.pg_opfamily o JOIN pg_catalog.pg_namespace n ON n.oid = o.opfnamespace WHERE n.nspname NOT IN ('pg_catalog', 'information_schema') AND n.nspname NOT LIKE 'pg_toast%' AND n.nspname NOT LIKE 'pg_temp_%' AND n.nspname NOT LIKE 'pg_toast_temp_%')
    + (SELECT count(*) FROM pg_catalog.pg_conversion o JOIN pg_catalog.pg_namespace n ON n.oid = o.connamespace WHERE n.nspname NOT IN ('pg_catalog', 'information_schema') AND n.nspname NOT LIKE 'pg_toast%' AND n.nspname NOT LIKE 'pg_temp_%' AND n.nspname NOT LIKE 'pg_toast_temp_%')
    + (SELECT count(*) FROM pg_catalog.pg_collation o JOIN pg_catalog.pg_namespace n ON n.oid = o.collnamespace WHERE n.nspname NOT IN ('pg_catalog', 'information_schema') AND n.nspname NOT LIKE 'pg_toast%' AND n.nspname NOT LIKE 'pg_temp_%' AND n.nspname NOT LIKE 'pg_toast_temp_%')
    + (SELECT count(*) FROM pg_catalog.pg_ts_config o JOIN pg_catalog.pg_namespace n ON n.oid = o.cfgnamespace WHERE n.nspname NOT IN ('pg_catalog', 'information_schema') AND n.nspname NOT LIKE 'pg_toast%' AND n.nspname NOT LIKE 'pg_temp_%' AND n.nspname NOT LIKE 'pg_toast_temp_%')
    + (SELECT count(*) FROM pg_catalog.pg_ts_dict o JOIN pg_catalog.pg_namespace n ON n.oid = o.dictnamespace WHERE n.nspname NOT IN ('pg_catalog', 'information_schema') AND n.nspname NOT LIKE 'pg_toast%' AND n.nspname NOT LIKE 'pg_temp_%' AND n.nspname NOT LIKE 'pg_toast_temp_%')
    + (SELECT count(*) FROM pg_catalog.pg_ts_parser o JOIN pg_catalog.pg_namespace n ON n.oid = o.prsnamespace WHERE n.nspname NOT IN ('pg_catalog', 'information_schema') AND n.nspname NOT LIKE 'pg_toast%' AND n.nspname NOT LIKE 'pg_temp_%' AND n.nspname NOT LIKE 'pg_toast_temp_%')
    + (SELECT count(*) FROM pg_catalog.pg_ts_template o JOIN pg_catalog.pg_namespace n ON n.oid = o.tmplnamespace WHERE n.nspname NOT IN ('pg_catalog', 'information_schema') AND n.nspname NOT LIKE 'pg_toast%' AND n.nspname NOT LIKE 'pg_temp_%' AND n.nspname NOT LIKE 'pg_toast_temp_%')
    + (SELECT count(*) FROM pg_catalog.pg_foreign_data_wrapper)
    + (SELECT count(*) FROM pg_catalog.pg_foreign_server)
    + (SELECT count(*) FROM pg_catalog.pg_default_acl)
    + (SELECT count(*) FROM pg_catalog.pg_largeobject_metadata)
    + (SELECT count(*) FROM pg_catalog.pg_publication)
    + (SELECT count(*) FROM pg_catalog.pg_subscription)
    + (SELECT count(*) FROM pg_catalog.pg_event_trigger)
    + (SELECT count(*) FROM pg_catalog.pg_language WHERE lanname NOT IN ('internal', 'c', 'sql', 'plpgsql'))
  ),
  (SELECT CASE
      WHEN rolsuper OR rolcreaterole OR rolcreatedb OR rolreplication OR rolbypassrls
        THEN 'ELEVATED'
      ELSE 'OK'
    END
     FROM pg_catalog.pg_roles
    WHERE rolname = current_user);
"@

    $info = [System.Diagnostics.ProcessStartInfo]::new()
    $info.FileName = $psql
    $info.UseShellExecute = $false
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    # Remove inherited libpq settings before the explicitly loopback-only preflight.
    # PGHOSTADDR can override the host passed through -h, so scrub every PG* variable.
    foreach ($key in @($info.Environment.Keys)) {
        if ([string]$key -like 'PG*') {
            [void]$info.Environment.Remove([string]$key)
        }
    }
    foreach ($arg in @("-X", "-w", "-A", "-t", "-h", $hostName, "-p", [string]$port, "-U", $username, "-d", $database, "-c", $query)) {
        $info.ArgumentList.Add($arg)
    }
    $info.Environment["PGPASSWORD"] = $password
    $sslMap = @{ Disable = "disable"; Allow = "allow"; Prefer = "prefer"; Require = "require"; VerifyCA = "verify-ca"; VerifyFull = "verify-full" }
    if (-not $sslMap.ContainsKey($sslMode)) {
        throw "Unsupported PostgreSQL SSL mode for the psql isolation preflight. No migration was started."
    }
    $info.Environment["PGSSLMODE"] = $sslMap[$sslMode]

    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $info
    try {
        if (-not $process.Start()) { throw "Could not start psql preflight." }
        $stdoutTask = $process.StandardOutput.ReadToEndAsync()
        $stderrTask = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit(20000)) {
            try { $process.Kill($true) } catch { }
            throw "PostgreSQL isolation preflight timed out. No GameNet migration was started."
        }
        $stdout = $stdoutTask.GetAwaiter().GetResult().Trim()
        $stderr = $stderrTask.GetAwaiter().GetResult()
        if ($process.ExitCode -ne 0) {
            throw "PostgreSQL isolation preflight failed (exit $($process.ExitCode)). Verify the local test role/database and retry. Details: $($stderr.Trim())"
        }
        if ($stdout -notmatch '^\s*0\|0\|OK\s*$') {
            throw "Refusing to migrate: the database has application relations/objects or the PostgreSQL role has elevated privileges. Expected relation-count|other-object-count|OK = 0|0|OK; observed '$stdout'."
        }
    }
    finally {
        # Dictionary.Remove returns a Boolean. Suppress it so the function emits
        # only the intended database-info object to its caller.
        [void]$info.Environment.Remove("PGPASSWORD")
        $process.Dispose()
        $builder.Clear()
        $password = $null
    }

    return [pscustomobject]@{
        Host = $hostName
        Port = $port
        Database = $database
        Username = $username
    }
}

function New-RuntimeProcessInfo {
    param(
        [Parameter(Mandatory)][string]$FilePath,
        [string[]]$Arguments = @(),
        [Parameter(Mandatory)][string]$ConfigurationDirectory,
        [switch]$ForServer,
        [string]$ConnectionString,
        [string]$SigningKey,
        [string]$AgentProvisioningKey,
        [string]$FingerprintKey,
        [string]$EnrollmentToken
    )

    $info = [System.Diagnostics.ProcessStartInfo]::new()
    $info.FileName = $FilePath
    $info.WorkingDirectory = Split-Path -Parent $FilePath
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $false
    foreach ($arg in $Arguments) { $info.ArgumentList.Add($arg) }

    foreach ($key in @($info.Environment.Keys)) {
        if ($key -match '^(GAMENET_.*|GameNet.*|DOTNET_GameNet.*|ASPNETCORE_GameNet.*|Kestrel.*|DOTNET_Kestrel.*|ASPNETCORE_Kestrel.*|ASPNETCORE_URLS|DOTNET_URLS|ASPNETCORE_HTTP_PORTS|ASPNETCORE_HTTPS_PORTS|DOTNET_HTTP_PORTS|DOTNET_HTTPS_PORTS|DOTNET_STARTUP_HOOKS|ASPNETCORE_HOSTINGSTARTUPASSEMBLIES|ConnectionStrings__.*|DOTNET_ConnectionStrings.*|ASPNETCORE_ConnectionStrings.*|PG[A-Z0-9_]*)$' -or
            $key -ieq "ASPNETCORE_ENVIRONMENT" -or
            $key -ieq "DOTNET_ENVIRONMENT") {
            [void]$info.Environment.Remove([string]$key)
        }
    }

    $info.Environment["ASPNETCORE_ENVIRONMENT"] = "Development"
    $info.Environment["DOTNET_ENVIRONMENT"] = "Development"
    $info.Environment["GAMENET_TEST_RUNTIME_CONFIG_DIRECTORY"] = $ConfigurationDirectory
    if ($ForServer) {
        $info.Environment["GAMENET_ALLOW_UNPROTECTED_TEST_SECRETS"] = "true"
        $info.Environment["GAMENET_DATABASE_CONNECTION"] = $ConnectionString
        $info.Environment["GameNet__Authentication__SigningKey"] = $SigningKey
        $info.Environment["GameNet__Agent__ProvisioningKey"] = $AgentProvisioningKey
        $info.Environment["GameNet__Idempotency__FingerprintKey"] = $FingerprintKey
    }
    if (-not [string]::IsNullOrWhiteSpace($EnrollmentToken)) {
        $info.Environment["GAMENET_AGENT_ENROLLMENT_TOKEN"] = $EnrollmentToken
    }
    return $info
}

function Start-GameNetProcess {
    param([Parameter(Mandatory)][System.Diagnostics.ProcessStartInfo]$StartInfo)
    try {
        $process = [System.Diagnostics.Process]::Start($StartInfo)
        if ($null -eq $process) { throw "Windows did not create a GameNet test process." }
        return $process
    }
    finally {
        # Process.Start has copied the environment block. Clear secret values from
        # the parent-side ProcessStartInfo so they do not linger in script objects.
        foreach ($key in @($StartInfo.Environment.Keys)) {
            if ($key -match '^(GAMENET_.*|GameNet.*)$') {
                [void]$StartInfo.Environment.Remove([string]$key)
            }
        }
    }
}

function Stop-GameNetProcess {
    param([System.Diagnostics.Process]$Process, [switch]$CloseWindowFirst)
    if ($null -eq $Process) { return }
    try {
        if (-not $Process.HasExited) {
            if ($CloseWindowFirst) {
                try { [void]$Process.CloseMainWindow() } catch { }
                if (-not $Process.WaitForExit(5000)) {
                    $Process.Kill($true)
                    [void]$Process.WaitForExit(10000)
                }
            }
            else {
                $Process.Kill($true)
                [void]$Process.WaitForExit(10000)
            }
        }
    }
    catch {
        Write-Warning "A test process could not be confirmed stopped. Inspect Task Manager before another run."
    }
    finally {
        $Process.Dispose()
    }
}

function Get-FreeLoopbackPort {
    $listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, 0)
    try {
        $listener.Start()
        return [int]$listener.LocalEndpoint.Port
    }
    finally { $listener.Stop() }
}

function Wait-GameNetServer {
    param([Parameter(Mandatory)][string]$BaseUrl, [Parameter(Mandatory)][System.Diagnostics.Process]$Process)
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(60)
    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        if ($Process.HasExited) {
            throw "The local GameNet Server exited during startup. Review its console error; no external endpoint is used."
        }
        try {
            $health = Invoke-RestMethod -Method Get -Uri "$BaseUrl/health" -TimeoutSec 2
            if ([string]::Equals([string]$health.data.readiness, "Ready", [StringComparison]::Ordinal)) {
                return
            }
        }
        catch { }
        Start-Sleep -Milliseconds 750
    }
    throw "The local GameNet Server did not become ready on its assigned loopback endpoint."
}

function Invoke-GameNetApi {
    param(
        [Parameter(Mandatory)][ValidateSet("GET", "POST", "PUT")][string]$Method,
        [Parameter(Mandatory)][string]$BaseUrl,
        [Parameter(Mandatory)][string]$Path,
        [string]$Token,
        [string]$BootstrapSecret,
        [string]$IdempotencyKey,
        [object]$Body
    )
    $headers = @{ "X-GameNet-Contract" = "v1" }
    if (-not [string]::IsNullOrWhiteSpace($Token)) { $headers["Authorization"] = "Bearer $Token" }
    if (-not [string]::IsNullOrWhiteSpace($BootstrapSecret)) { $headers["X-GameNet-Bootstrap-Secret"] = $BootstrapSecret }
    if (-not [string]::IsNullOrWhiteSpace($IdempotencyKey)) { $headers["Idempotency-Key"] = $IdempotencyKey }
    $parameters = @{ Method = $Method; Uri = "$BaseUrl$Path"; Headers = $headers; TimeoutSec = 15; ErrorAction = "Stop" }
    if ($null -ne $Body) {
        $parameters["ContentType"] = "application/json"
        $parameters["Body"] = ConvertTo-Json -InputObject $Body -Depth 12 -Compress
    }
    return Invoke-RestMethod @parameters
}

function Write-JsonFile {
    param([Parameter(Mandatory)][string]$Path, [Parameter(Mandatory)][object]$Value)
    [System.IO.File]::WriteAllText($Path, (ConvertTo-Json -InputObject $Value -Depth 12) + [Environment]::NewLine, [System.Text.UTF8Encoding]::new($false))
}

$serverProcess = $null
$agentProcess = $null
$desktopProcess = $null
$migrationProcess = $null
$testRoot = $null
$connectionString = $null
$ownerPassword = $null
$ownerPasswordConfirm = $null
$secureOwnerPassword = $null
$secureOwnerPasswordAgain = $null
$bootstrapSecret = $null
$signingKey = $null
$provisioningKey = $null
$fingerprintKey = $null
$agentToken = $null
$accessToken = $null
$bootstrapBody = $null
$login = $null
$loginPassword = $null
$serverInfo = $null

try {
    Write-Host ""
    Write-Host "GameNet 5 - Slice 1 process-based acceptance"
    if ($GuiMode) {
        Write-Host "Started by the test-only Windows GUI Setup. This is not a production release."
        Write-Host "Only temporary Server/Agent/Desktop processes are started on loopback."
    }
    else {
        Write-Host "Internal developer harness. This is NOT Setup/MSI and NOT a production install."
        Write-Host "It runs temporary Server/Agent/Desktop processes on this same PC."
    }
    Write-Host "No Windows services or machine-wide ProgramData, certificates, firewall, registry, clock, or LAN settings are changed."
    Write-Host ""


    $services = @(Get-CimInstance -ClassName Win32_Service -ErrorAction Stop |
        Where-Object { $_.Name -in @("GameNet 5 Server", "GameNet 5 Agent") -or $_.DisplayName -in @("GameNet 5 Server", "GameNet 5 Agent") })
    if ($services.Count -gt 0) {
        throw "A GameNet 5 Windows service already exists on this PC. This process-only workflow is not for an installed-service machine."
    }
    $programDataRoot = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::CommonApplicationData)) "GameNet Manager"
    $markers = @(
        (Join-Path $programDataRoot "Config\server.json"),
        (Join-Path $programDataRoot "Config\server-secrets.bin"),
        (Join-Path $programDataRoot "Secrets\server-secrets.v1.dpapi")
    )
    if (@($markers | Where-Object { Test-Path -LiteralPath $_ }).Count -gt 0) {
        throw "GameNet 5 Manager Server state already exists under ProgramData. This first-time process acceptance refuses to overlap the new app's existing runtime state; a separate legacy GameNet program is not what this guard targets."
    }

    if ($GuiMode) {
        $connectionString = [string]$guiSettings.connectionString
    }
    else {
        $secureConnection = Read-Host "Paste the connection string for a NEW empty local PostgreSQL test database (input is hidden)" -AsSecureString
        try { $connectionString = Convert-SecureStringToPlainText $secureConnection }
        finally { $secureConnection.Dispose() }
    }


    $databaseInfoItems = @(Assert-DisposableEmptyDatabase -ConnectionString $connectionString)
    if ($databaseInfoItems.Count -ne 1 -or
        $databaseInfoItems[0] -isnot [System.Management.Automation.PSCustomObject]) {
        throw "PostgreSQL preflight returned unexpected pipeline output. No GameNet process or migration was started."
    }
    $databaseInfo = $databaseInfoItems[0]
    foreach ($requiredInfoProperty in @("Database", "Host", "Port", "Username")) {
        if ($null -eq $databaseInfo.PSObject.Properties[$requiredInfoProperty]) {
            throw "PostgreSQL preflight omitted required result property '$requiredInfoProperty'. No GameNet process or migration was started."
        }
    }
    Write-Host ("Verified empty loopback database: {0} on {1}:{2}, role {3}" -f $databaseInfo.Database, $databaseInfo.Host, $databaseInfo.Port, $databaseInfo.Username)
    Write-Host "Database preflight: zero non-system relations and zero additional non-system database objects; role has no elevated PostgreSQL privileges."
    if ($GuiMode) {
        if ($guiSettings.confirmedDisposableDatabase -ne $true) {
            throw "The GUI did not explicitly confirm a new disposable test database. No migration was started."
        }
    }
    else {
        $confirmation = Read-Host "Type exactly USE EMPTY TEST DATABASE to confirm migration of this verified disposable database"
        if ($confirmation -cne "USE EMPTY TEST DATABASE") {
            throw "Confirmation did not match; no GameNet process or migration was started."
        }
    }


    $testRoot = Join-Path $env:TEMP ("GameNet5-Slice1-" + [DateTime]::UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + [Guid]::NewGuid().ToString("N"))
    $configDirectory = Join-Path $testRoot "Config"
    $agentStateRoot = Join-Path $testRoot "AgentState"
    New-Item -ItemType Directory -Force -Path $configDirectory, $agentStateRoot | Out-Null

    $currentPrincipal = [Security.Principal.WindowsIdentity]::GetCurrent().Name
    $grantCurrent = "{0}:(OI)(CI)F" -f $currentPrincipal
    $icacls = Join-Path $env:WINDIR "System32\icacls.exe"
    & $icacls $testRoot "/inheritance:r" "/grant:r" $grantCurrent "*S-1-5-18:(OI)(CI)F" "*S-1-5-32-544:(OI)(CI)F" | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "Could not restrict the temporary test directory ACL. No GameNet process was started."
    }

    $port = Get-FreeLoopbackPort
    $baseUrl = "http://127.0.0.1:$port"
    $bootstrapSecret = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(48))
    $signingKey = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(48))
    $provisioningKey = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(48))
    $fingerprintKey = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(48))

    $serverSettings = [ordered]@{
        urls = $baseUrl
        GameNet = [ordered]@{
            BusinessTimeZone = "Asia/Tehran"
            Currency = "TOM"
            Authentication = [ordered]@{
                Enabled = $true
                Issuer = "GameNet5.Slice1.Local"
                Audience = "GameNet5.Desktop"
            }
            Agent = [ordered]@{
                LeaseDurationSeconds = 30
                HeartbeatIntervalSeconds = 5
                AccessTokenLifetimeSeconds = 300
            }
            Setup = [ordered]@{ BootstrapSecret = $bootstrapSecret }
        }
        # Keep functional-acceptance console output readable. Warnings and errors
        # remain visible; per-query SQL and routine HTTP diagnostics otherwise
        # obscure the interactive Read-Host prompts.
        Logging = [ordered]@{
            LogLevel = [ordered]@{
                Default = "Warning"
                "Microsoft.Hosting.Lifetime" = "Information"
                "Microsoft.EntityFrameworkCore.Database.Command" = "Warning"
                "Microsoft.AspNetCore.Hosting.Diagnostics" = "Warning"
            }
        }
    }
    Write-JsonFile -Path (Join-Path $configDirectory "server.json") -Value $serverSettings

    Write-Host "Applying migrations only to the verified empty test database..."
    $migrationInfo = New-RuntimeProcessInfo -FilePath $serverExe -Arguments @("--migrate-only") -ConfigurationDirectory $configDirectory -ForServer -ConnectionString $connectionString -SigningKey $signingKey -AgentProvisioningKey $provisioningKey -FingerprintKey $fingerprintKey
    $migrationProcess = Start-GameNetProcess -StartInfo $migrationInfo
    if (-not $migrationProcess.WaitForExit(180000)) {
        try { $migrationProcess.Kill($true) } catch { }
        throw "Database migration timed out. Check the test database and Server output before retrying."
    }
    $migrationExitCode = $migrationProcess.ExitCode
    $migrationProcess.Dispose()
    $migrationProcess = $null
    if ($migrationExitCode -ne 0) {
        throw "GameNet schema migration failed with exit code $migrationExitCode. No other test process was started."
    }

    $serverInfo = New-RuntimeProcessInfo -FilePath $serverExe -ConfigurationDirectory $configDirectory -ForServer -ConnectionString $connectionString -SigningKey $signingKey -AgentProvisioningKey $provisioningKey -FingerprintKey $fingerprintKey
    $serverProcess = Start-GameNetProcess -StartInfo $serverInfo
    Wait-GameNetServer -BaseUrl $baseUrl -Process $serverProcess

    $bootstrapStatus = Invoke-GameNetApi -Method GET -BaseUrl $baseUrl -Path "/api/v1/bootstrap/status"
    if (-not $bootstrapStatus.data.required) {
        throw "Fresh-database Owner bootstrap was unexpectedly already completed; stop and investigate before proceeding."
    }

    if ($GuiMode) {
        $ownerUsername = ([string]$guiSettings.ownerUsername).Trim()
        $ownerDisplayName = ([string]$guiSettings.ownerDisplayName).Trim()
        $ownerPassword = [string]$guiSettings.ownerPassword
        $secureOwnerPassword = ConvertTo-SecureString -String $ownerPassword -AsPlainText -Force
        $ownerPasswordConfirm = $ownerPassword
        $secureOwnerPasswordAgain = $null
    }
    else {
        $ownerUsername = (Read-Host "Choose a throwaway Owner username for this test database").Trim()
        $ownerDisplayName = (Read-Host "Choose a test-only Owner display name").Trim()
    }
    if ($ownerUsername.Length -lt 3 -or $ownerUsername.Length -gt 64 -or $ownerDisplayName.Length -lt 1 -or $ownerDisplayName.Length -gt 120) {
        throw "Owner username/display name is outside the supported test input length."
    }
    if (-not $GuiMode) {
        $secureOwnerPassword = Read-Host "Choose a throwaway Owner password (10-256 characters; hidden)" -AsSecureString
        $secureOwnerPasswordAgain = Read-Host "Repeat the throwaway Owner password (hidden)" -AsSecureString
        try {
            $ownerPassword = Convert-SecureStringToPlainText $secureOwnerPassword
            $ownerPasswordConfirm = Convert-SecureStringToPlainText $secureOwnerPasswordAgain
        }
        finally {
            $secureOwnerPasswordAgain.Dispose()
            $secureOwnerPasswordAgain = $null
        }
    }
    if ($ownerPassword.Length -lt 10 -or $ownerPassword.Length -gt 256 -or
        -not [string]::Equals($ownerPassword, $ownerPasswordConfirm, [StringComparison]::Ordinal)) {
        throw "Owner password confirmation/length is invalid."
    }
    $ownerPasswordConfirm = $null

    try {
        $bootstrapBody = @{ username = $ownerUsername; displayName = $ownerDisplayName; password = $ownerPassword }
        $null = Invoke-GameNetApi -Method POST -BaseUrl $baseUrl -Path "/api/v1/bootstrap/admin" -BootstrapSecret $bootstrapSecret -Body $bootstrapBody
    }
    catch {
        throw "Initial Owner creation failed. Do not re-run on a different database; inspect the test Server output and the fresh database state."
    }
    if ($null -ne $bootstrapBody) { $bootstrapBody.password = $null }
    $bootstrapBody = $null
    $ownerPassword = $null
    $bootstrapSecret = $null

    Stop-GameNetProcess -Process $serverProcess
    $serverProcess = $null
    $serverSettings.GameNet.Remove("Setup")
    Write-JsonFile -Path (Join-Path $configDirectory "server.json") -Value $serverSettings

    $serverInfo = New-RuntimeProcessInfo -FilePath $serverExe -ConfigurationDirectory $configDirectory -ForServer -ConnectionString $connectionString -SigningKey $signingKey -AgentProvisioningKey $provisioningKey -FingerprintKey $fingerprintKey
    $serverProcess = Start-GameNetProcess -StartInfo $serverInfo
    $connectionString = $null
    $signingKey = $null
    $provisioningKey = $null
    $fingerprintKey = $null
    $serverInfo = $null
    Wait-GameNetServer -BaseUrl $baseUrl -Process $serverProcess

    # Reuse the same SecureString accepted and confirmed for bootstrap instead
    # of prompting the operator to type it a third time between noisy process logs.
    # If this login fails, the exact same password was used for both API calls.
    Write-Host "Owner bootstrap completed; verifying sign-in with the same captured test password..."
    try {
        $loginPassword = Convert-SecureStringToPlainText $secureOwnerPassword
        $login = Invoke-GameNetApi -Method POST -BaseUrl $baseUrl -Path "/api/v1/auth/login" -Body @{ username = $ownerUsername; password = $loginPassword }
    }
    catch {
        throw "Owner sign-in failed after bootstrap using the exact password accepted for bootstrap. Preserve the disposable database and test evidence; this indicates a bootstrap/login integration defect."
    }
    finally {
        $loginPassword = $null
        if ($null -ne $secureOwnerPassword) {
            $secureOwnerPassword.Dispose()
            $secureOwnerPassword = $null
        }
    }
    $accessToken = [string]$login.data.accessToken
    if ([string]::IsNullOrWhiteSpace($accessToken)) { throw "Server did not issue a local Owner access token." }

    if ($GuiMode) {
        $rateText = ([string]$guiSettings.hourlyRateToman).Trim()
    }
    else {
        $rateText = (Read-Host "Enter the fixed hourly tariff in whole Toman for this test only (no real payment)").Trim()
    }
    [long]$hourlyRate = 0
    if (-not [long]::TryParse($rateText, [Globalization.NumberStyles]::None, [Globalization.CultureInfo]::InvariantCulture, [ref]$hourlyRate) -or $hourlyRate -lt 1) {
        throw "Tariff must be a positive integer number of Toman."
    }

    $suffix = [Guid]::NewGuid().ToString("N").Substring(0, 10)
    $deviceId = "slice1-" + $suffix
    $stationCode = "TESTPC-" + $suffix.ToUpperInvariant()
    $tariffCode = "TESTPC-HOURLY-" + $suffix.ToUpperInvariant()

    $tariff = Invoke-GameNetApi -Method POST -BaseUrl $baseUrl -Path "/api/v1/tariffs" -Token $accessToken -IdempotencyKey ([Guid]::NewGuid().ToString("N")) -Body @{
        code = $tariffCode
        name = "Slice 1 disposable PC hourly"
        stationType = 1
        hourlyRateToman = $hourlyRate
        isDefault = $true
    }
    $station = Invoke-GameNetApi -Method POST -BaseUrl $baseUrl -Path "/api/v1/stations" -Token $accessToken -IdempotencyKey ([Guid]::NewGuid().ToString("N")) -Body @{
        code = $stationCode
        name = "Slice 1 test PC"
        type = 1
    }

    # Agent enrollment routes return the issue response directly (unlike the
    # standard ApiEnvelope responses used by Station/Tariff and Owner APIs).
    $enrollment = Invoke-GameNetApi -Method POST -BaseUrl $baseUrl -Path "/api/v1/agent/enrollment-tokens" -Token $accessToken -Body @{ deviceId = $deviceId }
    $agentToken = [string]$enrollment.token
    if ($agentToken -notmatch '^[A-Za-z0-9_-]{43}$') {
        throw "Server did not issue a valid one-time Agent enrollment token."
    }

    Write-JsonFile -Path (Join-Path $configDirectory "desktop.json") -Value @{
        GameNet = @{ Server = @{ BaseUrl = $baseUrl } }
    }
    Write-JsonFile -Path (Join-Path $configDirectory "agent.json") -Value @{
        GameNet = @{
            AgentIdentity = @{ DeviceId = $deviceId; RootPath = $agentStateRoot }
            AgentTransport = @{ ServerBaseUrl = $baseUrl; HeartbeatIntervalSeconds = 5; InitialRetrySeconds = 2 }
        }
    }

    $agentInfo = New-RuntimeProcessInfo -FilePath $agentExe -ConfigurationDirectory $configDirectory -EnrollmentToken $agentToken
    $agentProcess = Start-GameNetProcess -StartInfo $agentInfo
    $agentToken = $null
    $enrollment = $null

    $stationId = [string]$station.data.id
    $stationVersion = [int]$station.data.version
    $bound = $false
    $bindDeadline = [DateTimeOffset]::UtcNow.AddSeconds(45)
    while ([DateTimeOffset]::UtcNow -lt $bindDeadline) {
        if ($agentProcess.HasExited) { throw "The test Agent exited before enrollment/heartbeat completed. Review its console error." }
        try {
            $null = Invoke-GameNetApi -Method PUT -BaseUrl $baseUrl -Path "/api/v1/stations/$stationId/agent" -Token $accessToken -IdempotencyKey ([Guid]::NewGuid().ToString("N")) -Body @{
                deviceId = $deviceId
                expectedVersion = $stationVersion
            }
            $bound = $true
            break
        }
        catch {
            $statusCode = 0
            try { $statusCode = [int]$_.Exception.Response.StatusCode } catch { }
            if ($statusCode -ne 409) { throw "Station-to-Agent binding failed with HTTP $statusCode. Stop rather than weakening the Agent gate." }
            Start-Sleep -Seconds 2
        }
    }
    if (-not $bound) { throw "Agent did not become provisioned in 45 seconds. No fallback/override was used; review Agent and Server console output." }

    $online = $false
    $onlineDeadline = [DateTimeOffset]::UtcNow.AddSeconds(35)
    while ([DateTimeOffset]::UtcNow -lt $onlineDeadline) {
        if ($agentProcess.HasExited) { throw "The test Agent exited before its lease/heartbeat could be verified." }
        $stationList = Invoke-GameNetApi -Method GET -BaseUrl $baseUrl -Path "/api/v1/stations" -Token $accessToken
        $currentStation = @($stationList.data) | Where-Object { [string]$_.id -eq $stationId } | Select-Object -First 1
        if ($null -ne $currentStation -and $currentStation.agentOnline -eq $true) {
            $online = $true
            break
        }
        Start-Sleep -Seconds 2
    }
    if (-not $online) { throw "The Server did not report the test PC Agent online with a current lease. The UI will not be started." }
    $accessToken = $null
    $login = $null

    # Open the actual WPF application against the isolated loopback Server.
    $desktopInfo = New-RuntimeProcessInfo -FilePath $desktopExe -ConfigurationDirectory $configDirectory
    $desktopProcess = Start-GameNetProcess -StartInfo $desktopInfo
    $desktopInfo = $null
    Start-Sleep -Seconds 3
    if ($desktopProcess.HasExited) {
        throw "The WPF Desktop process exited during startup. Review its console/error output before proceeding with acceptance."
    }

    Write-Host ""
    Write-Host "Isolated test workspace is ready."
    Write-Host ("Source checkout SHA: {0}" -f $manifest.source.testedCheckoutSha)
    Write-Host ("Bundle type: {0} (not an installer)" -f $manifest.artifactType)
    Write-Host ("Server endpoint: {0} (loopback only)" -f $baseUrl)
    Write-Host ("Disposable DB: {0} on {1}:{2}" -f $databaseInfo.Database, $databaseInfo.Host, $databaseInfo.Port)
    Write-Host ("Test Station: {0}; hourly tariff: {1} Toman" -f $stationCode, $hourlyRate)
    Write-Host ("Isolated config/state directory: {0}" -f $testRoot)
    Write-Host "Owner password, DB connection string, Server keys and Agent enrollment token are not passed on command lines or persisted in Server config."
    Write-Host "Desktop will now open. Follow Tools\slice1-process-acceptance.md and use test amounts only."
    Write-Host "When evidence is saved, return here and press Enter to stop these test processes."
    if ($GuiMode) {
        Write-Host "WPF_ACCEPTANCE_READY=true"
        Write-Host "Use the launcher window's Stop button after collecting the screenshot and test evidence."
        while (-not (Test-Path -LiteralPath $StopSignalPath -PathType Leaf)) {
            Start-Sleep -Milliseconds 500
        }
    }
    else {
        $null = Read-Host "Press Enter after WPF acceptance is complete"
    }
}
finally {
    Stop-GameNetProcess -Process $desktopProcess -CloseWindowFirst
    Stop-GameNetProcess -Process $agentProcess
    Stop-GameNetProcess -Process $serverProcess
    if ($null -ne $migrationProcess) { Stop-GameNetProcess -Process $migrationProcess }

    $connectionString = $null
    $ownerPassword = $null
    $ownerPasswordConfirm = $null
    if ($null -ne $secureOwnerPassword) {
        $secureOwnerPassword.Dispose()
        $secureOwnerPassword = $null
    }
    if ($null -ne $secureOwnerPasswordAgain) {
        $secureOwnerPasswordAgain.Dispose()
        $secureOwnerPasswordAgain = $null
    }
    $bootstrapSecret = $null
    $signingKey = $null
    $provisioningKey = $null
    $fingerprintKey = $null
    $agentToken = $null
    $accessToken = $null
    $bootstrapBody = $null
    $login = $null
    $loginPassword = $null
    $serverInfo = $null

    if ($null -ne $testRoot -and (Test-Path -LiteralPath $testRoot -PathType Container)) {
        Write-Host ("Test evidence/config files were retained for review at: {0}" -f $testRoot)
        Write-Host "These files remain under this user's temporary directory, not ProgramData. Delete them only after saving test evidence and confirming all test processes have stopped."
    }
}