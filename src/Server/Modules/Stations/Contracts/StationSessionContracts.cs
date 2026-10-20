using GameNet.Shared.Contracts.V1.Stations;

namespace GameNet.Server.Modules.Stations.Contracts;

public sealed record StationSessionAvailability(
    Guid Id,
    string Code,
    StationTypeContract Type,
    StationStatusContract Status,
    bool AgentOnline);

/// <summary>
/// Exposes the administrative status and authoritative Agent-readiness facts Sessions needs;
/// it does not expose the Stations domain entity or repository implementation.
/// </summary>
public interface IStationSessionAccess
{
    Task<StationSessionAvailability?> FindForSessionStartAsync(Guid stationId, CancellationToken cancellationToken);
    Task<Guid?> FindAssignedPcStationIdForAgentAsync(string deviceId, CancellationToken cancellationToken) => Task.FromResult<Guid?>(null);
}
