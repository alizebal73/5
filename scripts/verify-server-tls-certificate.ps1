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

$testRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("gamenet-tls-store-test-" + [Guid]::NewGuid().ToString("N"))
$publicPath = Join-Path $testRoot "server-trust.cer"
$serverAddress = [System.Net.IPAddress]::Loopback
$dnsName = "gamenet-tls-test-" + [Guid]::NewGuid().ToString("N") + ".local"
$thumbprint = $null
$certificate = $null
$publicCertificate = $null
$rsa = $null

try {
    New-Item -ItemType Directory -Force -Path $testRoot | Out-Null
    $result = New-GameNetServerTlsCertificate -ServerAddress $serverAddress -DnsName $dnsName -PublicCertificatePath $publicPath -CertificateStoreLocation "Cert:\CurrentUser\My"
    $thumbprint = $result.Thumbprint

    if (-not (Test-Path -LiteralPath $publicPath -PathType Leaf)) {
        throw "Server TLS certificate verification failed: public certificate file was not created."
    }
    if ($result.Sha256Fingerprint -ne (Get-FileHash -LiteralPath $publicPath -Algorithm SHA256).Hash.ToUpperInvariant()) {
        throw "Server TLS certificate verification failed: SHA-256 fingerprint does not match the public certificate."
    }
    if ([string]::IsNullOrWhiteSpace($result.Thumbprint) -or [DateTime]::Parse($result.ExpiresUtc).ToUniversalTime() -le [DateTime]::UtcNow.AddDays(1)) {
        throw "Server TLS certificate verification failed: identity/expiry metadata is invalid."
    }

    $certificate = Get-Item -LiteralPath ("Cert:\CurrentUser\My\" + $thumbprint)
    if (-not $certificate.HasPrivateKey) {
        throw "Server TLS certificate verification failed: the certificate store entry lacks its private key."
    }
    Test-GameNetServerTlsCertificateProfile -Certificate $certificate -ServerAddress $serverAddress -DnsName $dnsName

    $rsa = [System.Security.Cryptography.X509Certificates.RSACertificateExtensions]::GetRSAPrivateKey($certificate)
    $exportRejected = $false
    try {
        $privateBytes = $rsa.ExportRSAPrivateKey()
        [System.Security.Cryptography.CryptographicOperations]::ZeroMemory($privateBytes)
    }
    catch [System.Security.Cryptography.CryptographicException] {
        $exportRejected = $true
    }
    if (-not $exportRejected) {
        throw "Server TLS certificate verification failed: the private key was exportable."
    }

    $publicCertificate = [System.Security.Cryptography.X509Certificates.X509Certificate2]::new($publicPath)
    if ($publicCertificate.HasPrivateKey -or $publicCertificate.Thumbprint -ne $thumbprint) {
        throw "Server TLS certificate verification failed: the public .cer contains a private key or does not match the store certificate."
    }

    $beforeHash = (Get-FileHash -LiteralPath $publicPath -Algorithm SHA256).Hash
    $overwriteRejected = $false
    try {
        New-GameNetServerTlsCertificate -ServerAddress $serverAddress -DnsName $dnsName -PublicCertificatePath $publicPath -CertificateStoreLocation "Cert:\CurrentUser\My" | Out-Null
    }
    catch {
        $overwriteRejected = $true
    }
    if (-not $overwriteRejected) {
        throw "Server TLS certificate verification failed: existing public output was not protected from overwrite."
    }
    $afterHash = (Get-FileHash -LiteralPath $publicPath -Algorithm SHA256).Hash
    if ($beforeHash -ne $afterHash) {
        throw "Server TLS certificate verification failed: refusing overwrite changed existing public certificate bytes."
    }

    Write-Host "SERVER_TLS_CERTIFICATE_VALIDATION_PASSED: SAN/EKU/validity, non-exportable private key, public-only export, SHA-256 fingerprint, and no-overwrite behavior."
}
finally {
    if ($null -ne $rsa) { $rsa.Dispose() }
    if ($null -ne $publicCertificate) { $publicCertificate.Dispose() }
    if ($null -ne $certificate) { $certificate.Dispose() }
    if (-not [string]::IsNullOrWhiteSpace($thumbprint)) {
        Remove-Item -LiteralPath ("Cert:\CurrentUser\My\" + $thumbprint) -Force -ErrorAction SilentlyContinue
    }
    Remove-Item -LiteralPath $testRoot -Recurse -Force -ErrorAction SilentlyContinue
}
