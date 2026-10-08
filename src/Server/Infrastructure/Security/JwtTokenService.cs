using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using GameNet.Server.Infrastructure.Configuration;
using GameNet.Server.Modules.Identity.Application;
using GameNet.Server.Modules.Identity.Domain;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace GameNet.Server.Infrastructure.Security;

public sealed class JwtTokenService(
    IOptions<GameNetOptions> options,
    TimeProvider timeProvider) : ITokenService
{
    public IssuedAccessToken Issue(OperatorUser user, IReadOnlySet<string> permissions)
    {
        var settings = options.Value.Authentication;

        if (!settings.Enabled ||
            string.IsNullOrWhiteSpace(settings.Issuer) ||
            string.IsNullOrWhiteSpace(settings.Audience) ||
            string.IsNullOrWhiteSpace(settings.SigningKey))
            throw new InvalidOperationException("AUTH_CONFIGURATION_INVALID");

        var now = timeProvider.GetUtcNow();
        var expires = now.AddHours(8);
        var jti = Guid.NewGuid().ToString("N");

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString("D")),
            new(ClaimTypes.NameIdentifier, user.Id.ToString("D")),
            new(ClaimTypes.Name, user.Username),
            new("display_name", user.DisplayName),
            new(JwtRegisteredClaimNames.Jti, jti)
        };

        claims.AddRange(permissions.Select(permission => new Claim("permission", permission)));

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(settings.SigningKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: settings.Issuer,
            audience: settings.Audience,
            claims: claims,
            notBefore: now.UtcDateTime,
            expires: expires.UtcDateTime,
            signingCredentials: credentials);

        return new IssuedAccessToken(
            new JwtSecurityTokenHandler().WriteToken(token),
            jti,
            expires);
    }
}
