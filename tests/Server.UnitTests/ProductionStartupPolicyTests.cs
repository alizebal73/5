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

    [Fact]
    public void Production_startup_rejects_disabled_protected_settings()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            ProductionStartupPolicy.EnsureProtectedSettingsEnabled(
                "Production", protectedSettingsEnabled: false));

        Assert.Equal(
            "Production requires DPAPI-protected Server settings to be enabled.",
            exception.Message);
    }

    [Fact]
    public void Production_startup_accepts_enabled_protected_settings()
    {
        ProductionStartupPolicy.EnsureProtectedSettingsEnabled(
            "Production", protectedSettingsEnabled: true);
    }

    [Fact]
    public void Non_production_hosts_may_disable_protected_settings_for_tests()
    {
        ProductionStartupPolicy.EnsureProtectedSettingsEnabled(
            "Development", protectedSettingsEnabled: false);
    }

    [Fact]
    public void Production_startup_rejects_protected_settings_path_override()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            ProductionStartupPolicy.EnsureProtectedSettingsPathOverrideAllowed(
                "Production", @"C:\Temp\alternate-protected-settings.bin"));

        Assert.Equal(
            "Production protected Server settings must use the canonical ProgramData path.",
            exception.Message);
    }

    [Fact]
    public void Development_test_hosts_may_use_an_explicit_protected_settings_path()
    {
        ProductionStartupPolicy.EnsureProtectedSettingsPathOverrideAllowed(
            "Development", @"C:\Temp\test-protected-settings.bin");
    }
}
