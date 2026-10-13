using System.Security.Cryptography;

namespace GameNet.Server.Infrastructure.Security;

public interface IDatabaseConnectionSecret
{
    string ConnectionString { get; }
}

public interface IJwtSigningKeySecret
{
    string SigningKey { get; }
}

public interface IAgentProvisioningKeySecret
{
    string ProvisioningKey { get; }
}

public interface IIdempotencyFingerprintSecret
{
    string FingerprintKey { get; }
}

internal sealed class ServerSecretMaterial :
    IDatabaseConnectionSecret,
    IJwtSigningKeySecret,
    IAgentProvisioningKeySecret,
    IIdempotencyFingerprintSecret
{
    private ServerSecretMaterial(
        string connectionString,
        string signingKey,
        string provisioningKey,
        string fingerprintKey)
    {
        ConnectionString = connectionString;
        SigningKey = signingKey;
        ProvisioningKey = provisioningKey;
        FingerprintKey = fingerprintKey;
    }

    public string ConnectionString { get; }
    public string SigningKey { get; }
    public string ProvisioningKey { get; }
    public string FingerprintKey { get; }

    // The optional final argument preserves compatibility with pre-release/test
    // stores that only have the original three secrets. New provisioning always
    // supplies an independently generated fingerprint key.
    internal static ServerSecretMaterial Create(
        string? connectionString,
        string? signingKeyBase64,
        string? provisioningKeyBase64,
        string? idempotencyFingerprintKeyBase64 = null)
    {
        if (string.IsNullOrWhiteSpace(connectionString) || connectionString.Length > 16_384)
            throw new ServerSecretStoreException("Required Server secret material is missing or invalid.");

        ValidateBase64Key(signingKeyBase64);
        ValidateBase64Key(provisioningKeyBase64);

        // Legacy stores deliberately retain the old fingerprint root so the same
        // idempotency records remain replay-compatible. Such stores must not rotate
        // the JWT signing key until re-provisioned with a dedicated fingerprint key.
        var fingerprintKey = string.IsNullOrWhiteSpace(idempotencyFingerprintKeyBase64)
            ? signingKeyBase64
            : idempotencyFingerprintKeyBase64;
        ValidateBase64Key(fingerprintKey);

        return new ServerSecretMaterial(
            connectionString,
            signingKeyBase64!,
            provisioningKeyBase64!,
            fingerprintKey!);
    }

    private static void ValidateBase64Key(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 4096)
            throw new ServerSecretStoreException("Required Server secret material is missing or invalid.");

        byte[]? decoded = null;
        try
        {
            decoded = Convert.FromBase64String(value);
            if (decoded.Length < 32)
                throw new ServerSecretStoreException("Required Server secret material is missing or invalid.");
        }
        catch (FormatException)
        {
            throw new ServerSecretStoreException("Required Server secret material is missing or invalid.");
        }
        finally
        {
            if (decoded is not null)
                CryptographicOperations.ZeroMemory(decoded);
        }
    }
}

public sealed class ServerSecretStoreException(string message) : InvalidOperationException(message);
