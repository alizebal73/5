using System.Security.Cryptography;
using GameNet.Server.Infrastructure.Security;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace GameNet.Server.UnitTests;

public sealed class ServerSecretBootstrapTests
{
    [Fact]
    public void Production_uses_protected_store()
    {
        var configuration = new ConfigurationBuilder().Build();
        var protectedMaterial = CreateMaterial();
        var called = false;

        var material = ServerSecretBootstrap.Resolve(
            "Production", configuration, new Dictionary<string, string?>(),
            Array.Empty<string>(), () => { called = true; return protectedMaterial; });

        Assert.True(called);
        Assert.Same(protectedMaterial, material);
    }

    [Fact]
    public void Production_rejects_secret_environment_overrides_without_echoing_values()
    {
        var secret = CreateKey();
        var environment = new Dictionary<string, string?>
        {
            ["DOTNET_GameNet__Authentication__SigningKey"] = secret
        };

        var exception = Assert.Throws<ServerSecretStoreException>(() =>
            ServerSecretBootstrap.Resolve(
                "Production", new ConfigurationBuilder().Build(), environment,
                Array.Empty<string>(), CreateMaterial));

        Assert.DoesNotContain(secret, exception.Message);
    }

    [Fact]
    public void Production_rejects_secret_command_line_arguments_without_echoing_values()
    {
        const string secret = "not-a-real-key";
        var exception = Assert.Throws<ServerSecretStoreException>(() =>
            ServerSecretBootstrap.Resolve(
                "Production", new ConfigurationBuilder().Build(),
                new Dictionary<string, string?>(),
                new[] { "--GameNet:Agent:ProvisioningKey=" + secret }, CreateMaterial));

        Assert.DoesNotContain(secret, exception.Message);
    }

    [Fact]
    public void Development_without_opt_in_uses_protected_store_not_environment_secret()
    {
        var called = false;
        var material = ServerSecretBootstrap.Resolve(
            "Development", new ConfigurationBuilder().Build(),
            new Dictionary<string, string?>(), Array.Empty<string>(),
            () => { called = true; return CreateMaterial(); });

        Assert.True(called);
        Assert.NotNull(material);
    }

    [Fact]
    public void Explicit_development_opt_in_uses_process_environment_values()
    {
        var connection = "Host=127.0.0.1;Database=gamenet_test;Username=test";
        var signingKey = CreateKey();
        var provisioningKey = CreateKey();
        var material = ServerSecretBootstrap.Resolve(
            "Development",
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["GameNet:DatabaseConnectionString"] = "Host=configuration-should-not-be-used"
            }).Build(),
            new Dictionary<string, string?>
            {
                ["GAMENET_ALLOW_UNPROTECTED_TEST_SECRETS"] = "true",
                ["GAMENET_DATABASE_CONNECTION"] = connection,
                ["GameNet__Authentication__SigningKey"] = signingKey,
                ["GameNet__Agent__ProvisioningKey"] = provisioningKey
            },
            Array.Empty<string>(),
            () => throw new InvalidOperationException("Protected store should not be called for explicit Development tests."));

        Assert.Equal(connection, material.ConnectionString);
        Assert.Equal(signingKey, material.SigningKey);
        Assert.Equal(provisioningKey, material.ProvisioningKey);
    }

    [Fact]
    public void Non_development_rejects_secret_configuration_values_without_echoing()
    {
        const string password = "not-a-real-password";
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["GameNet:DatabaseConnectionString"] = "Host=localhost;Password=" + password
            }).Build();

        var exception = Assert.Throws<ServerSecretStoreException>(() =>
            ServerSecretBootstrap.Resolve(
                "Staging", configuration, new Dictionary<string, string?>(),
                Array.Empty<string>(), CreateMaterial));

        Assert.DoesNotContain(password, exception.Message);
    }

    private static ServerSecretMaterial CreateMaterial() => ServerSecretMaterial.Create(
        "Host=127.0.0.1;Database=gamenet_test;Username=test", CreateKey(), CreateKey());

    private static string CreateKey() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
}
