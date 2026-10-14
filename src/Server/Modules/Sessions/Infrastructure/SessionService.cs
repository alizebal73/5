using System.Text.Json;
using GameNet.Server.Infrastructure.Audit;
using GameNet.Server.Infrastructure.Idempotency;
using GameNet.Server.Infrastructure.Transactions;
using GameNet.Server.Modules.Billing.Contracts;
using GameNet.Server.Modules.Customers.Contracts;
using GameNet.Server.Modules.Sessions.Application;
using GameNet.Server.Modules.Sessions.Contracts;
using GameNet.Server.Modules.Sessions.Domain;
using GameNet.Server.Modules.Stations.Contracts;
using GameNet.Server.Modules.Tariffs.Contracts;
using GameNet.Server.Persistence;
using GameNet.Shared.Contracts.V1.Security;
using GameNet.Shared.Contracts.V1.Sessions;
using GameNet.Shared.Contracts.V1.Stations;
using GameNet.Shared.Primitives;
using Microsoft.EntityFrameworkCore;

namespace GameNet.Server.Modules.Sessions.Infrastructure;

/// <summary>
/// Session use cases are the transactional orchestrator for start/end. EF stays in
/// Infrastructure and cross-module access goes through each module's public Contracts.
/// All contract implementations share this scoped DbContext and the outer mutation transaction.
/// </summary>
public sealed class SessionService(
    GameNetDbContext db,
    CommandMutationPipeline mutationPipeline,
    IRequestFingerprint requestFingerprint,
    ICustomerSessionAccess customers,
    IStationSessionAccess stations,
    ITariffSessionAccess tariffs,
    IBillingInvoiceWriter invoices,
    IAuditWriter audit,
    IGameClock clock) : ISessionService
{
    private static readonly TimeSpan Lease = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan Retention = TimeSpan.FromDays(1);

    public Task<Result<SessionResponse>> StartAsync(
        StartSessionCommand command,
        CancellationToken cancellationToken)
    {
        if (!command.Actor.Permissions.Contains(Permissions.SessionsOperate))
            return Task.FromResult(Fail<SessionResponse>("sessions.forbidden", "Not allowed to start sessions."));

        var payload = new { command.StationId, command.TariffId, command.CustomerId };
        var scope = $"sessions.start:{command.Actor.OperatorId:D}";

        return StartCoreAsync(command, scope, payload, cancellationToken);
    }

    private async Task<Result<SessionResponse>> StartCoreAsync(
        StartSessionCommand command,
        string scope,
        object payload,
        CancellationToken cancellationToken)
    {
        try
        {
            return await mutationPipeline.ExecuteAsync(
                scope, "session.start", command.IdempotencyKey, requestFingerprint.Compute(payload),
                201, Lease, Retention,
                async token =>
                {
                    var station = await stations.FindForSessionStartAsync(command.StationId, token);
                    if (station is null)
                        return Fail<SessionResponse>("sessions.station_not_found", "Station was not found.");
                    if (station.Type != StationTypeContract.Pc)
                        return Fail<SessionResponse>("sessions.pc_only", "This first session flow supports PC stations only.");
                    if (station.Status != StationStatusContract.Available)
                        return Fail<SessionResponse>("sessions.station_unavailable", "Station is not available.");

                    var tariff = await tariffs.FindActiveAsync(command.TariffId, token);
                    if (tariff is null)
                        return Fail<SessionResponse>("sessions.tariff_not_found", "Active tariff was not found.");
                    if (tariff.StationType != StationTypeContract.Pc)
                        return Fail<SessionResponse>("sessions.tariff_station_mismatch", "Tariff is not for PC stations.");

                    var activeExists = await db.Sessions.AnyAsync(
                        x => x.StationId == station.Id && x.Status == SessionStatus.Active, token);
                    if (activeExists)
                        return Fail<SessionResponse>("sessions.station_has_active_session", "Station already has an active session.");

                    var customer = command.CustomerId is Guid customerId
                        ? await customers.FindActiveAsync(customerId, token)
                        : await customers.CreateWalkInAsync(clock.UtcNow, token);
                    if (customer is null)
                        return Fail<SessionResponse>("sessions.customer_not_found", "Active customer was not found.");

                    var session = Session.Start(
                        Guid.NewGuid(),
                        station.Id,
                        customer.Id,
                        tariff.Id,
                        tariff.Code,
                        tariff.Name,
                        tariff.HourlyRateToman,
                        command.Actor.OperatorId,
                        clock.UtcNow);
                    await db.Sessions.AddAsync(session, token);

                    var response = ToResponse(session);
                    audit.Append(new AuditRecord(
                        clock.UtcNow,
                        "Operator",
                        command.Actor.OperatorId.ToString("D"),
                        "sessions.start",
                        "Session",
                        session.Id.ToString("D"),
                        null,
                        command.Actor.CorrelationId,
                        command.Actor.Source,
                        "Succeeded",
                        command.IdempotencyKey,
                        null,
                        JsonSerializer.Serialize(response)));

                    return Result<SessionResponse>.Success(response);
                },
                cancellationToken);
        }
        catch (PersistenceConflictException)
        {
            return Fail<SessionResponse>("sessions.station_has_active_session", "Station changed concurrently; refresh and try again.");
        }
    }

    public async Task<Result<InvoicePreviewResponse>> EndAsync(
        EndSessionCommand command,
        CancellationToken cancellationToken)
    {
        if (!command.Actor.Permissions.Contains(Permissions.SessionsOperate))
            return Fail<InvoicePreviewResponse>("sessions.forbidden", "Not allowed to end sessions.");

        var scope = $"sessions.end:{command.Actor.OperatorId:D}";
        try
        {
            return await mutationPipeline.ExecuteAsync(
                scope,
                "session.end",
                command.IdempotencyKey,
                requestFingerprint.Compute(new { command.SessionId, command.ExpectedVersion }),
                200,
                Lease,
                Retention,
                async token =>
                {
                    var session = await db.Sessions.SingleOrDefaultAsync(x => x.Id == command.SessionId, token);
                    if (session is null)
                        return Fail<InvoicePreviewResponse>("sessions.not_found", "Session was not found.");
                    if (session.Version != command.ExpectedVersion)
                        return Fail<InvoicePreviewResponse>("sessions.version_conflict", "Session changed; refresh before ending it.");
                    if (session.Status != SessionStatus.Active)
                        return Fail<InvoicePreviewResponse>("sessions.invalid_state", "Only an active session can be ended.");

                    var endedAt = clock.UtcNow;
                    var before = ToResponse(session);
                    try
                    {
                        session.End(command.Actor.OperatorId, endedAt);
                    }
                    catch (InvalidOperationException)
                    {
                        return Fail<InvoicePreviewResponse>("sessions.invalid_state", "Session could not be ended from its current state.");
                    }

                    var invoiceResult = await invoices.CreatePendingAsync(
                        new CreatePendingInvoiceCommand(
                            Guid.NewGuid(),
                            session.Id,
                            session.StationId,
                            session.CustomerId,
                            session.TariffId,
                            session.TariffCodeSnapshot,
                            session.TariffNameSnapshot,
                            session.TariffHourlyRateTomanSnapshot,
                            session.StartedAtUtc,
                            endedAt,
                            session.Version,
                            command.Actor.OperatorId,
                            endedAt,
                            command.Actor.CorrelationId,
                            command.Actor.Source,
                            command.IdempotencyKey),
                        token);
                    if (!invoiceResult.IsSuccess)
                        return Fail<InvoicePreviewResponse>(invoiceResult.Error.Code, invoiceResult.Error.Message);

                    audit.Append(new AuditRecord(
                        clock.UtcNow,
                        "Operator",
                        command.Actor.OperatorId.ToString("D"),
                        "sessions.end",
                        "Session",
                        session.Id.ToString("D"),
                        null,
                        command.Actor.CorrelationId,
                        command.Actor.Source,
                        "Succeeded",
                        command.IdempotencyKey,
                        JsonSerializer.Serialize(before),
                        JsonSerializer.Serialize(ToResponse(session))));

                    return invoiceResult;
                },
                cancellationToken);
        }
        catch (PersistenceConflictException)
        {
            return Fail<InvoicePreviewResponse>("sessions.version_conflict", "Session changed concurrently; refresh and try again.");
        }
    }

    public async Task<Result<IReadOnlyList<SessionResponse>>> ListActiveAsync(
        SessionActorContext actor,
        Guid? stationId,
        CancellationToken cancellationToken)
    {
        if (!actor.Permissions.Contains(Permissions.SessionsOperate))
            return Fail<IReadOnlyList<SessionResponse>>("sessions.forbidden", "Not allowed to read sessions.");

        var query = db.Sessions.AsNoTracking().Where(x => x.Status == SessionStatus.Active);
        if (stationId.HasValue)
            query = query.Where(x => x.StationId == stationId.Value);

        var rows = await query.OrderBy(x => x.StartedAtUtc).ToArrayAsync(cancellationToken);
        return Result<IReadOnlyList<SessionResponse>>.Success(rows.Select(ToResponse).ToArray());
    }

    private static SessionResponse ToResponse(Session session) =>
        new(session.Id, session.StationId, session.CustomerId, session.TariffId,
            session.TariffCodeSnapshot, session.TariffNameSnapshot, session.TariffHourlyRateTomanSnapshot,
            session.StartedByOperatorId, session.StartedAtUtc, session.EndedByOperatorId, session.EndedAtUtc,
            session.PaidByOperatorId, session.PaidAtUtc, (SessionStatusContract)session.Status, session.Version);

    private static Result<T> Fail<T>(string code, string message) =>
        Result<T>.Failure(new Error(code, message));
}

