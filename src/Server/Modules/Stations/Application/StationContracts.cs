using GameNet.Server.Modules.Stations.Domain;
namespace GameNet.Server.Modules.Stations.Application;
public sealed record StationActorContext(string ActorType, string? ActorId, string Source, string CorrelationId, IReadOnlySet<string> Permissions);
public sealed record CreateStationCommand(string Code, string Name, StationType Type, string IdempotencyKey, StationActorContext Actor);
public sealed record RenameStationCommand(Guid StationId, string Name, int ExpectedVersion, string IdempotencyKey, StationActorContext Actor);
public sealed record BindStationAgentCommand(Guid StationId, string DeviceId, int ExpectedVersion, string IdempotencyKey, StationActorContext Actor);
public sealed record SetStationStatusCommand(Guid StationId, StationStatus Status, int ExpectedVersion, string IdempotencyKey, StationActorContext Actor);
public sealed record StationRuntimeInfo(string DeviceId, DateTimeOffset LeaseExpiresAtUtc, DateTimeOffset? LastHeartbeatAtUtc, string? AgentVersion, string? StationState);
public sealed record StationDto(Guid Id, string Code, string Name, StationType Type, StationStatus Status, int Version, string? AgentDeviceId,
    bool AgentOnline, DateTimeOffset? LastHeartbeatAtUtc, string? AgentVersion, string? AgentReportedState);
public interface IStationRepository
{
    Task<IReadOnlyList<Station>> ListAsync(CancellationToken ct);
    Task<Station?> FindAsync(Guid id, CancellationToken ct);
    Task AddAsync(Station station, CancellationToken ct);
    Task<bool> CodeExistsAsync(string code, Guid? exceptId, CancellationToken ct);
    Task<bool> IsActiveAgentCredentialAsync(string deviceId, CancellationToken ct);
    Task<bool> IsAgentDeviceBoundAsync(string deviceId, Guid? exceptId, CancellationToken ct);
    Task<IReadOnlyDictionary<string, StationRuntimeInfo>> GetAgentRuntimeAsync(IReadOnlyCollection<string> deviceIds, CancellationToken ct);
}
