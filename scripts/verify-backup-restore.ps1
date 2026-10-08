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

$pgDump = Resolve-Tool "pg_dump"
$pgRestore = Resolve-Tool "pg_restore"
$psql = Resolve-Tool "psql"

$sourceDatabaseResult = & $psql "--dbname=$connection" "--set=ON_ERROR_STOP=1" "--tuples-only" "--no-align" "--command=SELECT current_database();" 2>&1
if ($LASTEXITCODE -ne 0) {
    throw "Could not determine the source PostgreSQL database."
}

$sourceDatabase = ($sourceDatabaseResult | Select-Object -Last 1).ToString().Trim()
if ([string]::IsNullOrWhiteSpace($sourceDatabase)) {
    throw "PostgreSQL returned an empty current_database()."
}
if ($sourceDatabase -eq "postgres") {
    throw "Backup/restore certification must target the dedicated GameNet database, not the postgres maintenance database."
}

$restoreDatabase = "gamenet5_restore_probe_" + [Guid]::NewGuid().ToString("N").Substring(0, 12)

$maintenance = [System.Data.Common.DbConnectionStringBuilder]::new()
$maintenance.ConnectionString = $connection
$maintenance["Database"] = "postgres"

$restore = [System.Data.Common.DbConnectionStringBuilder]::new()
$restore.ConnectionString = $connection
$restore["Database"] = $restoreDatabase

$workRoot = Join-Path ([IO.Path]::GetTempPath()) ("gamenet5-backup-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Force -Path $workRoot | Out-Null
$dumpFile = Join-Path $workRoot "foundation.dump"

try {
    Write-Host "Creating isolated backup artifact..."
    & $pgDump $connection "--format=custom" "--file=$dumpFile" "--no-owner" "--no-acl"
    if ($LASTEXITCODE -ne 0) { throw "pg_dump failed." }

    if (-not (Test-Path -LiteralPath $dumpFile -PathType Leaf)) {
        throw "Backup artifact was not created."
    }

    Write-Host "Creating isolated restore database..."
    $createSql = 'CREATE DATABASE "' + $restoreDatabase + '";'
    & $psql "--dbname=$($maintenance.ConnectionString)" "--set=ON_ERROR_STOP=1" "--command=$createSql"
    if ($LASTEXITCODE -ne 0) { throw "Could not create isolated restore database." }

    Write-Host "Restoring backup artifact..."
    & $pgRestore "--dbname=$($restore.ConnectionString)" "--exit-on-error" "--no-owner" "--no-acl" $dumpFile
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

    $result = ((& $psql "--dbname=$($restore.ConnectionString)" "--set=ON_ERROR_STOP=1" "--tuples-only" "--no-align" "--command=$check" 2>&1) -join "").Trim()
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
        $dropSql = 'DROP DATABASE IF EXISTS "' + $restoreDatabase + '";'
        & $psql "--dbname=$($maintenance.ConnectionString)" "--set=ON_ERROR_STOP=1" "--command=$dropSql" | Out-Null
    }
    catch {
        Write-Warning "Failed to clean up isolated restore database $restoreDatabase."
    }

    Remove-Item -LiteralPath $workRoot -Recurse -Force -ErrorAction SilentlyContinue
}
