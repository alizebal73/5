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
    public void Enabled_authentication_requires_a_database_connection()
    {
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

        Assert.False(result.Succeeded);
    }
}
