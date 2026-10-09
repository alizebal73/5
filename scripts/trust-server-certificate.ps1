[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$CertificatePath,

    [Parameter(Mandatory = $true)]
    [string]$ExpectedThumbprint
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$principal = [Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw "Run this script from an elevated PowerShell session."
}
if (-not (Test-Path -LiteralPath $CertificatePath -PathType Leaf)) {
    throw "Public certificate file was not found: $CertificatePath"
}

$expected = ($ExpectedThumbprint -replace '\s', '').ToUpperInvariant()
if ($expected -notmatch '^[0-9A-F]{40}$') {
    throw "ExpectedThumbprint must be a 40-character SHA-1 thumbprint."
}

$certificate = [System.Security.Cryptography.X509Certificates.X509Certificate2]::new((Resolve-Path -LiteralPath $CertificatePath).Path)
try {
    if ($certificate.HasPrivateKey) {
        throw "Refusing to import a certificate that contains a private key. Copy only the exported .cer file."
    }

    if (($certificate.Thumbprint -replace '\s', '').ToUpperInvariant() -ne $expected) {
        throw "Certificate thumbprint does not match the thumbprint independently verified from the Server PC."
    }

    $hasSan = $certificate.Extensions | Where-Object { $_.Oid.Value -eq "2.5.29.17" }
    $hasServerEku = $certificate.Extensions | Where-Object {
        $_ -is [System.Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension] -and
        ($_.EnhancedKeyUsages | Where-Object { $_.Value -eq "1.3.6.1.5.5.7.3.1" })
    }
    if (-not $hasSan -or -not $hasServerEku) {
        throw "Certificate lacks the required Subject Alternative Name or TLS Server Authentication EKU."
    }

    Import-Certificate -FilePath (Resolve-Path -LiteralPath $CertificatePath).Path -CertStoreLocation "Cert:\LocalMachine\Root" | Out-Null
    Write-Host "Trusted the verified public GameNet Server certificate in LocalMachine\Root."
    Write-Host "The configured ServerBaseUrl host must match one of the certificate SAN entries."
}
finally {
    $certificate.Dispose()
}
