using GameNet.Server.Infrastructure.Configuration;
using GameNet.Server.Infrastructure.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace GameNet.Server.UnitTests;

public sealed class AuthenticationRegistrationTests
{
    [Fact]
    public void Bearer_scheme_uses_configured_issuer_audience_and_protected_signing_secret()
    {
        const string issuer = "GameNet.Foundation.UnitTests";
        const string audience = "GameNet.Agent.UnitTests";
        var signingKeyBytes = Enumerable.Range(1, 48).Select(value => (byte)value).ToArray();
        var signingKey = Convert.ToBase64String(signingKeyBytes);

        var applicationOptions = new GameNetOptions
        {
            Authentication = new AuthenticationOptions
            {
                Enabled = true,
                Issuer = issuer,
                Audience = audience
            }
        };

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IOptions<GameNetOptions>>(Options.Create(applicationOptions));
        services.AddSingleton<IJwtSigningKeySecret>(new TestJwtSigningKeySecret(signingKey));
        services.AddGameNetAuthentication();

        using var provider = services.BuildServiceProvider();
        var bearerOptions = provider
            .GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);

        Assert.Equal(issuer, bearerOptions.TokenValidationParameters.ValidIssuer);
        Assert.Equal(audience, bearerOptions.TokenValidationParameters.ValidAudience);
        Assert.True(bearerOptions.TokenValidationParameters.ValidateIssuerSigningKey);
        Assert.Equal(
            Convert.ToHexString(signingKeyBytes),
            Convert.ToHexString(((SymmetricSecurityKey)bearerOptions.TokenValidationParameters.IssuerSigningKey!).Key));
        Assert.NotNull(bearerOptions.Events.OnMessageReceived);
    }

    private sealed class TestJwtSigningKeySecret(string signingKey) : IJwtSigningKeySecret
    {
        public string SigningKey { get; } = signingKey;
    }
}
