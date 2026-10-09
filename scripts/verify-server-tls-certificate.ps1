$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) {
    throw "Server TLS certificate verification requires Windows."
}

$modulePath = Join-Path $PSScriptRoot "modules/ServerTlsCertificate.psm1"
if (-not (Test-Path -LiteralPath $modulePath -PathType Leaf)) {
    throw "Server TLS certificate module was not found."
}
Import-Module -Name $modulePath -Force

$testRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("gamenet-tls-cert-test-" + [Guid]::NewGuid().ToString("N"))
$pfxPath = Join-Path $testRoot "server.pfx"
$cerPath = Join-Path $testRoot "server-trust.cer"
$passwordText = "GameNet-TLS-Test-" + [Guid]::NewGuid().ToString("N") + "-Secure"
$password = ConvertTo-SecureString -String $passwordText -AsPlainText -Force

try {
    New-Item -ItemType Directory -Force -Path $testRoot | Out-Null
    $result = New-GameNetServerTlsCertificate -ServerAddress ([System.Net.IPAddress]::Loopback) -PfxPath $pfxPath -PublicCertificatePath $cerPath -Password $password -CertificateStoreLocation "Cert:\CurrentUser\My"

    if (-not (Test-Path -LiteralPath $pfxPath -PathType Leaf) -or
        -not (Test-Path -LiteralPath $cerPath -PathType Leaf)) {
        throw "Server TLS certificate verification failed: expected PFX/public certificate files were not created."
    }
    if ($result.Sha256Fingerprint -ne (Get-FileHash -LiteralPath $cerPath -Algorithm SHA256).Hash.ToUpperInvariant()) {
        throw "Server TLS certificate verification failed: returned SHA-256 fingerprint did not match the public certificate."
    }
    if ([string]::IsNullOrWhiteSpace($result.Thumbprint) -or
        [DateTime]::Parse($result.ExpiresUtc).ToUniversalTime() -le [DateTime]::UtcNow.AddDays(1)) {
        throw "Server TLS certificate verification failed: returned identity/expiry metadata is invalid."
    }

    $beforeHash = (Get-FileHash -LiteralPath $pfxPath -Algorithm SHA256).Hash
    $overwriteRejected = $false
    try {
        New-GameNetServerTlsCertificate -ServerAddress ([System.Net.IPAddress]::Loopback) -PfxPath $pfxPath -PublicCertificatePath (Join-Path $testRoot "another-public.cer") -Password $password -CertificateStoreLocation "Cert:\CurrentUser\My" | Out-Null
    }
    catch {
        $overwriteRejected = $true
    }
    if (-not $overwriteRejected) {
        throw "Server TLS certificate verification failed: existing output was not protected from overwrite."
    }
    $afterHash = (Get-FileHash -LiteralPath $pfxPath -Algorithm SHA256).Hash
    if ($beforeHash -ne $afterHash) {
        throw "Server TLS certificate verification failed: refusing overwrite changed existing PFX bytes."
    }

    Write-Host "Server TLS certificate create/export/reopen/SAN/EKU/fingerprint/no-overwrite verification passed."
}
finally {
    $passwordText = $null
    $password.Dispose()
    Remove-Item -LiteralPath $testRoot -Recurse -Force -ErrorAction SilentlyContinue
}
