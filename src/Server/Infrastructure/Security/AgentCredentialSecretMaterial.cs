using System.Security.Cryptography;
using System.Text;

namespace GameNet.Server.Infrastructure.Security;

internal static class AgentCredentialSecretMaterial
{
    // A 32-byte value encoded as unpadded Base64URL has 43 characters. Since only
    // four data bits remain in the final Base64 sextet, the last character's low
    // two bits must be zero. Enforce that canonical representation as well as the
    // alphabet so alternate/non-canonical strings are rejected before hashing.
    private const string CanonicalFinalCharacters = "AEIMQUYcgkosw048";

    internal static string Generate()
    {
        var randomBytes = RandomNumberGenerator.GetBytes(32);
        try
        {
            return Convert.ToBase64String(randomBytes)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
        }
        finally
        {
            CryptographicOperations.ZeroMemory(randomBytes);
        }
    }

    internal static string Hash(string secret)
    {
        ArgumentNullException.ThrowIfNull(secret);
        var bytes = Encoding.UTF8.GetBytes(secret);
        byte[]? digest = null;
        try
        {
            digest = SHA256.HashData(bytes);
            return Convert.ToHexString(digest);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
            if (digest is not null)
                CryptographicOperations.ZeroMemory(digest);
        }
    }

    internal static bool IsCanonicalSecret(string? secret) =>
        secret is { Length: 43 } &&
        secret.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_') &&
        CanonicalFinalCharacters.Contains(secret[^1]);

    internal static string HashEnrollmentToken(string token)
    {
        if (!IsCanonicalSecret(token))
            throw new ArgumentException("Enrollment token format is invalid.", nameof(token));

        return Hash(token);
    }
}
