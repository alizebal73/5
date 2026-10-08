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

function Get-PgCliInfo([string]$value, [ref]$oldPassword) {
    $oldPassword.Value = [Environment]::GetEnvironmentVariable("PGPASSWORD","Process")

    if ($value -match '^(?i)postgres(?:ql)?://') {
        $uri = [Uri]$value
        if ([string]::IsNullOrWhiteSpace($uri.UserInfo)) {
            throw "PostgreSQL URI must include a username for CLI certification."
        }

        $parts = $uri.UserInfo.Split(':',2)
        $user = [Uri]::UnescapeDataString($parts[0])
        $password = if ($parts.Count -eq 2) { [Uri]::UnescapeDataString($parts[1]) } else { $null }
        if (-not [string]::IsNullOrWhiteSpace($password)) { $env:PGPASSWORD = $password }

        $db = $uri.AbsolutePath.TrimStart("/")
        if ([string]::IsNullOrWhiteSpace($db)) { $db = $user }

        $port = if ($uri.Port -gt 0) { $uri.Port } else { 5432 }
        $ssl = $null
        foreach ($part in $uri.Query.TrimStart("?").Split("&",[StringSplitOptions]::RemoveEmptyEntries)) {
            $kv = $part.Split("=",2)
            if ($kv[0] -ieq "sslmode" -and $kv.Count -eq 2) {
                $ssl = [Uri]::UnescapeDataString($kv[1])
            }
        }

        $args = @("--host=$($uri.Host)","--port=$port","--username=$user","--dbname=$db")
        if (-not [string]::IsNullOrWhiteSpace($ssl)) {
            $args += "--sslmode=$($ssl.ToLowerInvariant())"
        }

        return [pscustomobject]@{ Args = $args; Database = $db }
    }

    $builder = [System.Data.Common.DbConnectionStringBuilder]::new()
    $builder.ConnectionString = $value
    $hostValue = Get-ConnectionValue $builder @("Host","Server","Address")
    $port = Get-ConnectionValue $builder @("Port")
    $user = Get-ConnectionValue $builder @("Username","User Id","User")
    $password = Get-ConnectionValue $builder @("Password","Pwd")
    $db = Get-ConnectionValue $builder @("Database","Initial Catalog")
    $ssl = Get-ConnectionValue $builder @("SSL Mode","SslMode")

    if ([string]::IsNullOrWhiteSpace($user)) { throw "The PostgreSQL connection string must specify Username/User Id for CLI certification." }
    if ([string]::IsNullOrWhiteSpace($db)) { $db = $user }
    if ([string]::IsNullOrWhiteSpace($hostValue)) { $hostValue = "localhost" }
    if ([string]::IsNullOrWhiteSpace($port)) { $port = "5432" }
    if (-not [string]::IsNullOrWhiteSpace($password)) { $env:PGPASSWORD = $password }

    $args = @("--host=$hostValue","--port=$port","--username=$user","--dbname=$db")
    if (-not [string]::IsNullOrWhiteSpace($ssl)) {
        $args += "--sslmode=$($ssl.ToLowerInvariant())"
    }

    return [pscustomobject]@{ Args = $args; Database = $db }
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

$pgDump = Resolve-Tool "pg_dump"
$pgRestore = Resolve-Tool "pg_restore"
$psql = Resolve-Tool "psql"
$initdb = Resolve-Tool "initdb"
$pgCtl = Resolve-Tool "pg_ctl"

$oldPgPassword = $null
$pgInfo = Get-PgCliInfo $connection ([ref]$oldPgPassword)
$sourceCli = $pgInfo.Args
$sourceDatabase = $pgInfo.Database

$workRoot = Join-Path ([IO.Path]::GetTempPath()) ("gamenet5-backup-" + [Guid]::NewGuid().ToString("N"))
$clusterRoot = Join-Path $workRoot "restore-cluster"
$clusterLog = Join-Path $workRoot "restore-cluster.log"
$dumpFile = Join-Path $workRoot "foundation.dump"
$restoreDatabase = "gamenet5_restore_probe_" + [Guid]::NewGuid().ToString("N").Substring(0, 12)
$restorePort = Get-EphemeralPort

New-Item -ItemType Directory -Force -Path $workRoot | Out-Null
$clusterStarted = $false

try {
    if ($sourceDatabase -eq "postgres") {
        throw "Backup/restore certification must target the dedicated GameNet database, not the postgres maintenance database."
    }

    Write-Host "Creating isolated backup artifact..."
    & $pgDump @sourceCli "--format=custom" "--file=$dumpFile" "--no-owner" "--no-acl"
    if ($LASTEXITCODE -ne 0) { throw "pg_dump failed." }
    if (-not (Test-Path -LiteralPath $dumpFile -PathType Leaf)) { throw "Backup artifact was not created." }

    Write-Host "Initializing isolated PostgreSQL cluster..."
    & $initdb "--pgdata=$clusterRoot" "--username=postgres" "--auth=trust" "--no-instructions"
    if ($LASTEXITCODE -ne 0) { throw "initdb failed." }

    Write-Host "Starting isolated PostgreSQL cluster on port $restorePort..."
    & $pgCtl "--pgdata=$clusterRoot" "--log=$clusterLog" "--wait" "--timeout=60" "--options=-p $restorePort -h 127.0.0.1" "start"
    if ($LASTEXITCODE -ne 0) { throw "pg_ctl start failed." }
    $clusterStarted = $true

    $tempAdminArgs = @("--host=127.0.0.1","--port=$restorePort","--username=postgres","--dbname=postgres")
    $createSql = 'CREATE DATABASE "' + $restoreDatabase + '";'
    & $psql @tempAdminArgs "--set=ON_ERROR_STOP=1" "--command=$createSql"
    if ($LASTEXITCODE -ne 0) { throw "Could not create isolated restore database in temporary PostgreSQL cluster." }

    Write-Host "Restoring backup artifact into isolated database..."
    $tempRestoreArgs = @("--host=127.0.0.1","--port=$restorePort","--username=postgres","--dbname=$restoreDatabase")
    & $pgRestore @tempRestoreArgs "--exit-on-error" "--no-owner" "--no-acl" $dumpFile
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

    $result = ((& $psql @tempRestoreArgs "--set=ON_ERROR_STOP=1" "--tuples-only" "--no-align" "--command=$check" 2>&1) -join "").Trim()
    if ($LASTEXITCODE -ne 0) { throw "Restored database validation query failed: $result" }
    if ($result -ne "PASS") { throw "Restored database is missing one or more Foundation tables." }

    Write-Host "BACKUP/ISOLATED-RESTORE CERTIFICATION PASSED."
    Write-Host "Source database: $sourceDatabase"
    Write-Host "Isolated restore database: $restoreDatabase"
    Write-Host "Isolated restore cluster port: $restorePort"
}
finally {
    if ($clusterStarted) {
        try { & $pgCtl "--pgdata=$clusterRoot" "--mode=fast" "--wait" "stop" | Out-Null } catch { Write-Warning "Failed to stop isolated PostgreSQL cluster." }
    }

    if ($null -eq $oldPgPassword) { Remove-Item Env:PGPASSWORD -ErrorAction SilentlyContinue }
    else { $env:PGPASSWORD = $oldPgPassword }

    Remove-Item -LiteralPath $workRoot -Recurse -Force -ErrorAction SilentlyContinue
}
