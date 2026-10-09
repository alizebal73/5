using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GameNet.Server.Infrastructure.Audit;
using GameNet.Server.Infrastructure.Idempotency;
using GameNet.Server.Infrastructure.Transactions;
using GameNet.Server.Modules.Stations.Domain;
using GameNet.Shared.Contracts.V1.Security;
using GameNet.Shared.Primitives;

namespace GameNet.Server.Modules.Stations.Application;
public sealed class StationService(IStationRepository repository, ITransactionCoordinator transactions, IIdempotencyStore idempotency,
    IAuditWriter audit, IGameClock clock)
{
    private static readonly TimeSpan Lease = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan Retention = TimeSpan.FromDays(1);

    public async Task<Result<IReadOnlyList<StationDto>>> ListAsync(StationActorContext actor, CancellationToken ct)
    {
        if (!actor.Permissions.Contains(Permissions.StationsRead)) return FailList("stations.forbidden", "Not allowed to read stations.");
        var all = await repository.ListAsync(ct);
        var ids = all.Where(s => s.AgentDeviceId is not null).Select(s => s.AgentDeviceId!).Distinct(StringComparer.Ordinal).ToArray();
        var state = await repository.GetAgentRuntimeAsync(ids, ct);
        return Result<IReadOnlyList<StationDto>>.Success(all.Select(s => ToDto(s, state, clock.UtcNow)).ToArray());
    }

    public Task<Result<StationDto>> CreateAsync(CreateStationCommand c, CancellationToken ct) =>
        Mutate("create", c.IdempotencyKey, new { c.Code, c.Name, c.Type }, c.Actor, 201, async token =>
        {
            Station s;
            try { s = Station.Create(Guid.NewGuid(), c.Code, c.Name, c.Type); }
            catch (ArgumentException ex) { return Fail<StationDto>("stations.invalid", ex.Message); }
            if (await repository.CodeExistsAsync(s.Code, null, token)) return Fail<StationDto>("stations.code_exists", "Station code already exists.");
            await repository.AddAsync(s, token);
            var dto = await ReadDto(s, token);
            Audit(c.Actor, "stations.create", s.Id, null, dto, c.IdempotencyKey);
            return Result<StationDto>.Success(dto);
        }, ct);

    public Task<Result<StationDto>> RenameAsync(RenameStationCommand c, CancellationToken ct) =>
        Mutate("rename", c.IdempotencyKey, new { c.StationId, c.Name, c.ExpectedVersion }, c.Actor, 200, async token =>
        {
            var s = await repository.FindAsync(c.StationId, token);
            if (s is null) return Fail<StationDto>("stations.not_found", "Station was not found.");
            if (s.Version != c.ExpectedVersion) return Fail<StationDto>("stations.version_conflict", "Refresh the station before retrying.");
            var before = await ReadDto(s, token);
            try { s.Rename(c.Name); } catch (ArgumentException ex) { return Fail<StationDto>("stations.invalid", ex.Message); }
            var after = await ReadDto(s, token);
            Audit(c.Actor, "stations.rename", s.Id, before, after, c.IdempotencyKey);
            return Result<StationDto>.Success(after);
        }, ct);

    public Task<Result<StationDto>> BindAgentAsync(BindStationAgentCommand c, CancellationToken ct) =>
        Mutate("bind-agent", c.IdempotencyKey, new { c.StationId, c.DeviceId, c.ExpectedVersion }, c.Actor, 200, async token =>
        {
            var s = await repository.FindAsync(c.StationId, token);
            if (s is null) return Fail<StationDto>("stations.not_found", "Station was not found.");
            if (s.Version != c.ExpectedVersion) return Fail<StationDto>("stations.version_conflict", "Refresh the station before retrying.");
            if (!await repository.IsActiveAgentCredentialAsync(c.DeviceId, token)) return Fail<StationDto>("stations.agent_not_provisioned", "Agent has no active credential.");
            if (await repository.IsAgentDeviceBoundAsync(c.DeviceId, s.Id, token)) return Fail<StationDto>("stations.device_already_bound", "Agent device is bound to another station.");
            var before = await ReadDto(s, token);
            try { s.BindAgent(c.DeviceId); } catch (ArgumentException ex) { return Fail<StationDto>("stations.invalid", ex.Message); }
            var after = await ReadDto(s, token);
            Audit(c.Actor, "stations.bind_agent", s.Id, before, after, c.IdempotencyKey);
            return Result<StationDto>.Success(after);
        }, ct);

    public Task<Result<StationDto>> SetStatusAsync(SetStationStatusCommand c, CancellationToken ct) =>
        Mutate("set-status", c.IdempotencyKey, new { c.StationId, c.Status, c.ExpectedVersion }, c.Actor, 200, async token =>
        {
            var s = await repository.FindAsync(c.StationId, token);
            if (s is null) return Fail<StationDto>("stations.not_found", "Station was not found.");
            if (s.Version != c.ExpectedVersion) return Fail<StationDto>("stations.version_conflict", "Refresh the station before retrying.");
            var before = await ReadDto(s, token);
            try { s.SetAdministrativeStatus(c.Status); }
            catch (InvalidOperationException ex) { return Fail<StationDto>("stations.invalid_transition", ex.Message); }
            catch (ArgumentOutOfRangeException ex) { return Fail<StationDto>("stations.invalid", ex.Message); }
            var after = await ReadDto(s, token);
            Audit(c.Actor, "stations.set_status", s.Id, before, after, c.IdempotencyKey);
            return Result<StationDto>.Success(after);
        }, ct);

    private async Task<Result<T>> Mutate<T>(string op, string key, object payload, StationActorContext actor, int status,
        Func<CancellationToken, Task<Result<T>>> action, CancellationToken ct)
    {
        if (!actor.Permissions.Contains(Permissions.StationsOperate)) return Fail<T>("stations.forbidden", "Not allowed to change stations.");
        if (string.IsNullOrWhiteSpace(key) || key.Length > 200) return Fail<T>("idempotency.required", "A valid Idempotency-Key is required.");
        var scope = $"stations.{op}:{actor.ActorId ?? "unknown"}";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload))));
        try
        {
            return await transactions.ExecuteAsync(async token =>
            {
                var claim = await idempotency.TryClaimAsync(scope, key.Trim(), op, hash, Lease, Retention, token);
                if (claim.Completed && claim.ResponseJson is not null)
                {
                    var replay = JsonSerializer.Deserialize<T>(claim.ResponseJson);
                    return replay is null ? Fail<T>("idempotency.invalid_replay", "Saved operation response is invalid.") : Result<T>.Success(replay);
                }
                if (claim.InFlight) throw new AbortMutation(new Error("idempotency.in_flight", "Operation is already in progress."));
                if (!claim.Claimed || string.IsNullOrWhiteSpace(claim.LeaseToken))
                    throw new AbortMutation(new Error("idempotency.unavailable", "Operation could not be claimed."));
                var result = await action(token);
                if (!result.IsSuccess) throw new AbortMutation(result.Error);
                await idempotency.CompleteAsync(scope, key.Trim(), claim.LeaseToken, status, JsonSerializer.Serialize(result.Value), token);
                return result;
            }, ct);
        }
        catch (AbortMutation ex) { return Fail<T>(ex.Error.Code, ex.Error.Message); }
        catch (IdempotencyKeyConflictException) { return Fail<T>("idempotency.key_reused", "Idempotency-Key was used for a different request."); }
        catch (PersistenceConflictException) { return Fail<T>("stations.conflict", "A station code or Agent binding conflicts with another record."); }
    }

    private async Task<StationDto> ReadDto(Station s, CancellationToken ct)
    {
        IReadOnlyDictionary<string, StationRuntimeInfo> state = s.AgentDeviceId is null
            ? new Dictionary<string, StationRuntimeInfo>(StringComparer.Ordinal)
            : await repository.GetAgentRuntimeAsync(new[] { s.AgentDeviceId }, ct);
        return ToDto(s, state, clock.UtcNow);
    }

    private static StationDto ToDto(Station s, IReadOnlyDictionary<string, StationRuntimeInfo> state, DateTimeOffset now)
    {
        state.TryGetValue(s.AgentDeviceId ?? string.Empty, out var a);
        return new StationDto(s.Id, s.Code, s.Name, s.Type, s.Status, s.Version, s.AgentDeviceId,
            a is not null && a.LastHeartbeatAtUtc.HasValue && a.LeaseExpiresAtUtc > now,
            a?.LastHeartbeatAtUtc, a?.AgentVersion, a?.StationState);
    }

    private void Audit(StationActorContext actor, string operation, Guid id, object? before, object after, string key) =>
        audit.Append(new AuditRecord(OccurredAtUtc: clock.UtcNow, ActorType: actor.ActorType, ActorId: actor.ActorId,
            Operation: operation, ReferenceType: "Station", ReferenceId: id.ToString("D"), Reason: null,
            CorrelationId: actor.CorrelationId, Source: actor.Source, Outcome: "Succeeded",
            BeforeJson: before is null ? null : JsonSerializer.Serialize(before), AfterJson: JsonSerializer.Serialize(after), IdempotencyKey: key));

    private static Result<T> Fail<T>(string code, string message) => Result<T>.Failure(new Error(code, message));
    private static Result<IReadOnlyList<StationDto>> FailList(string code, string message) =>
        Result<IReadOnlyList<StationDto>>.Failure(new Error(code, message));
    private sealed class AbortMutation(Error error) : Exception(error.Code) { public Error Error { get; } = error; }
}
