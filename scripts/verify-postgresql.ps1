$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$dotnetPath = Join-Path $env:ProgramFiles "dotnet\dotnet.exe"
if (-not (Test-Path -LiteralPath $dotnetPath -PathType Leaf)) {
    $dotnetPath = (Get-Command dotnet -ErrorAction Stop).Source
}

$connection = [Environment]::GetEnvironmentVariable("GAMENET_DATABASE_CONNECTION", "Process")
if ([string]::IsNullOrWhiteSpace($connection)) {
    $connection = [Environment]::GetEnvironmentVariable("GAMENET_DATABASE_CONNECTION", "Machine")
}
if ([string]::IsNullOrWhiteSpace($connection)) {
    throw "GAMENET_DATABASE_CONNECTION is required for PostgreSQL Foundation certification."
}


function Get-NpgsqlBuilder([string]$connectionString) {
    $assemblyPath = Join-Path (Get-Location) "src\Server\bin\Release\net10.0\Npgsql.dll"
    if (-not (Test-Path -LiteralPath $assemblyPath -PathType Leaf)) {
        throw "Npgsql runtime assembly was not found at $assemblyPath."
    }

    $loaded = [AppDomain]::CurrentDomain.GetAssemblies() | Where-Object { $_.GetName().Name -eq "Npgsql" }
    if (-not $loaded) { Add-Type -Path $assemblyPath }
    return [Npgsql.NpgsqlConnectionStringBuilder]::new($connectionString)
}

function Assert-FoundationSchema([string]$connectionString) {
    $builder = Get-NpgsqlBuilder $connectionString
    $hostValue = if ([string]::IsNullOrWhiteSpace($builder.Host)) { "localhost" } else { $builder.Host }
    $port = $builder.Port
    $username = $builder.Username
    $password = $builder.Password
    $database = $builder.Database
    $sslMode = $builder.SslMode.ToString()

    if ([string]::IsNullOrWhiteSpace($database)) { $database = $username }
    if ([string]::IsNullOrWhiteSpace($username) -or [string]::IsNullOrWhiteSpace($database)) {
        throw "Foundation schema verification requires PostgreSQL username and database."
    }

    $psql = Join-Path $env:ProgramFiles "PostgreSQL\17\bin\psql.exe"
    if (-not (Test-Path -LiteralPath $psql -PathType Leaf)) {
        $psql = (Get-Command psql -ErrorAction Stop).Source
    }

    $oldPassword = [Environment]::GetEnvironmentVariable("PGPASSWORD","Process")
    $oldSslMode = [Environment]::GetEnvironmentVariable("PGSSLMODE","Process")
    try {
        if (-not [string]::IsNullOrWhiteSpace($password)) { $env:PGPASSWORD = $password }
        if (-not [string]::IsNullOrWhiteSpace($sslMode)) { $env:PGSSLMODE = $sslMode.ToLowerInvariant() }

        $cli = @("--host=$hostValue","--port=$port","--username=$username","--dbname=$database")
        $probe = @"
SELECT CASE WHEN
    to_regclass('public.audit_entries') IS NOT NULL AND
    to_regclass('public.idempotency_records') IS NOT NULL AND
    to_regclass('public.outbox_messages') IS NOT NULL AND
    to_regclass('public.agent_credentials') IS NOT NULL AND
    to_regclass('public.agent_connection_leases') IS NOT NULL AND
    EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'public'
          AND table_name = 'audit_entries'
          AND column_name = 'occurred_at_utc'
    ) AND
    NOT EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'public'
          AND table_name = 'audit_entries'
          AND column_name = 'OccurredAtUtc'
    )
    THEN 'PASS' ELSE 'FAIL' END;
"@

        $result = ((& $psql @cli "--set=ON_ERROR_STOP=1" "--tuples-only" "--no-align" "--command=$probe" 2>&1) -join "").Trim()
        if ($LASTEXITCODE -ne 0) {
            throw "Foundation schema verification query failed."
        }
        if ($result -ne "PASS") {
            throw "Required Foundation schema is incomplete or has an invalid AuditEntry timestamp column."
        }
    }
    finally {
        if ($null -eq $oldPassword) { Remove-Item Env:PGPASSWORD -ErrorAction SilentlyContinue } else { $env:PGPASSWORD = $oldPassword }
        if ($null -eq $oldSslMode) { Remove-Item Env:PGSSLMODE -ErrorAction SilentlyContinue } else { $env:PGSSLMODE = $oldSslMode }
    }
}

$project = "src/Server/GameNet.Server.csproj"
$context = "GameNet.Server.Persistence.GameNetDbContext"

& $dotnetPath @("restore", "GameNet.slnx")
if ($LASTEXITCODE -ne 0) { throw "Restore failed." }

