[CmdletBinding()]
param(
    [ValidateSet("Desktop", "Agent")]
    [string]$Component = "Desktop",

    [string]$ServerBaseUrl
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

if ([string]::IsNullOrWhiteSpace($ServerBaseUrl)) {
    $common = [Environment]::GetFolderPath([Environment+SpecialFolder]::CommonApplicationData)
    $configDirectory = Join-Path (Join-Path $common "GameNet Manager") "Config"
    $fileName = if ($Component -eq "Desktop") { "desktop.json" } else { "agent.json" }
    $configPath = Join-Path $configDirectory $fileName

    if (-not (Test-Path -LiteralPath $configPath -PathType Leaf)) {
        throw "Runtime configuration is missing: $configPath"
    }

    $configuration = Get-Content -LiteralPath $configPath -Raw | ConvertFrom-Json
    $ServerBaseUrl = if ($Component -eq "Desktop") {
        [string]$configuration.GameNet.Server.BaseUrl
    }
    else {
        [string]$configuration.GameNet.AgentTransport.ServerBaseUrl
    }
}

$uri = $null
if (-not [Uri]::TryCreate($ServerBaseUrl, [UriKind]::Absolute, [ref]$uri)) {
    throw "ServerBaseUrl must be an absolute URL."
}
if (($uri.Scheme -ine "https") -and (-not ($uri.Scheme -ieq "http" -and $uri.IsLoopback))) {
    throw "Network runtime checks require HTTPS. HTTP is accepted only for loopback development/certification."
}
if (-not [string]::IsNullOrEmpty($uri.UserInfo) -or
    -not [string]::IsNullOrEmpty($uri.Query) -or
    -not [string]::IsNullOrEmpty($uri.Fragment) -or
    $uri.AbsolutePath -ne "/") {
    throw "ServerBaseUrl must contain only the scheme, host and optional port."
}

$healthUri = [Uri]::new($uri, "health")
# Invoke-RestMethod intentionally uses the operating system's normal TLS validation.
# Do not add certificate-validation bypasses to this check.
$response = Invoke-RestMethod -Method Get -Uri $healthUri.AbsoluteUri -TimeoutSec 10
if ($response.data.readiness -ne "Ready") {
    throw "Server health returned a non-ready status."
}

Write-Host "SERVER_ENDPOINT_OK component=$Component scheme=$($uri.Scheme) host=$($uri.Host) port=$($uri.Port) readiness=$($response.data.readiness) version=$($response.data.version)"
