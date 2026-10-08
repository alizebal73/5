using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GameNet.Server.Application;
using GameNet.Server.Modules.Stations.Domain;
using GameNet.Shared.Contracts.V1.Security;
using GameNet.Shared.Primitives;

namespace GameNet.Server.Modules.Stations.Application;

public sealed class StationService(
    IStationRepository repository,
    ITransactionCoordinator transactions,
    IIdempotencyStore idempotency,
    IAuditWriter audit,
    IGameClock clock)
{
    public async Task<Result<IReadOnlyList<StationDto>>> ListAsync(
        StationActorContext actor,
        CancellationToken cancellationToken)
    {
        if (!actor.Permissions.Contains(Permissions.StationsRead))
            return Result<IReadOnlyList<StationDto>>.Failure(
                new Error("stations.forbidden", "The operator is not allowed to read stations."));

        var stations = await repository.ListAsync(cancellationToken);
        return Result<IReadOnlyList<StationDto>>.Success(stations.Select(ToDto).ToArray());
    }

    public Task<Result<StationDto>> CreateAsync(
        CreateStationCommand command,
        CancellationToken cancellationToken) =>
        ExecuteIdempotentAsync(
            scope: "stations.create",
            key: command.IdempotencyKey,
            operation: "stations.create",
            payload: new { command.Code, command.Name, command.Type },
            completedStatusCode: 201,
            actor: command.Actor,
            action: async ct =>
            {
                try
                {
                    var station = Station.Create(Guid.NewGuid(), command.Code, command.Name, command.Type);
                    await repository.AddAsync(station, ct);

                    var dto = ToDto(station);
                    audit.Append(new AuditRecord(
                        clock.UtcNow,
                        command.Actor.ActorType,
                        command.Actor.ActorId,
                        "stations.create",
                        "Station",
                        station.Id.ToString("D"),
                        null,
                        null,
                        JsonSerializer.Serialize(dto),
                        command.Actor.Source,
                        command.CommandId,
                        command.IdempotencyKey,
                        "Succeeded"));

                    return Result<StationDto>.Success(dto);
                }
                catch (ArgumentException ex)
                {
                    return Failure("stations.invalid", ex.Message);
                }
            },
            cancellationToken);

    public Task<Result<StationDto>> RenameAsync(
        RenameStationCommand command,
        CancellationToken cancellationToken) =>
        ExecuteIdempotentAsync(
            scope: "stations.rename",
            key: command.IdempotencyKey,
            operation: "stations.rename",
            payload: new { command.StationId, command.Name },
            completedStatusCode: 200,
            actor: command.Actor,
            action: async ct =>
            {
                var station = await repository.FindAsync(command.StationId, ct);
                if (station is null)
                    return Failure("stations.not_found", "Station was not found.");

                var before = ToDto(station);

                try
                {
                    station.Rename(command.Name);

                    var after = ToDto(station);
                    audit.Append(new AuditRecord(
                        clock.UtcNow,
                        command.Actor.ActorType,
                        command.Actor.ActorId,
                        "stations.rename",
                        "Station",
                        station.Id.ToString("D"),
                        null,
                        JsonSerializer.Serialize(before),
                        JsonSerializer.Serialize(after),
                        command.Actor.Source,
                        command.CommandId,
                        command.IdempotencyKey,
                        "Succeeded"));

                    return Result<StationDto>.Success(after);
                }
                catch (ArgumentException ex)
                {
                    return Failure("stations.invalid", ex.Message);
                }
            },
            cancellationToken);

    private async Task<Result<StationDto>> ExecuteIdempotentAsync(
        string scope,
        string key,
        string operation,
        object payload,
        int completedStatusCode,
        StationActorContext actor,
        Func<CancellationToken, Task<Result<StationDto>>> action,
        CancellationToken cancellationToken)
    {
        if (!actor.Permissions.Contains(Permissions.StationsOperate))
            return Failure("stations.forbidden", "The operator is not allowed to manage stations.");

        if (string.IsNullOrWhiteSpace(key))
            return Failure("idempotency.required", "An idempotency key is required.");

        var requestHash = Hash(payload);

        try
        {
            return await transactions.ExecuteAsync(async ct =>
            {
                var claim = await idempotency.TryClaimAsync(
                    scope,
                    key.Trim(),
                    operation,
                    requestHash,
                    ct);

                if (claim.State == IdempotencyClaimState.Completed && claim.ResponseJson is not null)
                {
                    var replay = JsonSerializer.Deserialize<StationDto>(claim.ResponseJson);
                    return replay is null
                        ? Failure("idempotency.invalid_replay", "The stored idempotent response is invalid.")
                        : Result<StationDto>.Success(replay);
                }

                if (claim.State == IdempotencyClaimState.InFlight)
                    throw new MutationAbortedException(
                        new Error("idempotency.in_flight", "The same operation is already in progress."));

                if (claim.State == IdempotencyClaimState.Conflict)
                    throw new MutationAbortedException(
                        new Error("idempotency.key_reused", "The idempotency key was already used for different request data."));

                var result = await action(ct);
                if (!result.IsSuccess)
                    throw new MutationAbortedException(result.Error);

                await idempotency.CompleteAsync(
                    scope,
                    key.Trim(),
                    claim.LeaseToken,
                    completedStatusCode,
                    JsonSerializer.Serialize(result.Value),
                    ct);

                return result;
            }, cancellationToken);
        }
        catch (MutationAbortedException ex)
        {
            return Failure(ex.Error.Code, ex.Error.Message);
        }
        catch (PersistenceConflictException ex) when (ex.Code == "persistence.unique")
        {
            return Failure("stations.code_exists", "A station with this code already exists.");
        }
        catch (PersistenceConflictException ex) when (ex.Code == "persistence.concurrency")
        {
            return Failure("stations.concurrency_conflict", "The station changed before this operation could be saved.");
        }
    }

    private static Result<StationDto> Failure(string code, string message) =>
        Result<StationDto>.Failure(new Error(code, message));

    private static StationDto ToDto(Station station) =>
        new(station.Id, station.Code, station.Name, station.Type, station.Status, station.Version);

    private static string Hash(object payload)
    {
        var json = JsonSerializer.Serialize(payload);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
    }
}
