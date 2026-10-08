$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

if ([Environment]::GetEnvironmentVariable("GAMENET_FOUNDATION_REPAIR", "Process") -ne "1") {
    throw "This script is a one-time dedicated Foundation database repair and requires GAMENET_FOUNDATION_REPAIR=1."
}

$connection = [Environment]::GetEnvironmentVariable("GAMENET_DATABASE_CONNECTION", "Process")
if ([string]::IsNullOrWhiteSpace($connection)) {
    $connection = [Environment]::GetEnvironmentVariable("GAMENET_DATABASE_CONNECTION", "Machine")
}
if ([string]::IsNullOrWhiteSpace($connection)) {
    throw "GAMENET_DATABASE_CONNECTION is required."
}

$dotnetPath = Join-Path $env:ProgramFiles "dotnet\dotnet.exe"
if (-not (Test-Path -LiteralPath $dotnetPath -PathType Leaf)) {
    $dotnetPath = (Get-Command dotnet -ErrorAction Stop).Source
}

$assemblyPath = Join-Path (Get-Location) "src\Server\bin\Release\net10.0\Npgsql.dll"
if (-not (Test-Path -LiteralPath $assemblyPath -PathType Leaf)) {
    throw "Npgsql runtime assembly was not found at $assemblyPath."
}

$loaded = [AppDomain]::CurrentDomain.GetAssemblies() | Where-Object { $_.GetName().Name -eq "Npgsql" }
if (-not $loaded) {
    Add-Type -Path $assemblyPath
}

$builder = [Npgsql.NpgsqlConnectionStringBuilder]::new($connection)
$hostValue = if ([string]::IsNullOrWhiteSpace($builder.Host)) { "localhost" } else { $builder.Host }
$port = $builder.Port
$user = $builder.Username
$password = $builder.Password
$database = $builder.Database
if ([string]::IsNullOrWhiteSpace($database)) { $database = $user }

if ([string]::IsNullOrWhiteSpace($user)) {
    throw "Foundation repair requires a database username."
}
if ([string]::IsNullOrWhiteSpace($database)) {
    throw "Foundation repair requires a database name."
}

$oldPassword = [Environment]::GetEnvironmentVariable("PGPASSWORD", "Process")
$oldSslMode = [Environment]::GetEnvironmentVariable("PGSSLMODE", "Process")

try {
    if (-not [string]::IsNullOrWhiteSpace($password)) { $env:PGPASSWORD = $password }
    $env:PGSSLMODE = $builder.SslMode.ToString().ToLowerInvariant()

    $psql = Join-Path $env:ProgramFiles "PostgreSQL\17\bin\psql.exe"
    if (-not (Test-Path -LiteralPath $psql -PathType Leaf)) {
        $psql = (Get-Command psql -ErrorAction Stop).Source
    }

    $cli = @("--host=$hostValue","--port=$port","--username=$user","--dbname=$database")

    $probeSql = @"
SELECT
    current_database(),
    current_user,
    COALESCE((SELECT count(*) FROM public."__EFMigrationsHistory"), 0),
    (SELECT count(*)
       FROM pg_catalog.pg_tables
      WHERE schemaname = 'public'
        AND tablename IN (
            'audit_entries',
            'idempotency_records',
            'outbox_messages',
            'agent_credentials',
            'agent_connection_leases'
        ));
"@

    $probe = ((& $psql @cli "--set=ON_ERROR_STOP=1" "--tuples-only" "--no-align" "--command=$probeSql" 2>&1) -join "").Trim()
    if ($LASTEXITCODE -ne 0) { throw "Foundation database probe failed." }

    $parts = $probe.Split('|')
    if ($parts.Count -ne 4) {
        throw "Unexpected Foundation database probe result."
    }

    $currentDatabase = $parts[0].Trim()
    $historyCount = [int64]$parts[2].Trim()
    $requiredTableCount = [int64]$parts[3].Trim()

    if ($currentDatabase -ne "gamenet5_foundation") {
        throw "Repair safety check failed: current database is '$currentDatabase'."
    }

    if ($historyCount -le 0) {
        Write-Host "Foundation migration history is empty; no destructive repair is required."
        exit 0
    }

    if ($requiredTableCount -eq 5) {
        Write-Host "All required Foundation tables already exist; no repair is required."
        exit 0
    }

    Write-Host "Repairing inconsistent dedicated Foundation database schema..."
    $repairSql = @"
DROP SCHEMA IF EXISTS public CASCADE;
CREATE SCHEMA public;
GRANT USAGE, CREATE ON SCHEMA public TO PUBLIC;
"@

    & $psql @cli "--set=ON_ERROR_STOP=1" "--command=$repairSql"
    if ($LASTEXITCODE -ne 0) {
        throw "Could not rebuild the dedicated Foundation public schema."
    }

    & $dotnetPath @(
        "ef",
        "database", "update",
        "--configuration", "Release",
        "--no-build",
        "--project", "src/Server/GameNet.Server.csproj",
        "--startup-project", "src/Server/GameNet.Server.csproj",
        "--context", "GameNet.Server.Persistence.GameNetDbContext"
    )
    if ($LASTEXITCODE -ne 0) {
        throw "Could not reapply Foundation migrations after schema repair."
    }

    $verifySql = @"
SELECT count(*)
  FROM pg_catalog.pg_tables
 WHERE schemaname = 'public'
   AND tablename IN (
        'audit_entries',
        'idempotency_records',
        'outbox_messages',
        'agent_credentials',
        'agent_connection_leases'
   );
"@

    $verified = ((& $psql @cli "--set=ON_ERROR_STOP=1" "--tuples-only" "--no-align" "--command=$verifySql" 2>&1) -join "").Trim()
    if ($LASTEXITCODE -ne 0 -or [int64]$verified -ne 5) {
        throw "Foundation schema repair did not recreate all required tables."
    }

    Write-Host "DEDICATED FOUNDATION DATABASE REPAIRED AND RE-MIGRATED."
}
finally {
    if ($null -eq $oldPassword) { Remove-Item Env:PGPASSWORD -ErrorAction SilentlyContinue }
    else { $env:PGPASSWORD = $oldPassword }

    if ($null -eq $oldSslMode) { Remove-Item Env:PGSSLMODE -ErrorAction SilentlyContinue }
    else { $env:PGSSLMODE = $oldSslMode }
}
