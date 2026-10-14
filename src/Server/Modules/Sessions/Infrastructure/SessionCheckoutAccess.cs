using GameNet.Server.Modules.Sessions.Contracts;
using GameNet.Server.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GameNet.Server.Modules.Sessions.Infrastructure;

/// <summary>
/// Reads Session concurrency versions for Billing through the Sessions module boundary.
/// Uses the caller's scoped DbContext and never starts or commits a transaction.
/// </summary>
public sealed class SessionCheckoutAccess(GameNetDbContext db) : ISessionCheckoutAccess
{
    public async Task<IReadOnlyDictionary<Guid, int>> GetSessionVersionsAsync(
        IReadOnlyCollection<Guid> sessionIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sessionIds);
        if (sessionIds.Count == 0)
            return new Dictionary<Guid, int>();

        return await db.Sessions.AsNoTracking()
            .Where(session => sessionIds.Contains(session.Id))
            .ToDictionaryAsync(session => session.Id, session => session.Version, cancellationToken);
    }
}
