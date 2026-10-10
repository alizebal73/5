using Microsoft.Extensions.Hosting;

namespace GameNet.Server.Infrastructure.Security;

internal static class ProductionStartupPolicy
{
    internal static void EnsureAuthenticationEnabled(string environmentName, bool authenticationEnabled)
    {
        if (string.Equals(environmentName, Environments.Production, StringComparison.OrdinalIgnoreCase) &&
            !authenticationEnabled)
        {
            throw new InvalidOperationException("Production authentication must be enabled.");
        }
    }

    internal static void EnsureProtectedSettingsEnabled(string environmentName, bool protectedSettingsEnabled)
    {
        if (string.Equals(environmentName, Environments.Production, StringComparison.OrdinalIgnoreCase) &&
            !protectedSettingsEnabled)
        {
            throw new InvalidOperationException(
                "Production requires DPAPI-protected Server settings to be enabled.");
        }
    }

    internal static void EnsureProtectedSettingsPathOverrideAllowed(string environmentName, string? pathOverride)
    {
        if (string.Equals(environmentName, Environments.Production, StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(pathOverride))
        {
            throw new InvalidOperationException(
                "Production protected Server settings must use the canonical ProgramData path.");
        }
    }
}
