$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Test-GameNetServerTlsCertificateProfile {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [System.Security.Cryptography.X509Certificates.X509Certificate2]$Certificate,
        [Parameter(Mandatory = $true)]
        [System.Net.IPAddress]$ServerAddress,
        [Parameter(Mandatory = $true)]
        [string]$DnsName
    )

    $sanExtension = @($Certificate.Extensions | Where-Object { $_.Oid.Value -eq "2.5.29.17" } | Select-Object -First 1)[0]
    if ($null -eq $sanExtension) {
        throw "Server TLS certificate is missing its Subject Alternative Name extension."
    }
    $sanText = $sanExtension.Format($true)
    if ($sanText -notmatch [regex]::Escape($ServerAddress.ToString()) -or $sanText -notmatch [regex]::Escape($DnsName)) {
        throw "Server TLS certificate SAN does not match both the requested Server IP address and DNS name."
    }

    $ekuExtensions = @($Certificate.Extensions | Where-Object { $_.Oid.Value -eq "2.5.29.37" })
    $hasServerAuthentication = $false
    foreach ($ekuExtension in $ekuExtensions) {
        if ($ekuExtension -is [System.Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension]) {
            foreach ($oid in $ekuExtension.EnhancedKeyUsages) {
                if ($oid.Value -eq "1.3.6.1.5.5.7.3.1") { $hasServerAuthentication = $true }
            }
        }
    }
    if (-not $hasServerAuthentication) {
        throw "Server TLS certificate is missing the Server Authentication EKU."
    }

    if ($Certificate.NotBefore.ToUniversalTime() -gt [DateTime]::UtcNow.AddMinutes(5) -or $Certificate.NotAfter.ToUniversalTime() -le [DateTime]::UtcNow.AddDays(1)) {
        throw "Server TLS certificate validity period is missing or too short."
    }
}

function New-GameNetServerTlsCertificate {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [System.Net.IPAddress]$ServerAddress,
        [Parameter(Mandatory = $true)]
        [string]$DnsName,
        [Parameter(Mandatory = $true)]
        [string]$PublicCertificatePath,
        [Parameter()]
        [ValidateSet("Cert:\CurrentUser\My", "Cert:\LocalMachine\My")]
        [string]$CertificateStoreLocation = "Cert:\LocalMachine\My"
    )

    if ($ServerAddress.AddressFamily -ne [System.Net.Sockets.AddressFamily]::InterNetwork) {
        throw "The Server TLS certificate address must be IPv4."
    }
    if ([string]::IsNullOrWhiteSpace($DnsName) -or [Uri]::CheckHostName($DnsName) -ne [UriHostNameType]::Dns) {
        throw "DnsName must be a valid DNS host name without a scheme, path or port."
    }

    $publicFullPath = [System.IO.Path]::GetFullPath($PublicCertificatePath)
    if (Test-Path -LiteralPath $publicFullPath) {
        throw "The public certificate output already exists. Refusing to overwrite existing certificate material."
    }
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $publicFullPath) | Out-Null

    $certificate = $null
    $publicCertificate = $null
    $createdCertificate = $false
    $createdPublicCertificate = $false
    try {
        $certificate = New-SelfSignedCertificate -Subject "CN=$DnsName" -CertStoreLocation $CertificateStoreLocation -KeyAlgorithm RSA -KeyLength 3072 -HashAlgorithm SHA256 -KeyExportPolicy NonExportable -KeyUsage DigitalSignature,KeyEncipherment -Type Custom -TextExtension @("2.5.29.17={text}DNS=$DnsName&IPAddress=$($ServerAddress.IPAddressToString)","2.5.29.37={text}1.3.6.1.5.5.7.3.1") -NotAfter (Get-Date).AddYears(3)
        if ($null -eq $certificate -or -not $certificate.HasPrivateKey) {
            throw "Could not create a Server TLS certificate with a private key."
        }
        $createdCertificate = $true
        Test-GameNetServerTlsCertificateProfile -Certificate $certificate -ServerAddress $ServerAddress -DnsName $DnsName

        Export-Certificate -Cert $certificate -FilePath $publicFullPath -Type CERT | Out-Null
        $createdPublicCertificate = $true
        $publicCertificate = [System.Security.Cryptography.X509Certificates.X509Certificate2]::new($publicFullPath)
        if ($publicCertificate.HasPrivateKey -or $publicCertificate.Thumbprint -ne $certificate.Thumbprint) {
            throw "The exported public certificate does not match the generated certificate or unexpectedly contains a private key."
        }
        Test-GameNetServerTlsCertificateProfile -Certificate $publicCertificate -ServerAddress $ServerAddress -DnsName $DnsName

        return [pscustomobject]@{
            Thumbprint = $certificate.Thumbprint
            ExpiresUtc = $certificate.NotAfter.ToUniversalTime().ToString("O")
            Sha256Fingerprint = (Get-FileHash -LiteralPath $publicFullPath -Algorithm SHA256).Hash.ToUpperInvariant()
            PublicCertificatePath = $publicFullPath
            CertificateStoreLocation = $CertificateStoreLocation
        }
    }
    catch {
        if ($createdPublicCertificate -and (Test-Path -LiteralPath $publicFullPath -PathType Leaf)) {
            Remove-Item -LiteralPath $publicFullPath -Force -ErrorAction SilentlyContinue
        }
        if ($createdCertificate -and $null -ne $certificate) {
            Remove-Item -LiteralPath (Join-Path $CertificateStoreLocation $certificate.Thumbprint) -Force -ErrorAction SilentlyContinue
        }
        throw
    }
    finally {
        if ($null -ne $publicCertificate) { $publicCertificate.Dispose() }
        if ($null -ne $certificate) { $certificate.Dispose() }
    }
}

Export-ModuleMember -Function Test-GameNetServerTlsCertificateProfile, New-GameNetServerTlsCertificate
