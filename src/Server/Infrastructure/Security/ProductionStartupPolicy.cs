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
}
