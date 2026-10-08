$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$desktop = Get-ChildItem "src/Desktop/bin/Release" -Recurse -File -Filter "*.exe" |
    Where-Object { $_.Name -like "*.Desktop.exe" -or $_.Name -eq "GameNet.Manager.Desktop.exe" } |
    Select-Object -First 1

if (-not $desktop) { throw "Built Desktop executable was not found." }

$dotnet = Join-Path $env:ProgramFiles "dotnet\dotnet.exe"
if (-not (Test-Path $dotnet)) { $dotnet = (Get-Command dotnet -ErrorAction Stop).Source }

$serverUrl = "http://127.0.0.1:5096"
$serverLog = Join-Path ([IO.Path]::GetTempPath()) ("gamenet5-desktop-server-" + [Guid]::NewGuid().ToString("N") + ".log")
$server = Start-Process -FilePath $dotnet -ArgumentList @("run","--project","src/Server/GameNet.Server.csproj","--no-build","--no-restore") -WorkingDirectory (Get-Location) -RedirectStandardOutput $serverLog -RedirectStandardError $serverLog -PassThru

try {
    $env:ASPNETCORE_URLS = $serverUrl
    for ($i = 0; $i -lt 45; $i++) {
        Start-Sleep -Seconds 1
        try {
            $health = Invoke-RestMethod -Uri "$serverUrl/health" -TimeoutSec 2
            if ($health.data.readiness -eq "Ready") { break }
        } catch {}
        if ($i -eq 44) { throw "Desktop smoke Server did not become ready. See $serverLog." }
    }

    $previousUrl = [Environment]::GetEnvironmentVariable("GameNet__Server__BaseUrl","Process")
    $previousSmoke = [Environment]::GetEnvironmentVariable("GAMENET_DESKTOP_SMOKE","Process")
    $previousCulture = [Environment]::GetEnvironmentVariable("GAMENET_UI_CULTURE","Process")

    try {
        foreach ($culture in @("fa-IR","en-US")) {
            $env:GameNet__Server__BaseUrl = $serverUrl
            $env:GAMENET_DESKTOP_SMOKE = "1"
            $env:GAMENET_UI_CULTURE = $culture
            $process = Start-Process -FilePath $desktop.FullName -PassThru -Wait
            if ($process.ExitCode -ne 0) {
                throw "Desktop smoke failed for $culture with exit code $($process.ExitCode)."
            }
        }
    }
    finally {
        if ($null -eq $previousUrl) { Remove-Item Env:GameNet__Server__BaseUrl -ErrorAction SilentlyContinue } else { $env:GameNet__Server__BaseUrl = $previousUrl }
        if ($null -eq $previousSmoke) { Remove-Item Env:GAMENET_DESKTOP_SMOKE -ErrorAction SilentlyContinue } else { $env:GAMENET_DESKTOP_SMOKE = $previousSmoke }
        if ($null -eq $previousCulture) { Remove-Item Env:GAMENET_UI_CULTURE -ErrorAction SilentlyContinue } else { $env:GAMENET_UI_CULTURE = $previousCulture }
    }

    Write-Host "DESKTOP RUNTIME / SERVER CONTRACT / fa-IR / en-US CERTIFICATION PASSED."
}
finally {
    Stop-Process -Id $server.Id -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $serverLog -Force -ErrorAction SilentlyContinue
    Remove-Item Env:ASPNETCORE_URLS -ErrorAction SilentlyContinue
}
