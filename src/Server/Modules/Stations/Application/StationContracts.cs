using GameNet.Server.Modules.Stations.Domain;
using GameNet.Shared.Primitives;

namespace GameNet.Server.Modules.Stations.Application;

public sealed record StationActorContext(
    string ActorType,
    string? ActorId,
    string Source,
    IReadOnlySet<string> Permissions);

public sealed record CreateStationCommand(
    string Code,
    string Name,
    StationType Type,
    string IdempotencyKey,
    string CommandId,
    StationActorContext Actor);

public sealed record RenameStationCommand(
    Guid StationId,
    string Name,
    string IdempotencyKey,
    string CommandId,
    StationActorContext Actor);

public sealed record StationDto(
    Guid Id,
    string Code,
    string Name,
    StationType Type,
    StationStatus Status,
    int Version);

public interface IStationRepository
{
    Task<IReadOnlyList<Station>> ListAsync(CancellationToken cancellationToken);
    Task AddAsync(Station station, CancellationToken cancellationToken);
    Task<Station?> FindAsync(Guid id, CancellationToken cancellationToken);
}
