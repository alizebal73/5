using GameNet.Server.Modules.Stations.Application;
using GameNet.Server.Modules.Stations.Domain;
using GameNet.Server.Persistence;
using Microsoft.EntityFrameworkCore;
namespace GameNet.Server.Infrastructure.Persistence.Stations;
public sealed class EfStationRepository(GameNetDbContext db) : IStationRepository
{
    public async Task<IReadOnlyList<Station>> ListAsync(CancellationToken ct) => await db.Stations.AsNoTracking().OrderBy(x => x.Code).ToArrayAsync(ct);
    public Task<Station?> FindAsync(Guid id, CancellationToken ct) => db.Stations.SingleOrDefaultAsync(x => x.Id == id, ct);
    public async Task AddAsync(Station station, CancellationToken ct) => await db.Stations.AddAsync(station, ct);
    public Task<bool> CodeExistsAsync(string code, Guid? exceptId, CancellationToken ct)
    {
        var query = db.Stations.AsNoTracking().Where(x => x.Code == code);
        if (exceptId.HasValue) query = query.Where(x => x.Id != exceptId.Value);
        return query.AnyAsync(ct);
    }
    public Task<bool> IsActiveAgentCredentialAsync(string deviceId, CancellationToken ct) =>
        db.AgentCredentials.AsNoTracking().AnyAsync(x => x.DeviceId == deviceId && x.RevokedAtUtc == null, ct);
    public Task<bool> IsAgentDeviceBoundAsync(string deviceId, Guid? exceptId, CancellationToken ct)
    {
        var query = db.Stations.AsNoTracking().Where(x => x.AgentDeviceId == deviceId);
        if (exceptId.HasValue) query = query.Where(x => x.Id != exceptId.Value);
        return query.AnyAsync(ct);
    }
    public async Task<IReadOnlyDictionary<string, StationRuntimeInfo>> GetAgentRuntimeAsync(IReadOnlyCollection<string> deviceIds, CancellationToken ct)
    {
        if (deviceIds.Count == 0) return new Dictionary<string, StationRuntimeInfo>(StringComparer.Ordinal);
        var rows = await db.AgentConnectionLeases.AsNoTracking().Where(x => deviceIds.Contains(x.DeviceId))
            .Select(x => new StationRuntimeInfo(x.DeviceId, x.LeaseExpiresAtUtc, x.LastHeartbeatAtUtc, x.AgentVersion, x.StationState)).ToArrayAsync(ct);
        return rows.ToDictionary(x => x.DeviceId, StringComparer.Ordinal);
    }
}
