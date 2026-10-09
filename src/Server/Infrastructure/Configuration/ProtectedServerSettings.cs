using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace GameNet.Server.Infrastructure.Configuration;

public static class ProtectedServerSettings
{
    public const string PathEnvironmentVariableName = "GAMENET_PROTECTED_SETTINGS_FILE";
    public const string DefaultFileName = "server-secrets.bin";

    private static readonly HashSet<string> AllowedKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "GameNet:DatabaseConnectionString",
        "GameNet:Authentication:Enabled",
        "GameNet:Authentication:Issuer",
        "GameNet:Authentication:Audience",
        "GameNet:Authentication:SigningKey",
        "GameNet:Agent:ProvisioningKey",
        "GameNet:Setup:BootstrapSecret"
    };

    public static string GetDefaultPath() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "GameNet Manager",
            "Config",
            DefaultFileName);

    public static void LoadInto(
        IConfigurationManager configuration,
        bool enableDefaultProtectedFile,
        string? explicitFilePath = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var overridePath = explicitFilePath;
        if (string.IsNullOrWhiteSpace(overridePath))
            overridePath = Environment.GetEnvironmentVariable(PathEnvironmentVariableName);

        string path;
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            path = Path.GetFullPath(overridePath);
            if (!File.Exists(path))
                throw new FileNotFoundException("The explicitly configured protected Server settings file was not found.", path);
        }
        else
        {
            if (!enableDefaultProtectedFile)
                return;

            path = GetDefaultPath();
            if (!File.Exists(path))
                throw new FileNotFoundException("Protected Server settings are enabled, but the protected settings file is missing.", path);
        }

        var settings = Read(path);
        configuration.AddInMemoryCollection(settings);
    }

    public static IReadOnlyDictionary<string, string?> Read(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!File.Exists(path))
            throw new FileNotFoundException("Protected Server settings file was not found.", path);

        byte[] clearBytes;
        try
        {
            var protectedBytes = File.ReadAllBytes(path);
            clearBytes = ProtectedData.Unprotect(protectedBytes, optionalEntropy: null, DataProtectionScope.LocalMachine);
            CryptographicOperations.ZeroMemory(protectedBytes);
        }
        catch (CryptographicException exception)
        {
            throw new InvalidOperationException("Protected Server settings could not be decrypted on this machine.", exception);
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<Dictionary<string, string?>>(clearBytes);
            if (parsed is null || parsed.Count == 0)
                throw new InvalidOperationException("Protected Server settings are empty or invalid JSON.");

            var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in parsed)
            {
                if (!AllowedKeys.Contains(pair.Key))
                    throw new InvalidOperationException($"Protected Server settings contain an unsupported key: {pair.Key}.");
                if (pair.Value is null)
                    throw new InvalidOperationException($"Protected Server setting '{pair.Key}' cannot be null.");
                if (!result.TryAdd(pair.Key, pair.Value))
                    throw new InvalidOperationException($"Protected Server settings contain a duplicate key: {pair.Key}.");
            }

            RequireNonBlank(result, "GameNet:DatabaseConnectionString");
            RequireNonBlank(result, "GameNet:Authentication:Issuer");
            RequireNonBlank(result, "GameNet:Authentication:Audience");
            RequireNonBlank(result, "GameNet:Authentication:SigningKey");
            RequireNonBlank(result, "GameNet:Agent:ProvisioningKey");

            if (!bool.TryParse(result["GameNet:Authentication:Enabled"], out var enabled) || !enabled)
                throw new InvalidOperationException("Protected Server settings must enable authentication.");
            if (result["GameNet:Authentication:SigningKey"]!.Length < 32)
                throw new InvalidOperationException("Protected Server authentication signing key must contain at least 32 characters.");
            if (result["GameNet:Agent:ProvisioningKey"]!.Length < 32)
                throw new InvalidOperationException("Protected Server Agent provisioning key must contain at least 32 characters.");

            return result;
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("Protected Server settings are not valid JSON.", exception);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(clearBytes);
        }
    }

    private static void RequireNonBlank(
        IReadOnlyDictionary<string, string?> settings,
        string key)
    {
        if (!settings.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException($"Required protected Server setting '{key}' is missing.");
    }
}
