[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$CertificatePath,

    [Parameter(Mandatory = $true)]
    [string]$ExpectedSha256Fingerprint,

    [switch]$ValidateOnly
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

if (-not (Test-Path -LiteralPath $CertificatePath -PathType Leaf)) {
    throw "Public certificate file was not found: $CertificatePath"
}

$expected = ($ExpectedSha256Fingerprint -replace '\s', '').ToUpperInvariant()
if ($expected -notmatch '^[0-9A-F]{64}$') {
    throw "ExpectedSha256Fingerprint must be a 64-character SHA-256 fingerprint of the exported public .cer file."
}

$resolvedPath = (Resolve-Path -LiteralPath $CertificatePath).Path
$actual = (Get-FileHash -LiteralPath $resolvedPath -Algorithm SHA256).Hash.ToUpperInvariant()
if ($actual -ne $expected) {
    throw "Public certificate SHA-256 fingerprint does not match the value independently verified from the Server PC."
}

$certificate = [System.Security.Cryptography.X509Certificates.X509Certificate2]::new($resolvedPath)
try {
    if ($certificate.HasPrivateKey) {
        throw "Refusing to import a certificate that contains a private key. Copy only the exported public .cer file."
    }

    $nowUtc = [DateTime]::UtcNow
    if ($certificate.NotBefore.ToUniversalTime() -gt $nowUtc.AddMinutes(5) -or
        $certificate.NotAfter.ToUniversalTime() -le $nowUtc) {
        throw "Server certificate is outside its valid time window."
    }

    $hasSan = $certificate.Extensions | Where-Object { $_.Oid.Value -eq "2.5.29.17" }
    $hasServerEku = $certificate.Extensions | Where-Object {
        $_ -is [System.Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension] -and
        ($_.EnhancedKeyUsages | Where-Object { $_.Value -eq "1.3.6.1.5.5.7.3.1" })
    }
    if (-not $hasSan -or -not $hasServerEku) {
        throw "Certificate lacks the required Subject Alternative Name or TLS Server Authentication EKU."
    }

    if (-not $ValidateOnly) {
        $principal = [Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
        if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
            throw "Run this script from an elevated PowerShell session."
        }

        Import-Certificate -FilePath $resolvedPath -CertStoreLocation "Cert:\LocalMachine\Root" | Out-Null
        Write-Host "Trusted the verified public GameNet Server certificate in LocalMachine\Root."
    }
    else {
        Write-Host "ValidateOnly passed; no certificate trust-store changes were made."
    }

    Write-Host "Verified public certificate SHA-256 fingerprint: $actual"
    Write-Host "The configured ServerBaseUrl host must match one of the certificate SAN entries."
}
finally {
    $certificate.Dispose()
}
