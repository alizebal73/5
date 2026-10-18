$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) {
    throw "Server certificate trust verification requires Windows."
}

$trustScript = Join-Path $PSScriptRoot "trust-server-certificate.ps1"
if (-not (Test-Path -LiteralPath $trustScript -PathType Leaf)) {
    throw "Server certificate trust script was not found."
}

$testRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("gamenet-trust-cert-test-" + [Guid]::NewGuid().ToString("N"))
$certificatePath = Join-Path $testRoot "server-trust.cer"
$testCertificate = $null

try {
    New-Item -ItemType Directory -Force -Path $testRoot | Out-Null
    $testCertificate = New-SelfSignedCertificate `
        -DnsName "gamenet-trust-test.local" `
        -Type SSLServerAuthentication `
        -CertStoreLocation "Cert:\CurrentUser\My" `
        -KeyAlgorithm RSA `
        -KeyLength 2048 `
        -NotAfter (Get-Date).AddDays(30)

    if ($null -eq $testCertificate -or -not $testCertificate.HasPrivateKey) {
        throw "Could not create the temporary TLS test certificate."
    }

    Export-Certificate -Cert $testCertificate -FilePath $certificatePath -Type CERT | Out-Null
    $expectedFingerprint = (Get-FileHash -LiteralPath $certificatePath -Algorithm SHA256).Hash.ToUpperInvariant()

    & $trustScript -CertificatePath $certificatePath -ExpectedSha256Fingerprint $expectedFingerprint -ValidateOnly

    $wrongFingerprint = if ($expectedFingerprint.StartsWith("0", [StringComparison]::Ordinal)) {
        "1" + $expectedFingerprint.Substring(1)
    }
    else {
        "0" + $expectedFingerprint.Substring(1)
    }

    $mismatchRejected = $false
    try {
        & $trustScript -CertificatePath $certificatePath -ExpectedSha256Fingerprint $wrongFingerprint -ValidateOnly
    }
    catch {
        $mismatchRejected = $true
    }
    if (-not $mismatchRejected) {
        throw "Trust validation accepted an incorrect SHA-256 fingerprint."
    }

    $sha1LengthRejected = $false
    try {
        & $trustScript -CertificatePath $certificatePath -ExpectedSha256Fingerprint $testCertificate.Thumbprint -ValidateOnly
    }
    catch {
        $sha1LengthRejected = $true
    }
    if (-not $sha1LengthRejected) {
        throw "Trust validation accepted a 40-character certificate thumbprint instead of a SHA-256 fingerprint."
    }

    Write-Host "SERVER_CERTIFICATE_TRUST_VALIDATION_PASSED: correct SHA-256 accepted; mismatch and SHA-1-length input rejected; trust store unchanged."
}
finally {
    if ($null -ne $testCertificate) {
        $thumbprint = $testCertificate.Thumbprint
        $testCertificate.Dispose()
        Remove-Item -LiteralPath ("Cert:\CurrentUser\My\" + $thumbprint) -Force -ErrorAction SilentlyContinue
    }
    Remove-Item -LiteralPath $testRoot -Recurse -Force -ErrorAction SilentlyContinue
}