& $dotnetPath @("build", $project, "--configuration", "Release", "--no-restore")
if ($LASTEXITCODE -ne 0) { throw "Server build failed." }

$efOptions = @(
    "--project", $project,
    "--startup-project", $project,
    "--context", $context,
    "--configuration", "Release"
)

& $dotnetPath @(@("ef", "migrations", "has-pending-model-changes") + $efOptions)
if ($LASTEXITCODE -ne 0) {
    $diagnosticRoot = Join-Path (Get-Location) "artifacts/foundation"
    New-Item -ItemType Directory -Force -Path $diagnosticRoot | Out-Null
    $diagnosticName = "ModelDriftDiagnostic" + [Guid]::NewGuid().ToString("N").Substring(0, 8)
    $diagnosticArgs = @("ef", "migrations", "add", $diagnosticName) + $efOptions
    $diagnosticOutput = & $dotnetPath @diagnosticArgs 2>&1
    $diagnosticExitCode = $LASTEXITCODE
    $diagnosticPath = Join-Path $diagnosticRoot "ef-model-drift-diagnostic.txt"

    $diagnosticLines = @(
        "diagnostic=EF pending model changes"
        "commit=$((git rev-parse HEAD 2>$null))"
        "branch=$((git branch --show-current 2>$null))"
        "migrationScaffoldExitCode=$diagnosticExitCode"
        "migrationScaffoldOutput=$($diagnosticOutput -join [Environment]::NewLine)"
    )

    $generated = @(
        Get-ChildItem -LiteralPath "src/Server/Persistence/Migrations" -File -Filter "*.cs" |
            Where-Object { $_.Name -like "*$diagnosticName*" }
    )
    foreach ($file in $generated) {
        $diagnosticLines += ""
        $diagnosticLines += "===== $($file.Name) ====="
        $diagnosticLines += [System.IO.File]::ReadAllText($file.FullName)
    }
    if ($generated.Count -eq 0) {
        $diagnosticLines += "No generated migration source was found."
    }

    $diagnosticLines | Set-Content -LiteralPath $diagnosticPath -Encoding utf8
    throw "EF model/migration verification failed. Inspect artifacts/foundation/ef-model-drift-diagnostic.txt; the diagnostic migration exists only in this disposable CI workspace."
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

& $dotnetPath @(@("ef", "migrations", "list") + $efOptions)
if ($LASTEXITCODE -ne 0) { throw "EF migration listing failed." }

$cleanRoot = Join-Path ([IO.Path]::GetTempPath()) ("gamenet5-clean-migration-" + [Guid]::NewGuid().ToString("N"))
$cleanClusterLog = Join-Path $cleanRoot "postgres.log"
$cleanCluster = Join-Path $cleanRoot "cluster"
$cleanDatabase = "gamenet5_clean_probe_" + [Guid]::NewGuid().ToString("N").Substring(0, 12)
$cleanPort = Get-EphemeralPort
$cleanClusterStarted = $false
$previousProcessConnection = [Environment]::GetEnvironmentVariable("GAMENET_DATABASE_CONNECTION", "Process")

function Resolve-PgTool([string]$name) {
    $default = Join-Path $env:ProgramFiles "PostgreSQL\17\bin\$name.exe"
    if (Test-Path -LiteralPath $default -PathType Leaf) { return $default }
    return (Get-Command $name -ErrorAction Stop).Source
}

try {
    New-Item -ItemType Directory -Force -Path $cleanRoot | Out-Null
    $initdb = Resolve-PgTool "initdb"
    $pgCtl = Resolve-PgTool "pg_ctl"
    $psql = Resolve-PgTool "psql"

    Write-Host "===== Clean PostgreSQL migration certification ====="
    & $initdb "--pgdata=$cleanCluster" "--username=postgres" "--auth=trust" "--no-instructions"
    if ($LASTEXITCODE -ne 0) { throw "Could not initialize the isolated clean-migration PostgreSQL cluster." }

    & $pgCtl "--pgdata=$cleanCluster" "--log=$cleanClusterLog" "--wait" "--timeout=60" "--options=-p $cleanPort -h 127.0.0.1" "start"
    if ($LASTEXITCODE -ne 0) { throw "Could not start the isolated clean-migration PostgreSQL cluster." }
    $cleanClusterStarted = $true

    $adminArgs = @("--host=127.0.0.1","--port=$cleanPort","--username=postgres","--dbname=postgres")
    $createSql = 'CREATE DATABASE "' + $cleanDatabase + '";'
    & $psql @adminArgs "--set=ON_ERROR_STOP=1" "--command=$createSql"
    if ($LASTEXITCODE -ne 0) { throw "Could not create the isolated clean-migration database." }

    $env:GAMENET_DATABASE_CONNECTION = "Host=127.0.0.1;Port=$cleanPort;Database=$cleanDatabase;Username=postgres;Pooling=false"
    & $dotnetPath @(@("ef", "database", "update", "--no-build") + $efOptions)
    if ($LASTEXITCODE -ne 0) { throw "Clean PostgreSQL migration execution failed." }

    Assert-FoundationSchema $env:GAMENET_DATABASE_CONNECTION
    Write-Host "Clean PostgreSQL migration/schema verification passed."
}
finally {
    if ($null -eq $previousProcessConnection) {
        Remove-Item Env:GAMENET_DATABASE_CONNECTION -ErrorAction SilentlyContinue
    }
    else {
        $env:GAMENET_DATABASE_CONNECTION = $previousProcessConnection
    }

    if ($cleanClusterStarted) {
        try {
            & $pgCtl "--pgdata=$cleanCluster" "--mode=fast" "--wait" "stop" | Out-Null
        }
        catch {
            Write-Warning "Failed to stop isolated clean-migration PostgreSQL cluster."
        }
    }

    Remove-Item -LiteralPath $cleanRoot -Recurse -Force -ErrorAction SilentlyContinue
}




$concurrencyRoot = Join-Path ([IO.Path]::GetTempPath()) ("gamenet5-ef-concurrency-" + [Guid]::NewGuid().ToString("N"))
$workspaceRoots = @(
    (Join-Path $concurrencyRoot "worker-1"),
    (Join-Path $concurrencyRoot "worker-2")
)

try {
    New-Item -ItemType Directory -Force -Path $concurrencyRoot | Out-Null

    foreach ($workspace in $workspaceRoots) {
        New-Item -ItemType Directory -Force -Path $workspace | Out-Null
        Copy-Item -LiteralPath ".\src" -Destination (Join-Path $workspace "src") -Recurse -Force
        Copy-Item -LiteralPath ".\Directory.Build.props" -Destination (Join-Path $workspace "Directory.Build.props") -Force
        Copy-Item -LiteralPath ".\Directory.Packages.props" -Destination (Join-Path $workspace "Directory.Packages.props") -Force
        Copy-Item -LiteralPath ".\global.json" -Destination (Join-Path $workspace "global.json") -Force
        Copy-Item -LiteralPath ".\.config" -Destination (Join-Path $workspace ".config") -Recurse -Force

        Push-Location -LiteralPath $workspace
        try {
            & $dotnetPath "tool" "restore" "--tool-manifest" ".config\dotnet-tools.json"
            if ($LASTEXITCODE -ne 0) {
                throw "Could not restore dotnet-ef tooling in isolated concurrency workspace."
            }
        }
        finally {
            Pop-Location
        }
    }

    $jobs = 1..2 | ForEach-Object {
        $workspace = $workspaceRoots[$_ - 1]

        Start-Job -ScriptBlock {
            param($dotnet, $workspacePath)

            Set-Location -LiteralPath $workspacePath

            $ef = "dotnet-ef"
            $args = @(
                "database", "update",
                "--configuration", "Release",
                "--no-build",
                "--project", "src/Server/GameNet.Server.csproj",
                "--startup-project", "src/Server/GameNet.Server.csproj",
                "--context", "GameNet.Server.Persistence.GameNetDbContext"
            )

            $output = & $ef @args 2>&1
            [pscustomobject]@{
                Workspace = $workspacePath
                ExitCode = $LASTEXITCODE
                Output = ($output -join [Environment]::NewLine)
            }
        } -ArgumentList $dotnetPath, $workspace
    }

    $results = Receive-Job -Job $jobs -Wait -AutoRemoveJob
    foreach ($result in $results) {
        Write-Host "===== EF concurrency workspace: $($result.Workspace) ====="
        Write-Host $result.Output

        if ($result.ExitCode -ne 0) {
            throw "Concurrent PostgreSQL migration execution failed with exit code $($result.ExitCode)."
        }
    }
}
finally {
    Remove-Item -LiteralPath $concurrencyRoot -Recurse -Force -ErrorAction SilentlyContinue
}

& $dotnetPath @(@("ef", "database", "update", "--no-build") + $efOptions)
if ($LASTEXITCODE -ne 0) { throw "Final PostgreSQL migration execution failed." }

& $dotnetPath @(@("ef", "migrations", "has-pending-model-changes") + $efOptions)
if ($LASTEXITCODE -ne 0) { throw "Post-migration model verification failed." }

Assert-FoundationSchema $connection

Write-Host "POSTGRESQL FOUNDATION MIGRATION/CONCURRENCY/SCHEMA VERIFICATION PASSED."
