using GameNet.Server.Modules.Stations.Application;
using GameNet.Server.Modules.Stations.Domain;
using GameNet.Server.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GameNet.Server.Infrastructure.Persistence.Stations;

public sealed class EfStationRepository(GameNetDbContext db) : IStationRepository
{
    public async Task<IReadOnlyList<Station>> ListAsync(CancellationToken cancellationToken) =>
        await db.Stations
            .AsNoTracking()
            .OrderBy(x => x.Code)
            .ToListAsync(cancellationToken);

    public async Task AddAsync(Station station, CancellationToken cancellationToken) =>
        await db.Stations.AddAsync(station, cancellationToken);

    public Task<Station?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        db.Stations.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
}
