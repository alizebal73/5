using GameNet.Server.Modules.Stations.Contracts;
using GameNet.Server.Persistence;
using GameNet.Shared.Contracts.V1.Stations;
using GameNet.Shared.Primitives;
using Microsoft.EntityFrameworkCore;

namespace GameNet.Server.Modules.Stations.Infrastructure;

/// <summary>
/// Supplies only public station facts to the Session workflow and does not expose the
/// Stations domain aggregate across module boundaries.
/// </summary>
public sealed class StationSessionAccess(GameNetDbContext db, IGameClock clock) : IStationSessionAccess
{
    private static readonly TimeSpan HeartbeatFreshness = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan MaximumFutureHeartbeatSkew = TimeSpan.FromSeconds(2);

    public async Task<StationSessionAvailability?> FindForSessionStartAsync(
        Guid stationId,
        CancellationToken cancellationToken)
    {
        var station = await db.Stations.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == stationId, cancellationToken);
        if (station is null)
            return null;

        var agentOnline = false;
        if (station.Type == GameNet.Server.Modules.Stations.Domain.StationType.Pc &&
            !string.IsNullOrWhiteSpace(station.AgentDeviceId))
        {
            var now = clock.UtcNow;
            var hasActiveCredential = await db.AgentCredentials.AsNoTracking().AnyAsync(
                x => x.DeviceId == station.AgentDeviceId && x.RevokedAtUtc == null,
                cancellationToken);
            if (hasActiveCredential)
            {
                var lease = await db.AgentConnectionLeases.AsNoTracking()
                    .SingleOrDefaultAsync(x => x.DeviceId == station.AgentDeviceId, cancellationToken);

                agentOnline = lease is not null &&
                    lease.LeaseExpiresAtUtc > now &&
                    lease.LastHeartbeatAtUtc is DateTimeOffset heartbeat &&
                    heartbeat >= now.Subtract(HeartbeatFreshness) &&
                    heartbeat <= now.Add(MaximumFutureHeartbeatSkew);
            }
        }

        return new StationSessionAvailability(
            station.Id,
            station.Code,
            (StationTypeContract)station.Type,
            (StationStatusContract)station.Status,
            agentOnline);
    }
}
