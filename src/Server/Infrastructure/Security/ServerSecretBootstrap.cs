using System.Collections;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace GameNet.Server.Infrastructure.Security;

internal static class ServerSecretBootstrap
{
    private const string DevelopmentSecretOptIn = "GAMENET_ALLOW_UNPROTECTED_TEST_SECRETS";

    private static readonly HashSet<string> SecretConfigurationKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "GameNet:DatabaseConnectionString",
        "GameNet:Authentication:SigningKey",
        "GameNet:Agent:ProvisioningKey",
        "GAMENET_DATABASE_CONNECTION"
    };

    internal static ServerSecretMaterial Load(
        IHostEnvironment environment,
        IConfiguration configuration,
        IReadOnlyList<string> commandLineArguments)
    {
        if (!OperatingSystem.IsWindows())
            throw new ServerSecretStoreException("The protected Server secret store requires Windows DPAPI.");

        var variables = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables(EnvironmentVariableTarget.Process))
        {
            if (entry.Key is string key)
                variables[key] = entry.Value as string;
        }

        return Resolve(
            environment.EnvironmentName,
            configuration,
            variables,
            commandLineArguments,
            DpapiServerSecretStore.LoadDefault);
    }

    internal static ServerSecretMaterial Resolve(
        string environmentName,
        IConfiguration configuration,
        IReadOnlyDictionary<string, string?> processEnvironment,
        IReadOnlyList<string> commandLineArguments,
        Func<ServerSecretMaterial> loadProtectedStore)
    {
        RejectSecretCommandLineArguments(commandLineArguments);

        var isDevelopment = string.Equals(
            environmentName,
            Environments.Development,
            StringComparison.OrdinalIgnoreCase);

        if (isDevelopment && string.Equals(
                FindEnvironmentValue(processEnvironment, DevelopmentSecretOptIn),
                "true",
                StringComparison.OrdinalIgnoreCase))
        {
            return ServerSecretMaterial.Create(
                FindEnvironmentValue(processEnvironment, "GAMENET_DATABASE_CONNECTION"),
                FindEnvironmentValue(processEnvironment, "GameNet:Authentication:SigningKey"),
                FindEnvironmentValue(processEnvironment, "GameNet:Agent:ProvisioningKey"));
        }

        RejectSecretEnvironmentOverrides(processEnvironment);
        RejectSecretConfigurationValues(configuration);
        return loadProtectedStore();
    }

    private static string? FindEnvironmentValue(
        IReadOnlyDictionary<string, string?> environment,
        string key)
    {
        foreach (var pair in environment)
        {
            if (string.Equals(NormalizeEnvironmentKey(pair.Key), key, StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(pair.Value))
                return pair.Value;
        }

        return null;
    }

    private static string NormalizeEnvironmentKey(string key)
    {
        var normalized = key;
        if (normalized.StartsWith("DOTNET_", StringComparison.OrdinalIgnoreCase))
            normalized = normalized["DOTNET_".Length..];
        else if (normalized.StartsWith("ASPNETCORE_", StringComparison.OrdinalIgnoreCase))
            normalized = normalized["ASPNETCORE_".Length..];

        return normalized.Replace("__", ":", StringComparison.Ordinal);
    }

    private static void RejectSecretEnvironmentOverrides(IReadOnlyDictionary<string, string?> environment)
    {
        foreach (var pair in environment)
        {
            if (!string.IsNullOrWhiteSpace(pair.Value) &&
                SecretConfigurationKeys.Contains(NormalizeEnvironmentKey(pair.Key)))
                throw new ServerSecretStoreException(
                    "Secret environment overrides are disabled unless the explicit Development test seam is enabled.");
        }
    }

    private static void RejectSecretCommandLineArguments(IReadOnlyList<string> arguments)
    {
        foreach (var argument in arguments)
        {
            var candidate = argument.TrimStart('-', '/');
            var separator = candidate.IndexOf('=');
            var key = (separator < 0 ? candidate : candidate[..separator])
                .Replace("__", ":", StringComparison.Ordinal);

            if (SecretConfigurationKeys.Contains(key))
                throw new ServerSecretStoreException("Secret command-line overrides are not supported.");
        }
    }

    private static void RejectSecretConfigurationValues(IConfiguration configuration)
    {
        foreach (var key in SecretConfigurationKeys)
        {
            if (!string.IsNullOrWhiteSpace(configuration[key]))
                throw new ServerSecretStoreException(
                    "Secret values must not be supplied through ordinary application configuration.");
        }
    }
}
