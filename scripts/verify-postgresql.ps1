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

$jobs = 1..2 | ForEach-Object {
    Start-Job -ScriptBlock {
        param($dotnet, $proj, $ctx)
        & $dotnet @("ef", "database", "update", "--project", $proj, "--startup-project", $proj, "--context", $ctx)
        return $LASTEXITCODE
    } -ArgumentList $dotnetPath, $project, $context
}
$results = Receive-Job -Job $jobs -Wait -AutoRemoveJob
if (($results | Where-Object { $_ -ne 0 }).Count -gt 0) {
    throw "Concurrent PostgreSQL migration execution failed."
}

& $dotnetPath @("ef", "database", "update", "--project", $project, "--startup-project", $project, "--context", $context)
if ($LASTEXITCODE -ne 0) { throw "Final PostgreSQL migration execution failed." }

& $dotnetPath @("ef", "migrations", "has-pending-model-changes", "--project", $project, "--startup-project", $project, "--context", $context)
if ($LASTEXITCODE -ne 0) { throw "Post-migration model verification failed." }

Write-Host "POSTGRESQL FOUNDATION MIGRATION/CONCURRENCY VERIFICATION PASSED."
