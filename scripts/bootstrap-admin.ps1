[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ServerBaseUrl
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

try { $serverUri = [Uri]::new($ServerBaseUrl, [UriKind]::Absolute) }
catch { throw "ServerBaseUrl must be an absolute HTTPS URL, or HTTP loopback for local setup." }

if ($serverUri.Scheme -ne [Uri]::UriSchemeHttps -and -not ($serverUri.Scheme -eq [Uri]::UriSchemeHttp -and $serverUri.IsLoopback)) {
    throw "Refusing to send the bootstrap secret or owner password over remote HTTP."
}
if (-not [string]::IsNullOrEmpty($serverUri.UserInfo) -or $serverUri.AbsolutePath -notin @("", "/") -or
    -not [string]::IsNullOrEmpty($serverUri.Query) -or -not [string]::IsNullOrEmpty($serverUri.Fragment)) {
    throw "ServerBaseUrl must be the server origin without credentials, path, query, or fragment."
}

$baseUrl = $serverUri.GetLeftPart([UriPartial]::Authority).TrimEnd("/")
$headers = @{ "X-GameNet-Contract" = "v1" }

function ConvertFrom-SecurePrompt([System.Security.SecureString]$Value) {
    $pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($Value)
    try { return [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer) }
    finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer) }
}

$secureSecret = $null
$securePassword = $null
$bootstrapSecret = $null
$password = $null
$payload = $null

try {
    $status = Invoke-RestMethod -Method Get -Uri "$baseUrl/api/v1/bootstrap/status" -Headers $headers -TimeoutSec 15
    if (-not $status.data.required) { throw "Initial setup is already complete. No administrator was created." }

    $username = (Read-Host "Owner username").Trim()
    $displayName = (Read-Host "Owner display name").Trim()
    $secureSecret = Read-Host "Initial owner bootstrap secret (the same value entered in the protected Server settings file)" -AsSecureString
    $securePassword = Read-Host "Choose an owner password (minimum 10 characters)" -AsSecureString
    $bootstrapSecret = ConvertFrom-SecurePrompt $secureSecret
    $password = ConvertFrom-SecurePrompt $securePassword

    if ([string]::IsNullOrWhiteSpace($username) -or [string]::IsNullOrWhiteSpace($displayName)) { throw "Owner username and display name are required." }
    if ($password.Length -lt 10 -or $password.Length -gt 256) { throw "Owner password must contain 10-256 characters." }
    if ([Text.Encoding]::UTF8.GetByteCount($bootstrapSecret) -lt 32) { throw "The configured bootstrap secret must contain at least 32 UTF-8 bytes." }

    $headers["X-GameNet-Bootstrap-Secret"] = $bootstrapSecret
    $payload = @{ username = $username; displayName = $displayName; password = $password } | ConvertTo-Json -Compress
    $response = Invoke-RestMethod -Method Post -Uri "$baseUrl/api/v1/bootstrap/admin" -Headers $headers -ContentType "application/json" -Body $payload -TimeoutSec 15

    Write-Host ("Initial owner created: {0} ({1})" -f $response.data.username, $response.data.userId)
    Write-Host "The Server reads the expected bootstrap secret from its DPAPI-protected settings file; a machine-wide GAMENET_BOOTSTRAP_SECRET is not required."
Write-Warning "The one-time bootstrap secret remains in the protected file. Installation is not considered fully hardened until a supported post-bootstrap step removes it from that file."
}
finally {
    [void]$headers.Remove("X-GameNet-Bootstrap-Secret")
    $bootstrapSecret = $null
    $password = $null
    $payload = $null
    if ($secureSecret) { $secureSecret.Dispose() }
    if ($securePassword) { $securePassword.Dispose() }
}
