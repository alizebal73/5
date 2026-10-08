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

$project = "src/Server/GameNet.Server.csproj"
$context = "GameNet.Server.Persistence.GameNetDbContext"

& $dotnetPath @("restore", "GameNet.slnx")
if ($LASTEXITCODE -ne 0) { throw "Restore failed." }

& $dotnetPath @("build", $project, "--configuration", "Release", "--no-restore")
if ($LASTEXITCODE -ne 0) { throw "Server build failed." }

$efBase = @(
    "ef",
    "--project", $project,
    "--startup-project", $project,
    "--context", $context,
    "--configuration", "Release"
)

& $dotnetPath @($efBase + @("migrations", "has-pending-model-changes"))
if ($LASTEXITCODE -ne 0) { throw "EF model/migration verification failed." }

& $dotnetPath @($efBase + @("migrations", "list"))
if ($LASTEXITCODE -ne 0) { throw "EF migration listing failed." }

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

            $args = @(
                "database", "update",
                "--configuration", "Release",
                "--no-build",
                "--project", "src/Server/GameNet.Server.csproj",
                "--startup-project", "src/Server/GameNet.Server.csproj",
                "--context", "GameNet.Server.Persistence.GameNetDbContext"
            )

            $output = & $dotnet "ef" @args 2>&1
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

& $dotnetPath @($efBase + @("database", "update", "--no-build"))
if ($LASTEXITCODE -ne 0) { throw "Final PostgreSQL migration execution failed." }

& $dotnetPath @($efBase + @("migrations", "has-pending-model-changes"))
if ($LASTEXITCODE -ne 0) { throw "Post-migration model verification failed." }

Write-Host "POSTGRESQL FOUNDATION MIGRATION/CONCURRENCY VERIFICATION PASSED."
