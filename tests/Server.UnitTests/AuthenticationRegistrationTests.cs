using System.Security.Claims;
using System.Text;
using GameNet.Server.Infrastructure.Configuration;
using GameNet.Server.Infrastructure.Security;
using GameNet.Shared.Contracts.V1.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Xunit;
using GameNetAuthenticationOptions = GameNet.Server.Infrastructure.Configuration.AuthenticationOptions;

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
            Authentication = new GameNetAuthenticationOptions
            {
                Enabled = true,
                Issuer = issuer,
                Audience = audience
            }
        };

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IOptions<GameNetOptions>>(Options.Create(applicationOptions));
        services.AddSingleton<IJwtSigningKeySecret>(new TestSigningKeySecret(signingKey));
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
            Convert.ToHexString(((SymmetricSecurityKey)bearerOptions.TokenValidationParameters.IssuerSigningKey!).Key));
        Assert.NotNull(bearerOptions.Events.OnMessageReceived);
    }

    [Fact]
    public async Task Agent_token_is_accepted_only_when_its_exact_persisted_credential_is_active()
    {
        var result = await ValidateAgentTokenAsync(credentialActive: true, includeCredentialId: true);

        Assert.Null(result);
    }

    [Fact]
    public async Task Agent_token_is_rejected_after_credential_revocation_or_rotation()
    {
        var result = await ValidateAgentTokenAsync(credentialActive: false, includeCredentialId: true);

        Assert.NotNull(result?.Failure);
    }

    [Fact]
    public async Task Legacy_Agent_token_without_credential_id_is_rejected()
    {
        var result = await ValidateAgentTokenAsync(credentialActive: true, includeCredentialId: false);

        Assert.NotNull(result?.Failure);
    }

    private static async Task<AuthenticateResult?> ValidateAgentTokenAsync(
        bool credentialActive,
        bool includeCredentialId)
    {
        const string deviceId = "pc-01";
        const string signingKey = "unit-test-signing-key-that-is-long-enough-for-hmac-sha256";
        var credentialId = Guid.Parse("9ee5f8cb-04d7-43c0-aef2-fca16ca6c0f5");
        var applicationOptions = new GameNetOptions
        {
            Authentication = new GameNetAuthenticationOptions
            {
                Enabled = true,
                Issuer = "GameNet.Foundation.UnitTests",
                Audience = "GameNet.Agent.UnitTests"
            }
        };

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IOptions<GameNetOptions>>(Options.Create(applicationOptions));
        services.AddSingleton<IJwtSigningKeySecret>(new TestSigningKeySecret(signingKey));
        services.AddSingleton<IAgentCredentialService>(
            new TestAgentCredentialService(credentialId, deviceId, credentialActive));
        services.AddGameNetAuthentication();

        using var provider = services.BuildServiceProvider();
        var bearerOptions = provider
            .GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);
        var claims = new List<Claim>
        {
            new("actor_type", "Agent"),
            new("device_id", deviceId)
        };
        if (includeCredentialId)
            claims.Add(new Claim("credential_id", credentialId.ToString("D")));

        var httpContext = new DefaultHttpContext { RequestServices = provider };
        var scheme = new AuthenticationScheme(
            JwtBearerDefaults.AuthenticationScheme,
            JwtBearerDefaults.AuthenticationScheme,
            typeof(JwtBearerHandler));
        var tokenContext = new TokenValidatedContext(httpContext, scheme, bearerOptions)
        {
            Principal = new ClaimsPrincipal(new ClaimsIdentity(claims, JwtBearerDefaults.AuthenticationScheme))
        };

        await bearerOptions.Events.OnTokenValidated!(tokenContext);
        return tokenContext.Result;
    }

    private sealed record TestSigningKeySecret(string SigningKey) : IJwtSigningKeySecret;

    private sealed class TestAgentCredentialService(
        Guid activeCredentialId,
        string activeDeviceId,
        bool credentialActive) : IAgentCredentialService
    {
        public Task<AgentCredentialSecretResponse> ProvisionAsync(
            AgentCredentialProvisionRequest request, CancellationToken cancellationToken = default) =>
            Task.FromException<AgentCredentialSecretResponse>(new NotSupportedException());

        public Task<AgentCredentialRotationRequestedResponse> RequestRotationAsync(
            AgentCredentialRotateRequest request, CancellationToken cancellationToken = default) =>
            Task.FromException<AgentCredentialRotationRequestedResponse>(new NotSupportedException());

        public Task<AgentCredentialRotationStageResponse> StageRotationAsync(
            AgentCredentialRotationStageRequest request, CancellationToken cancellationToken = default) =>
            Task.FromException<AgentCredentialRotationStageResponse>(new NotSupportedException());

        public Task<AgentCredentialRotationConfirmationResponse> ConfirmRotationAsync(
            AgentCredentialRotationConfirmationRequest request, CancellationToken cancellationToken = default) =>
            Task.FromException<AgentCredentialRotationConfirmationResponse>(new NotSupportedException());

        public Task<Guid?> GetRequestedRotationOperationIdAsync(
            string deviceId, Guid credentialId, CancellationToken cancellationToken = default) =>
            Task.FromResult<Guid?>(null);

        public Task<bool> RevokeAsync(
            AgentCredentialRevokeRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task<bool> AuthenticateAsync(
            string deviceId, string secret, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task<Guid?> AuthenticateAndGetCredentialIdAsync(
            string deviceId, string secret, CancellationToken cancellationToken = default) =>
            Task.FromResult<Guid?>(null);

        public Task<bool> IsCredentialActiveAsync(
            Guid credentialId, string deviceId, CancellationToken cancellationToken = default) =>
            Task.FromResult(credentialActive &&
                            credentialId == activeCredentialId &&
                            string.Equals(deviceId, activeDeviceId, StringComparison.Ordinal));
    }
}
