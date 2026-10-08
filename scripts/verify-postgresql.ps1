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

& $dotnetPath @("ef", "migrations", "has-pending-model-changes", "--project", $project, "--startup-project", $project, "--context", $context)
if ($LASTEXITCODE -ne 0) { throw "EF model/migration verification failed." }

& $dotnetPath @("ef", "migrations", "list", "--project", $project, "--startup-project", $project, "--context", $context)
if ($LASTEXITCODE -ne 0) { throw "EF migration listing failed." }

& $dotnetPath @("restore", "GameNet.slnx")
if ($LASTEXITCODE -ne 0) { throw "Restore failed." }

& $dotnetPath @("build", $project, "--configuration", "Release", "--no-restore")
if ($LASTEXITCODE -ne 0) { throw "Server build failed." }

$jobs = 1..2 | ForEach-Object {
    Start-Job -ScriptBlock {
        param($dotnet, $proj, $ctx)
        $output = & $dotnet @("ef", "database", "update", "--no-build", "--project", $proj, "--startup-project", $proj, "--context", $ctx) 2>&1
        [pscustomobject]@{
            ExitCode = $LASTEXITCODE
            Output = ($output -join [Environment]::NewLine)
        }
    } -ArgumentList $dotnetPath, $project, $context
}

$results = Receive-Job -Job $jobs -Wait -AutoRemoveJob
foreach ($result in $results) {
    Write-Host $result.Output
    if ($result.ExitCode -ne 0) {
        throw "Concurrent PostgreSQL migration execution failed with exit code $($result.ExitCode)."
    }
}

& $dotnetPath @("ef", "database", "update", "--no-build", "--project", $project, "--startup-project", $project, "--context", $context)
if ($LASTEXITCODE -ne 0) { throw "Final PostgreSQL migration execution failed." }

& $dotnetPath @("ef", "migrations", "has-pending-model-changes", "--project", $project, "--startup-project", $project, "--context", $context)
if ($LASTEXITCODE -ne 0) { throw "Post-migration model verification failed." }

Write-Host "POSTGRESQL FOUNDATION MIGRATION/CONCURRENCY VERIFICATION PASSED."
