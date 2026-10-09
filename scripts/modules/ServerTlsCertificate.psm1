$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Test-GameNetServerTlsCertificateProfile {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][System.Security.Cryptography.X509Certificates.X509Certificate2]$Certificate,
        [Parameter(Mandatory)][System.Net.IPAddress]$ServerAddress
    )

    $sanExtension = @($Certificate.Extensions | Where-Object { $_.Oid.Value -eq "2.5.29.17" } | Select-Object -First 1)[0]
    if ($null -eq $sanExtension -or
        $sanExtension.Format($true) -notmatch [regex]::Escape($ServerAddress.ToString())) {
        throw "Server TLS certificate SAN does not match the requested IP address."
    }

    $ekuExtension = @($Certificate.Extensions | Where-Object { $_.Oid.Value -eq "2.5.29.37" } | Select-Object -First 1)[0]
    $hasServerAuthentication = $false
    if ($null -ne $ekuExtension) {
        $ekuOids = ([System.Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension]$ekuExtension).EnhancedKeyUsages
        foreach ($oid in $ekuOids) {
            if ($oid.Value -eq "1.3.6.1.5.5.7.3.1") {
                $hasServerAuthentication = $true
            }
        }
    }

    if (-not $hasServerAuthentication) {
        throw "Server TLS certificate is missing the Server Authentication EKU."
    }

    if ($Certificate.NotBefore.ToUniversalTime() -gt [DateTime]::UtcNow.AddMinutes(5) -or
        $Certificate.NotAfter.ToUniversalTime() -le [DateTime]::UtcNow.AddDays(1)) {
        throw "Server TLS certificate validity period is missing or too short."
    }
}

function New-GameNetServerTlsCertificate {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][System.Net.IPAddress]$ServerAddress,
        [Parameter(Mandatory)][string]$PfxPath,
        [Parameter(Mandatory)][string]$PublicCertificatePath,
        [Parameter(Mandatory)][System.Security.SecureString]$Password,
        [Parameter()][ValidateSet("Cert:\CurrentUser\My", "Cert:\LocalMachine\My")][string]$CertificateStoreLocation = "Cert:\LocalMachine\My"
    )

    if ($ServerAddress.AddressFamily -ne [System.Net.Sockets.AddressFamily]::InterNetwork) {
        throw "The Server TLS certificate address must be IPv4."
    }
    if ($Password.Length -lt 32) {
        throw "The Server TLS PFX password must contain at least 32 characters."
    }

    $pfxFullPath = [System.IO.Path]::GetFullPath($PfxPath)
    $publicFullPath = [System.IO.Path]::GetFullPath($PublicCertificatePath)
    if ([string]::Equals($pfxFullPath, $publicFullPath, [StringComparison]::OrdinalIgnoreCase)) {
        throw "The private PFX and public certificate must use different file paths."
    }
    if ((Test-Path -LiteralPath $pfxFullPath) -or (Test-Path -LiteralPath $publicFullPath)) {
        throw "Server TLS certificate output already exists. Refusing to overwrite existing certificate material."
    }

    $pfxDirectory = Split-Path -Parent $pfxFullPath
    $publicDirectory = Split-Path -Parent $publicFullPath
    New-Item -ItemType Directory -Force -Path $pfxDirectory,$publicDirectory | Out-Null

    $certificate = $null
    $publicCertificate = $null
    $pfxCertificate = $null
    $plainPassword = $null
    $pfxCreated = $false
    $publicCreated = $false
    try {
        $certificateArguments = @{
            Type = "SSLServerAuthentication"
            Subject = "CN=GameNet Server"
            TextExtension = @("2.5.29.17={text}IPAddress=$($ServerAddress.ToString())")
            KeyAlgorithm = "RSA"
            KeyLength = 3072
            HashAlgorithm = "SHA256"
            KeyExportPolicy = "Exportable"
            CertStoreLocation = $CertificateStoreLocation
            NotAfter = (Get-Date).AddYears(2)
        }
        $certificate = New-SelfSignedCertificate @certificateArguments

        if ($null -eq $certificate -or -not $certificate.HasPrivateKey) {
            throw "Could not create a Server TLS certificate with a private key."
        }
        Test-GameNetServerTlsCertificateProfile -Certificate $certificate -ServerAddress $ServerAddress

        $pfxCreated = $true
        Export-PfxCertificate -Cert $certificate -FilePath $pfxFullPath -Password $Password | Out-Null
        $publicCreated = $true
        Export-Certificate -Cert $certificate -FilePath $publicFullPath -Type CERT | Out-Null

        if (-not (Test-Path -LiteralPath $pfxFullPath -PathType Leaf) -or
            (Get-Item -LiteralPath $pfxFullPath).Length -lt 1024) {
            throw "Server TLS private certificate export is missing or unexpectedly small."
        }

        $publicCertificate = [System.Security.Cryptography.X509Certificates.X509Certificate2]::new($publicFullPath)
        if ($publicCertificate.HasPrivateKey -or $publicCertificate.Thumbprint -ne $certificate.Thumbprint) {
            throw "The public certificate export does not match the generated certificate or unexpectedly includes a private key."
        }
        Test-GameNetServerTlsCertificateProfile -Certificate $publicCertificate -ServerAddress $ServerAddress

        $plainPassword = ([System.Net.NetworkCredential]::new("", $Password)).Password
        $pfxCertificate = [System.Security.Cryptography.X509Certificates.X509Certificate2]::new(
            $pfxFullPath,
            $plainPassword,
            [System.Security.Cryptography.X509Certificates.X509KeyStorageFlags]::EphemeralKeySet)
        if (-not $pfxCertificate.HasPrivateKey -or $pfxCertificate.Thumbprint -ne $certificate.Thumbprint) {
            throw "The exported private PFX could not be reopened or did not match the generated certificate."
        }
        Test-GameNetServerTlsCertificateProfile -Certificate $pfxCertificate -ServerAddress $ServerAddress

        return [pscustomobject]@{
            Thumbprint = $certificate.Thumbprint
            ExpiresUtc = $certificate.NotAfter.ToUniversalTime().ToString("O")
            Sha256Fingerprint = (Get-FileHash -LiteralPath $publicFullPath -Algorithm SHA256).Hash.ToUpperInvariant()
            PfxPath = $pfxFullPath
            PublicCertificatePath = $publicFullPath
        }
    }
    catch {
        if ($pfxCreated -and (Test-Path -LiteralPath $pfxFullPath)) {
            Remove-Item -LiteralPath $pfxFullPath -Force -ErrorAction SilentlyContinue
        }
        if ($publicCreated -and (Test-Path -LiteralPath $publicFullPath)) {
            Remove-Item -LiteralPath $publicFullPath -Force -ErrorAction SilentlyContinue
        }
        throw
    }
    finally {
        if ($null -ne $certificate) {
            $storeCertificatePath = Join-Path $CertificateStoreLocation $certificate.Thumbprint
            if (Test-Path -LiteralPath $storeCertificatePath) {
                Remove-Item -LiteralPath $storeCertificatePath -Force -ErrorAction SilentlyContinue
            }
        }
        if ($null -ne $pfxCertificate) { $pfxCertificate.Dispose() }
        if ($null -ne $publicCertificate) { $publicCertificate.Dispose() }
        if ($null -ne $certificate) { $certificate.Dispose() }
        $plainPassword = $null
    }
}

Export-ModuleMember -Function New-GameNetServerTlsCertificate
