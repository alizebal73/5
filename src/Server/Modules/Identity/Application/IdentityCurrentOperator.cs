using GameNet.Server.Modules.Identity.Application;

namespace GameNet.Server.Modules.Identity.Application;

public sealed record CurrentOperatorResult(
    Guid UserId,
    string Username,
    string DisplayName,
    IReadOnlySet<string> Permissions);