public sealed class SessionSettlement(
    GameNetDbContext db,
    IAuditWriter audit,
    IGameClock clock) : ISessionSettlement
{
    public async Task<Result<SessionResponse>> MarkPaidAsync(
        SettleSessionCommand command,
        CancellationToken cancellationToken)
    {
        if (db.Database.CurrentTransaction is null)
            return Result<SessionResponse>.Failure(
                new Error("sessions.transaction_required", "Session settlement requires the active Billing transaction."));

        var session = await db.Sessions.SingleOrDefaultAsync(x => x.Id == command.SessionId, cancellationToken);
        if (session is null)
            return Result<SessionResponse>.Failure(new Error("billing.session_not_found", "Session was not found."));
        if (session.Version != command.ExpectedVersion)
            return Result<SessionResponse>.Failure(new Error("billing.session_version_conflict", "Session changed; refresh before checkout."));
        if (session.Status != SessionStatus.PendingSettlement)
            return Result<SessionResponse>.Failure(new Error("billing.session_not_ready", "Session is not awaiting settlement."));

        var before = ToResponse(session);
        try
        {
            session.MarkPaid(command.PaidByOperatorId, command.PaidAtUtc);
        }
        catch (InvalidOperationException)
        {
            return Result<SessionResponse>.Failure(new Error("billing.session_not_ready", "Session is not awaiting settlement."));
        }

        var response = ToResponse(session);
        audit.Append(new AuditRecord(
            clock.UtcNow,
            "Operator",
            command.PaidByOperatorId.ToString("D"),
            "sessions.paid",
            "Session",
            session.Id.ToString("D"),
            null,
            command.CorrelationId,
            command.Source,
            "Succeeded",
            command.IdempotencyKey,
            JsonSerializer.Serialize(before),
            JsonSerializer.Serialize(response)));
        return Result<SessionResponse>.Success(response);
    }

    private static SessionResponse ToResponse(Session session) =>
        new(session.Id, session.StationId, session.CustomerId, session.TariffId,
            session.TariffCodeSnapshot, session.TariffNameSnapshot, session.TariffHourlyRateTomanSnapshot,
            session.StartedByOperatorId, session.StartedAtUtc, session.EndedByOperatorId, session.EndedAtUtc,
            session.PaidByOperatorId, session.PaidAtUtc, (SessionStatusContract)session.Status, session.Version);
}
