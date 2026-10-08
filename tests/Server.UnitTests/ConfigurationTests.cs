using GameNet.Server.Infrastructure.Configuration;
namespace GameNet.Server.UnitTests;
public sealed class ConfigurationTests
{
    [Fact] public void Default_currency_is_toman()
    {
        var result = new GameNetOptionsValidator().Validate(null, new GameNetOptions());
        Assert.True(result.Succeeded);
    }
}
