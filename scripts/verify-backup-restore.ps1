$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$connection = [Environment]::GetEnvironmentVariable("GAMENET_DATABASE_CONNECTION", "Process")
if ([string]::IsNullOrWhiteSpace($connection)) {
    $connection = [Environment]::GetEnvironmentVariable("GAMENET_DATABASE_CONNECTION", "Machine")
}
if ([string]::IsNullOrWhiteSpace($connection)) {
    throw "GAMENET_DATABASE_CONNECTION is required for backup/restore certification."
}

function Resolve-Tool([string]$name) {
    $command = Get-Command $name -ErrorAction SilentlyContinue
    if ($command) { return $command.Source }

    $default = Join-Path $env:ProgramFiles "PostgreSQL\17\bin\$name.exe"
    if (Test-Path -LiteralPath $default -PathType Leaf) { return $default }

    throw "$name.exe was not found."
}

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

$pgDump = Resolve-Tool "pg_dump"
$pgRestore = Resolve-Tool "pg_restore"
$psql = Resolve-Tool "psql"

$builder = [System.Data.Common.DbConnectionStringBuilder]::new()
$builder.ConnectionString = $connection

$pgHost = Get-ConnectionValue $builder @("Host","Server","Address")
$port = Get-ConnectionValue $builder @("Port")
$username = Get-ConnectionValue $builder @("Username","User Id","User")
$password = Get-ConnectionValue $builder @("Password","Pwd")
$database = Get-ConnectionValue $builder @("Database","Initial Catalog")
$sslMode = Get-ConnectionValue $builder @("SSL Mode","SslMode")

if ([string]::IsNullOrWhiteSpace($username)) {
    throw "The PostgreSQL connection string must specify Username/User Id for CLI certification."
}
if ([string]::IsNullOrWhiteSpace($database)) {
    $database = $username
}
if ([string]::IsNullOrWhiteSpace($pgHost)) { $pgHost = "localhost" }
if ([string]::IsNullOrWhiteSpace($port)) { $port = "5432" }

$cliBase = @("--host=$pgHost","--port=$port","--username=$username")
if (-not [string]::IsNullOrWhiteSpace($sslMode)) {
    $cliBase += "--sslmode=$($sslMode.ToLowerInvariant())"
}

$oldPgPassword = [Environment]::GetEnvironmentVariable("PGPASSWORD","Process")
if (-not [string]::IsNullOrWhiteSpace($password)) {
    $env:PGPASSWORD = $password
}

$restoreDatabase = "gamenet5_restore_probe_" + [Guid]::NewGuid().ToString("N").Substring(0, 12)
$workRoot = Join-Path ([IO.Path]::GetTempPath()) ("gamenet5-backup-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Force -Path $workRoot | Out-Null
$dumpFile = Join-Path $workRoot "foundation.dump"

try {
    $sourceQuery = (& $psql @cliBase "--dbname=$database" "--set=ON_ERROR_STOP=1" "--tuples-only" "--no-align" "--command=SELECT current_database();" 2>&1)
    if ($LASTEXITCODE -ne 0) {
        throw "Could not determine the source PostgreSQL database."
    }

    $sourceDatabase = ($sourceQuery | Select-Object -Last 1).ToString().Trim()
    if ([string]::IsNullOrWhiteSpace($sourceDatabase)) {
        throw "PostgreSQL returned an empty current_database()."
    }
    if ($sourceDatabase -eq "postgres") {
        throw "Backup/restore certification must target the dedicated GameNet database, not the postgres maintenance database."
    }

    Write-Host "Creating isolated backup artifact..."
    & $pgDump @cliBase "--dbname=$sourceDatabase" "--format=custom" "--file=$dumpFile" "--no-owner" "--no-acl"
    if ($LASTEXITCODE -ne 0) { throw "pg_dump failed." }

    if (-not (Test-Path -LiteralPath $dumpFile -PathType Leaf)) {
        throw "Backup artifact was not created."
    }

    Write-Host "Creating isolated restore database..."
    $maintenanceArgs = $cliBase + @("--dbname=postgres")
    $createSql = 'CREATE DATABASE "' + $restoreDatabase + '";'
    & $psql @maintenanceArgs "--set=ON_ERROR_STOP=1" "--command=$createSql"
    if ($LASTEXITCODE -ne 0) { throw "Could not create isolated restore database." }

    Write-Host "Restoring backup artifact..."
    $restoreArgs = $cliBase + @("--dbname=$restoreDatabase")
    & $pgRestore @restoreArgs "--exit-on-error" "--no-owner" "--no-acl" $dumpFile
    if ($LASTEXITCODE -ne 0) { throw "pg_restore failed." }

    $check = @"
SELECT CASE WHEN bool_and(ok) THEN 'PASS' ELSE 'FAIL' END
FROM (
    VALUES
        (to_regclass('public.audit_entries') IS NOT NULL),
        (to_regclass('public.idempotency_records') IS NOT NULL),
        (to_regclass('public.outbox_messages') IS NOT NULL),
        (to_regclass('public.agent_credentials') IS NOT NULL),
        (to_regclass('public.agent_connection_leases') IS NOT NULL)
) AS required(ok);
"@

    $result = ((& $psql @restoreArgs "--set=ON_ERROR_STOP=1" "--tuples-only" "--no-align" "--command=$check" 2>&1) -join "").Trim()
    if ($LASTEXITCODE -ne 0) {
        throw "Restored database validation query failed: $result"
    }

    if ($result -ne "PASS") {
        throw "Restored database is missing one or more Foundation tables."
    }

    Write-Host "BACKUP/ISOLATED-RESTORE CERTIFICATION PASSED."
    Write-Host "Source database: $sourceDatabase"
    Write-Host "Isolated restore database: $restoreDatabase"
}
finally {
    try {
        $maintenanceArgs = $cliBase + @("--dbname=postgres")
        $dropSql = 'DROP DATABASE IF EXISTS "' + $restoreDatabase + '";'
        & $psql @maintenanceArgs "--set=ON_ERROR_STOP=1" "--command=$dropSql" | Out-Null
    }
    catch {
        Write-Warning "Failed to clean up isolated restore database $restoreDatabase."
    }

    if ($null -eq $oldPgPassword) {
        Remove-Item Env:PGPASSWORD -ErrorAction SilentlyContinue
    }
    else {
        $env:PGPASSWORD = $oldPgPassword
    }

    Remove-Item -LiteralPath $workRoot -Recurse -Force -ErrorAction SilentlyContinue
}
