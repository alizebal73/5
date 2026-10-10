using System.Security.Cryptography;
using System.Text;

namespace GameNet.Server.Infrastructure.Security;

internal static class AgentCredentialSecretMaterial
{
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

    internal static string HashEnrollmentToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length != 43 ||
            token.Any(ch => !(char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_')))
            throw new ArgumentException("Enrollment token format is invalid.", nameof(token));

        return Hash(token);
    }
}
