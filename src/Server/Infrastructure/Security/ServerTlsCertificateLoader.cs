using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace GameNet.Server.Infrastructure.Security;

public static class ServerTlsCertificateLoader
{
    private const string ServerAuthenticationOid = "1.3.6.1.5.5.7.3.1";
    private const string SubjectAlternativeNameOid = "2.5.29.17";

    public static X509Certificate2 LoadFromLocalMachine(string thumbprint)
    {
        var normalized = thumbprint.Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("\t", string.Empty, StringComparison.Ordinal)
            .ToUpperInvariant();

        if (normalized.Length != 40 || normalized.Any(character => !Uri.IsHexDigit(character)))
            throw new InvalidOperationException("Server TLS certificate thumbprint must be a 40-character SHA-1 certificate thumbprint.");

        using var store = new X509Store(StoreName.My, StoreLocation.LocalMachine);
        store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);

        var matches = store.Certificates.Find(
            X509FindType.FindByThumbprint,
            normalized,
            validOnly: true);

        if (matches.Count != 1)
            throw new InvalidOperationException("A single valid Server TLS certificate was not found in LocalMachine\\My.");

        var certificate = matches[0];
        if (!certificate.HasPrivateKey)
            throw new InvalidOperationException("The Server TLS certificate does not have an accessible private key.");

        var serverAuthentication = certificate.Extensions
            .OfType<X509EnhancedKeyUsageExtension>()
            .SelectMany(extension => extension.EnhancedKeyUsages.Cast<Oid>())
            .Any(oid => string.Equals(oid.Value, ServerAuthenticationOid, StringComparison.Ordinal));

        if (!serverAuthentication)
            throw new InvalidOperationException("The Server TLS certificate is not valid for TLS server authentication.");

        var hasSubjectAlternativeName = certificate.Extensions
            .Cast<X509Extension>()
            .Any(extension => string.Equals(extension.Oid?.Value, SubjectAlternativeNameOid, StringComparison.Ordinal));

        if (!hasSubjectAlternativeName)
            throw new InvalidOperationException("The Server TLS certificate must contain a Subject Alternative Name.");

        return certificate;
    }
}
