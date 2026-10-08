using System.Text;
using GameNet.Server.Infrastructure.Configuration;
using GameNet.Server.Infrastructure.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace GameNet.Server.UnitTests;

public sealed class AuthenticationRegistrationTests
{
    [Fact]
    public void Bearer_scheme_uses_configured_issuer_audience_and_signing_key()
    {
        const string issuer = "GameNet.Foundation.UnitTests";
        const string audience = "GameNet.Agent.UnitTests";
        const string signingKey = "unit-test-signing-key-that-is-long-enough-for-hmac-sha256";

        var applicationOptions = new GameNetOptions
        {
            Authentication = new AuthenticationOptions
            {
                Enabled = true,
                Issuer = issuer,
                Audience = audience,
                SigningKey = signingKey
            }
        };

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IOptions<GameNetOptions>>(Options.Create(applicationOptions));
        services.AddGameNetAuthentication();

        using var provider = services.BuildServiceProvider();
        var bearerOptions = provider
            .GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);

        Assert.Equal(issuer, bearerOptions.TokenValidationParameters.ValidIssuer);
        Assert.Equal(audience, bearerOptions.TokenValidationParameters.ValidAudience);
        Assert.True(bearerOptions.TokenValidationParameters.ValidateIssuerSigningKey);
        Assert.Equal(
            Convert.ToHexString(Encoding.UTF8.GetBytes(signingKey)),
            Convert.ToHexString(bearerOptions.TokenValidationParameters.IssuerSigningKey!.Key));
        Assert.NotNull(bearerOptions.Events.OnMessageReceived);
    }
}
