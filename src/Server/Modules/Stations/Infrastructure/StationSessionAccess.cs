using GameNet.Server.Modules.Stations.Contracts;
using GameNet.Server.Persistence;
using GameNet.Shared.Contracts.V1.Stations;
using Microsoft.EntityFrameworkCore;

namespace GameNet.Server.Modules.Stations.Infrastructure;

/// <summary>
/// Supplies only public station facts to the Session workflow and does not expose the
/// Stations domain aggregate across module boundaries.
/// </summary>
public sealed class StationSessionAccess(GameNetDbContext db) : IStationSessionAccess
{
    public async Task<StationSessionAvailability?> FindForSessionStartAsync(
        Guid stationId,
        CancellationToken cancellationToken)
    {
        var station = await db.Stations.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == stationId, cancellationToken);
        return station is null
            ? null
            : new StationSessionAvailability(
                station.Id,
                station.Code,
                (StationTypeContract)station.Type,
                (StationStatusContract)station.Status);
    }
}
