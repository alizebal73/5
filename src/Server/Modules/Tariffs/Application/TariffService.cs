using System.Text.Json;
using GameNet.Server.Infrastructure.Audit;
using GameNet.Server.Infrastructure.Idempotency;
using GameNet.Server.Infrastructure.Transactions;
using GameNet.Server.Modules.Tariffs.Domain;
using GameNet.Server.Persistence;
using GameNet.Shared.Contracts.V1.Security;
using GameNet.Shared.Contracts.V1.Stations;
using GameNet.Shared.Contracts.V1.Tariffs;
using GameNet.Shared.Primitives;
using Microsoft.EntityFrameworkCore;

namespace GameNet.Server.Modules.Tariffs.Application;

public sealed class TariffService(
    GameNetDbContext db,
    CommandMutationPipeline mutationPipeline,
    IRequestFingerprint requestFingerprint,
    IAuditWriter audit,
    IGameClock clock) : ITariffService
{
    private static readonly TimeSpan Lease = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan Retention = TimeSpan.FromDays(1);

    public async Task<Result<IReadOnlyList<TariffResponse>>> ListActiveAsync(
        TariffActorContext actor,
        StationTypeContract? stationType,
        CancellationToken cancellationToken)
    {
        if (!actor.Permissions.Contains(Permissions.SessionsOperate) &&
            !actor.Permissions.Contains(Permissions.SettingsWrite))
            return Fail<IReadOnlyList<TariffResponse>>("tariffs.forbidden", "Not allowed to read tariffs.");

        if (stationType.HasValue && !Enum.IsDefined(stationType.Value))
            return Fail<IReadOnlyList<TariffResponse>>("tariffs.invalid", "Unknown station type.");

        var query = db.Tariffs.AsNoTracking().Where(x => x.IsActive);
        if (stationType.HasValue)
            query = query.Where(x => x.StationType == stationType.Value);

        var rows = await query.OrderBy(x => x.StationType).ThenBy(x => x.Code).ToArrayAsync(cancellationToken);
        return Result<IReadOnlyList<TariffResponse>>.Success(rows.Select(ToResponse).ToArray());
    }

    public Task<Result<TariffResponse>> CreateAsync(CreateTariffCommand command, CancellationToken cancellationToken)
    {
        if (!command.Actor.Permissions.Contains(Permissions.SettingsWrite))
            return Task.FromResult(Fail<TariffResponse>("tariffs.forbidden", "Not allowed to change tariffs."));

        if (!Enum.IsDefined(command.StationType))
            return Task.FromResult(Fail<TariffResponse>("tariffs.invalid", "Unknown station type."));

        var payload = new
        {
            command.Code,
            command.Name,
            command.StationType,
            command.HourlyRateToman,
            command.IsDefault
        };

        return CreateCoreAsync(command, payload, cancellationToken);
    }

    private async Task<Result<TariffResponse>> CreateCoreAsync(
        CreateTariffCommand command,
        object payload,
        CancellationToken cancellationToken)
    {
        var scope = $"tariffs.create:{command.Actor.OperatorId:D}";
        try
        {
            return await mutationPipeline.ExecuteAsync(
                scope,
                "tariff.create",
                command.IdempotencyKey,
                requestFingerprint.Compute(payload),
                201,
                Lease,
                Retention,
                async token =>
                {
                    Tariff tariff;
                    try
                    {
                        tariff = Tariff.CreateFixedHourly(
                            Guid.NewGuid(),
                            command.Code,
                            command.Name,
                            command.StationType,
                            command.HourlyRateToman,
                            command.IsDefault,
                            clock.UtcNow);
                    }
                    catch (ArgumentException ex)
                    {
                        return Fail<TariffResponse>("tariffs.invalid", ex.Message);
                    }

                    if (await db.Tariffs.AnyAsync(x => x.Code == tariff.Code, token))
                        return Fail<TariffResponse>("tariffs.code_exists", "Tariff code already exists.");

                    if (tariff.IsDefault)
                    {
                        var previousDefaults = await db.Tariffs
                            .Where(x => x.IsActive && x.IsDefault && x.StationType == tariff.StationType)
                            .ToArrayAsync(token);

                        foreach (var previous in previousDefaults)
                        {
                            var before = ToResponse(previous);
                            previous.SetDefault(false);
                            audit.Append(new AuditRecord(
                                clock.UtcNow, "Operator", command.Actor.OperatorId.ToString("D"),
                                "tariffs.unset_default", "Tariff", previous.Id.ToString("D"), null,
                                command.Actor.CorrelationId, command.Actor.Source, "Succeeded",
                                command.IdempotencyKey, JsonSerializer.Serialize(before),
                                JsonSerializer.Serialize(ToResponse(previous))));
                        }
                    }

                    await db.Tariffs.AddAsync(tariff, token);
                    var response = ToResponse(tariff);
                    audit.Append(new AuditRecord(
                        clock.UtcNow, "Operator", command.Actor.OperatorId.ToString("D"),
                        "tariffs.create", "Tariff", tariff.Id.ToString("D"), null,
                        command.Actor.CorrelationId, command.Actor.Source, "Succeeded",
                        command.IdempotencyKey, null, JsonSerializer.Serialize(response)));

                    return Result<TariffResponse>.Success(response);
                },
                cancellationToken);
        }
        catch (PersistenceConflictException)
        {
            return Fail<TariffResponse>("tariffs.conflict", "Tariff data conflicts with another update.");
        }
    }

    private static TariffResponse ToResponse(Tariff tariff) =>
        new(tariff.Id, tariff.Code, tariff.Name, tariff.StationType, tariff.HourlyRateToman,
            tariff.IsDefault, tariff.IsActive, tariff.CreatedAtUtc, tariff.Version);

    private static Result<T> Fail<T>(string code, string message) =>
        Result<T>.Failure(new Error(code, message));
}
