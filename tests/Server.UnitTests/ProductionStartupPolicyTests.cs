using GameNet.Server.Infrastructure.Security;
using Xunit;

namespace GameNet.Server.UnitTests;

public sealed class ProductionStartupPolicyTests
{
    [Fact]
    public void Production_startup_rejects_disabled_authentication()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            ProductionStartupPolicy.EnsureAuthenticationEnabled("Production", authenticationEnabled: false));

        Assert.Equal("Production authentication must be enabled.", exception.Message);
    }

    [Fact]
    public void Production_startup_accepts_enabled_authentication()
    {
        ProductionStartupPolicy.EnsureAuthenticationEnabled("Production", authenticationEnabled: true);
    }

    [Fact]
    public void Development_test_hosts_can_disable_authentication_for_migration_only()
    {
        ProductionStartupPolicy.EnsureAuthenticationEnabled("Development", authenticationEnabled: false);
    }
}
