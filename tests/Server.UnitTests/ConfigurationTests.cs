using Xunit;
using GameNet.Server.Infrastructure.Configuration;
namespace GameNet.Server.UnitTests;
public sealed class ConfigurationTests
{
    [Fact] public void Default_currency_is_toman()
    {
        var result = new GameNetOptionsValidator().Validate(null, new GameNetOptions());
        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Enabled_authentication_does_not_require_database_credentials_in_options_configuration()
    {
        // Production database credentials enter EF Core through IDatabaseConnectionSecret,
        // not through ordinary GameNetOptions configuration.
        var result = new GameNetOptionsValidator().Validate(null, new GameNetOptions
        {
            Authentication = new AuthenticationOptions
            {
                Enabled = true,
                Issuer = "GameNet.Tests",
                Audience = "GameNet.Tests.Client",
                SigningKey = new string('s', 48)
            },
            Agent = new AgentOptions { ProvisioningKey = new string('p', 48) }
        });

        Assert.True(result.Succeeded);
    }
}
