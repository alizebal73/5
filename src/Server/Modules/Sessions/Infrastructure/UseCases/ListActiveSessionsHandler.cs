using GameNet.Server.Modules.Sessions.Application;
using GameNet.Server.Modules.Sessions.Domain;
using GameNet.Server.Persistence;
using GameNet.Shared.Contracts.V1.Security;
using GameNet.Shared.Contracts.V1.Sessions;
using GameNet.Shared.Primitives;
using Microsoft.EntityFrameworkCore;
using static GameNet.Server.Modules.Sessions.Infrastructure.SessionMapping;

namespace GameNet.Server.Modules.Sessions.Infrastructure.UseCases;

/// <summary>Read-only list of active Sessions, optionally filtered by station.</summary>
internal sealed class ListActiveSessionsHandler(GameNetDbContext db)
{
    public async Task<Result<IReadOnlyList<SessionResponse>>> HandleAsync(
        SessionActorContext actor,
        Guid? stationId,
        CancellationToken cancellationToken)
    {
        if (!actor.Permissions.Contains(Permissions.SessionsOperate))
            return Fail<IReadOnlyList<SessionResponse>>("sessions.forbidden", "Not allowed to read sessions.");

        var query = db.Sessions.AsNoTracking().Where(x => x.Status == SessionStatus.Active);
        if (stationId.HasValue)
            query = query.Where(x => x.StationId == stationId.Value);

        var rows = await query.OrderBy(x => x.StartedAtUtc).ToArrayAsync(cancellationToken);
        return Result<IReadOnlyList<SessionResponse>>.Success(rows.Select(ToResponse).ToArray());
    }
}
