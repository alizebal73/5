using GameNet.Server.Modules.Identity.Domain;

namespace GameNet.Server.Modules.Identity.Application;

public sealed record IssuedAccessToken(
    string AccessToken,
    string Jti,
    DateTimeOffset ExpiresAtUtc);

public interface ITokenService
{
    IssuedAccessToken Issue(OperatorUser user, IReadOnlySet<string> permissions);
}
