$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Protect-DiagnosticFile([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return }

    $content = Get-Content -LiteralPath $Path -Raw
    $content = [Regex]::Replace($content, '(?i)\b(password|pwd)\s*=\s*([^;\s]+)', '$1=<redacted>')
    $content = [Regex]::Replace($content, '(?i)\b(signingkey|provisioningkey)\s*[:=]\s*([^;\s,"}]+)', '$1=<redacted>')
    Set-Content -LiteralPath $Path -Value $content -Encoding utf8
}

$desktop = Get-ChildItem "src/Desktop/bin/Release" -Recurse -File -Filter "*.exe" |
    Where-Object { $_.Name -like "*.Desktop.exe" -or $_.Name -eq "GameNet.Manager.Desktop.exe" } |
    Select-Object -First 1

if (-not $desktop) { throw "Built Desktop executable was not found." }

$dotnet = Join-Path $env:ProgramFiles "dotnet\dotnet.exe"
if (-not (Test-Path -LiteralPath $dotnet -PathType Leaf)) {
    $dotnet = (Get-Command dotnet -ErrorAction Stop).Source
}

$environmentNames = @(
    "GAMENET_DATABASE_CONNECTION",
    "ASPNETCORE_URLS",
    "ASPNETCORE_ENVIRONMENT",
    "DOTNET_ENVIRONMENT",
    "GameNet__Authentication__Enabled",
    "GameNet__Authentication__Issuer",
    "GameNet__Authentication__Audience",
    "GameNet__Authentication__SigningKey",
    "GameNet__Agent__ProvisioningKey",
    "GameNet__Server__BaseUrl",
    "GAMENET_DESKTOP_SMOKE",
    "GAMENET_UI_CULTURE"
)
$previousEnvironment = @{}
foreach ($name in $environmentNames) {
    $previousEnvironment[$name] = [Environment]::GetEnvironmentVariable($name, "Process")
}

$serverUrl = "http://127.0.0.1:5096"
$diagnosticRoot = Join-Path (Get-Location) "artifacts/foundation"
New-Item -ItemType Directory -Force -Path $diagnosticRoot | Out-Null
$runToken = [Guid]::NewGuid().ToString("N")
$serverLog = Join-Path $diagnosticRoot "desktop-runtime-server-$runToken.log"
$serverErrorLog = Join-Path $diagnosticRoot "desktop-runtime-server-$runToken.err"
$diagnosticFiles = @($serverLog, $serverErrorLog)
$server = $null
$failureMessage = $null
$testSucceeded = $false

try {
    $databaseConnection = $previousEnvironment["GAMENET_DATABASE_CONNECTION"]
    if ([string]::IsNullOrWhiteSpace($databaseConnection)) {
        $databaseConnection = [Environment]::GetEnvironmentVariable("GAMENET_DATABASE_CONNECTION", "Machine")
    }
    if ([string]::IsNullOrWhiteSpace($databaseConnection)) {
        throw "GAMENET_DATABASE_CONNECTION is required for Desktop runtime certification."
    }

    $env:GAMENET_DATABASE_CONNECTION = $databaseConnection
    $env:ASPNETCORE_URLS = $serverUrl
    $env:ASPNETCORE_ENVIRONMENT = "Production"
    $env:DOTNET_ENVIRONMENT = "Production"

    # Program.cs intentionally refuses to start in Production with authentication disabled.
    # Use throwaway credentials so the smoke test exercises the real Production startup guard.
    $env:GameNet__Authentication__Enabled = "true"
    $env:GameNet__Authentication__Issuer = "GameNet5.Foundation.DesktopSmoke"
    $env:GameNet__Authentication__Audience = "GameNet5.DesktopSmoke"
    $env:GameNet__Authentication__SigningKey = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(48))
    $env:GameNet__Agent__ProvisioningKey = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(48))

    $server = Start-Process -FilePath $dotnet -ArgumentList @("run","--project","src/Server/GameNet.Server.csproj","--configuration","Release","--no-build","--no-restore") -WorkingDirectory (Get-Location) -RedirectStandardOutput $serverLog -RedirectStandardError $serverErrorLog -PassThru

    $ready = $false
    $deadlineUtc = [DateTime]::UtcNow.AddSeconds(45)
    while ([DateTime]::UtcNow -lt $deadlineUtc) {
        $server.Refresh()
        if ($server.HasExited) {
            throw "Desktop smoke Server exited before readiness with exit code $($server.ExitCode). See $serverLog and $serverErrorLog."
        }

        try {
            $health = Invoke-RestMethod -Uri "$serverUrl/health" -TimeoutSec 2
            if ($health.data.readiness -eq "Ready") {
                $ready = $true
                break
            }
        }
        catch {}

        Start-Sleep -Milliseconds 500
    }

    if (-not $ready) {
        throw "Desktop smoke Server did not become ready within 45 seconds. See $serverLog and $serverErrorLog."
    }

    foreach ($culture in @("fa-IR","en-US")) {
        $env:GameNet__Server__BaseUrl = $serverUrl
        $env:GAMENET_DESKTOP_SMOKE = "1"
        $env:GAMENET_UI_CULTURE = $culture

        $desktopLog = Join-Path $diagnosticRoot "desktop-runtime-desktop-$culture-$runToken.log"
        $desktopErrorLog = Join-Path $diagnosticRoot "desktop-runtime-desktop-$culture-$runToken.err"
        $diagnosticFiles += @($desktopLog, $desktopErrorLog)

        $process = Start-Process -FilePath $desktop.FullName -PassThru -RedirectStandardOutput $desktopLog -RedirectStandardError $desktopErrorLog
        try {
            if (-not $process.WaitForExit(60000)) {
                Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
                throw "Desktop smoke timed out for $culture after 60 seconds. See $desktopLog and $desktopErrorLog."
            }

            if ($process.ExitCode -ne 0) {
                throw "Desktop smoke failed for $culture with exit code $($process.ExitCode). See $desktopLog and $desktopErrorLog."
            }
        }
        finally {
            $process.Dispose()
        }
    }

    Write-Host "DESKTOP RUNTIME / SERVER CONTRACT / fa-IR / en-US CERTIFICATION PASSED."
    $testSucceeded = $true
}
catch {
    $failureMessage = $_.Exception.ToString()
    throw
}
finally {
    if ($server) {
        $server.Refresh()
        if (-not $server.HasExited) {
            Stop-Process -Id $server.Id -Force -ErrorAction SilentlyContinue
        }
        $server.Dispose()
    }

    if ($testSucceeded) {
        foreach ($path in $diagnosticFiles) {
            Remove-Item -LiteralPath $path -Force -ErrorAction SilentlyContinue
        }
    }
    else {
        foreach ($path in $diagnosticFiles) {
            Protect-DiagnosticFile $path
        }

        $diagnosticPath = Join-Path $diagnosticRoot "desktop-runtime-diagnostic-$runToken.txt"
        @(
            "commit=$((git rev-parse HEAD 2>$null))"
            "branch=$((git branch --show-current 2>$null))"
            "timestampUtc=$([DateTime]::UtcNow.ToString('O'))"
            "failure=$failureMessage"
            "serverLog=$serverLog"
            "serverErrorLog=$serverErrorLog"
            "diagnosticFiles=$($diagnosticFiles -join ';')"
        ) | Set-Content -LiteralPath $diagnosticPath -Encoding utf8

        Write-Host "Desktop runtime diagnostics preserved under $diagnosticRoot."
    }

    foreach ($name in $environmentNames) {
        [Environment]::SetEnvironmentVariable($name, $previousEnvironment[$name], "Process")
    }
}
