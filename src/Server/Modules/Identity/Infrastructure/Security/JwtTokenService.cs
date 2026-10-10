using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using GameNet.Server.Infrastructure.Configuration;
using GameNet.Server.Modules.Identity.Application;
using GameNet.Server.Modules.Identity.Domain;
using GameNet.Shared.Primitives;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace GameNet.Server.Modules.Identity.Infrastructure.Security;

public sealed class JwtTokenService(IOptions<GameNetOptions> options, IGameClock clock, IJwtSigningKeySecret signingSecret) : ITokenService
{
    public IssuedAccessToken Issue(OperatorUser user, IReadOnlySet<string> permissions)
    {
        var settings = options.Value.Authentication;
        if (!settings.Enabled || string.IsNullOrWhiteSpace(settings.Issuer) ||
            string.IsNullOrWhiteSpace(settings.Audience) || string.IsNullOrWhiteSpace(signingSecret.SigningKey))
            throw new InvalidOperationException("AUTH_CONFIGURATION_INVALID");
        var now = clock.UtcNow;
        var expires = now.AddHours(8);
        var jti = Guid.NewGuid().ToString("N");
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString("D")),
            new(ClaimTypes.NameIdentifier, user.Id.ToString("D")),
            new(ClaimTypes.Name, user.Username),
            new("display_name", user.DisplayName),
            new("actor_type", "Operator"),
            new(JwtRegisteredClaimNames.Jti, jti)
        };
        claims.AddRange(permissions.Select(permission => new Claim("permission", permission)));
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingSecret.SigningKey));
        var token = new JwtSecurityToken(issuer: settings.Issuer, audience: settings.Audience, claims: claims,
            notBefore: now.UtcDateTime, expires: expires.UtcDateTime,
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
        return new IssuedAccessToken(new JwtSecurityTokenHandler().WriteToken(token), jti, expires);
    }
}
