using GameNet.Server.Modules.Tariffs.Contracts;
using GameNet.Server.Persistence;
using GameNet.Shared.Contracts.V1.Stations;
using Microsoft.EntityFrameworkCore;

namespace GameNet.Server.Modules.Tariffs.Infrastructure;

/// <summary>
/// Resolves the tariff inside the calling Session transaction so the selected active tariff
/// and the persisted Session snapshot are observed under the same transaction boundary.
/// </summary>
public sealed class TariffSessionAccess(GameNetDbContext db) : ITariffSessionAccess
{
    public async Task<TariffSessionSnapshot?> FindActiveAsync(
        Guid tariffId,
        CancellationToken cancellationToken)
    {
        var tariff = await db.Tariffs.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == tariffId && x.IsActive, cancellationToken);
        return tariff is null
            ? null
            : new TariffSessionSnapshot(
                tariff.Id,
                tariff.Code,
                tariff.Name,
                (StationTypeContract)tariff.StationType,
                tariff.HourlyRateToman,
                tariff.Version);
    }
}
