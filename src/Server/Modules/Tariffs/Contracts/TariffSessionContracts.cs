using GameNet.Shared.Contracts.V1.Stations;

namespace GameNet.Server.Modules.Tariffs.Contracts;

public sealed record TariffSessionSnapshot(
    Guid Id,
    string Code,
    string Name,
    StationTypeContract StationType,
    long HourlyRateToman,
    int Version);

/// <summary>
/// Returns the active tariff values needed for an immutable Session snapshot. The
/// implementation participates in the caller's current scoped transaction and does not commit it.
/// </summary>
public interface ITariffSessionAccess
{
    Task<TariffSessionSnapshot?> FindActiveAsync(Guid tariffId, CancellationToken cancellationToken);
    Task<TariffSessionSnapshot?> FindDefaultActiveAsync(StationTypeContract stationType, CancellationToken cancellationToken) => Task.FromResult<TariffSessionSnapshot?>(null);
}
