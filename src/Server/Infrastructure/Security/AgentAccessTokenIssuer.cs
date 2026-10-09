using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using GameNet.Server.Infrastructure.Configuration;
using GameNet.Shared.Contracts.V1.Security;
using GameNet.Shared.Primitives;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace GameNet.Server.Infrastructure.Security;

public sealed class AgentAccessTokenIssuer(
    IOptions<GameNetOptions> options,
    IGameClock clock,
    IJwtSigningKeySecret signingSecret) : IAgentAccessTokenIssuer
{
    public AgentTokenResponse Issue(string deviceId)
    {
        var auth = options.Value.Authentication;
        if (!auth.Enabled ||
            string.IsNullOrWhiteSpace(auth.Issuer) ||
            string.IsNullOrWhiteSpace(auth.Audience))
            throw new InvalidOperationException("JWT authentication is not fully configured.");

        var now = clock.UtcNow;
        var expires = now.AddSeconds(options.Value.Agent.AccessTokenLifetimeSeconds);
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Convert.FromBase64String(signingSecret.SigningKey)),
            SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, deviceId),
            new Claim("actor_type", "Agent"),
            new Claim("device_id", deviceId),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N"))
        };

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = auth.Issuer,
            Audience = auth.Audience,
            Subject = new ClaimsIdentity(claims),
            NotBefore = now.UtcDateTime,
            Expires = expires.UtcDateTime,
            SigningCredentials = credentials
        };

        var handler = new JwtSecurityTokenHandler();
        return new AgentTokenResponse(handler.WriteToken(handler.CreateToken(descriptor)), expires);
    }
}
