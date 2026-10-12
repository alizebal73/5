using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GameNet.Server.Infrastructure.Security;

namespace GameNet.Server.Infrastructure.Idempotency;

public sealed class HmacRequestFingerprint : IRequestFingerprint
{
    private const string KeyPurpose = "GameNet.Idempotency.RequestFingerprint.v1";
    private readonly byte[] key;

    public HmacRequestFingerprint(IJwtSigningKeySecret signingSecret)
    {
        ArgumentNullException.ThrowIfNull(signingSecret);
        key = DeriveKey(signingSecret.SigningKey);
    }

    public string Compute(object payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        var json = JsonSerializer.Serialize(payload);
        var digest = HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(json));
        return "hmac-sha256-v1:" + Convert.ToHexString(digest);
    }

    private static byte[] DeriveKey(string? rootSecret)
    {
        if (string.IsNullOrWhiteSpace(rootSecret) || Encoding.UTF8.GetByteCount(rootSecret) < 32)
            throw new InvalidOperationException("Request fingerprinting requires a configured 32+ character authentication signing secret.");

        // Purpose-specific derivation prevents the fingerprinting operation from reusing the raw JWT signing key.
        return HMACSHA256.HashData(Encoding.UTF8.GetBytes(rootSecret), Encoding.UTF8.GetBytes(KeyPurpose));
    }
}
