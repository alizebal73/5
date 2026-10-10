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

internal sealed class ServerSecretMaterial :
    IDatabaseConnectionSecret,
    IJwtSigningKeySecret,
    IAgentProvisioningKeySecret
{
    private ServerSecretMaterial(string connectionString, string signingKey, string provisioningKey)
    {
        ConnectionString = connectionString;
        SigningKey = signingKey;
        ProvisioningKey = provisioningKey;
    }

    public string ConnectionString { get; }
    public string SigningKey { get; }
    public string ProvisioningKey { get; }

    internal static ServerSecretMaterial Create(
        string? connectionString,
        string? signingKeyBase64,
        string? provisioningKeyBase64)
    {
        if (string.IsNullOrWhiteSpace(connectionString) || connectionString.Length > 16_384)
            throw new ServerSecretStoreException("Required Server secret material is missing or invalid.");

        ValidateBase64Key(signingKeyBase64);
        ValidateBase64Key(provisioningKeyBase64);
        return new ServerSecretMaterial(connectionString, signingKeyBase64!, provisioningKeyBase64!);
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
