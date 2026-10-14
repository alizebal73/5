using GameNet.Shared.Contracts.V1.Stations;

namespace GameNet.Server.Modules.Stations.Contracts;

public sealed record StationSessionAvailability(
    Guid Id,
    string Code,
    StationTypeContract Type,
    StationStatusContract Status);

/// <summary>
/// Exposes only the administrative station facts Sessions needs; it does not expose the
/// Stations domain entity or repository implementation.
/// </summary>
public interface IStationSessionAccess
{
    Task<StationSessionAvailability?> FindForSessionStartAsync(Guid stationId, CancellationToken cancellationToken);
}
