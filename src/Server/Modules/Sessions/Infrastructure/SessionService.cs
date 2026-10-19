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
using GameNet.Server.Modules.Wallet.Contracts;
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
public sealed partial class SessionService(
    GameNetDbContext db,
    CommandMutationPipeline mutationPipeline,
    IRequestFingerprint requestFingerprint,
    ICustomerSessionAccess customers,
    IStationSessionAccess stations,
    ITariffSessionAccess tariffs,
    IBillingInvoiceWriter invoices,
    IWalletSessionFunding walletFunding,
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

        // Any customer-bound PC Session is prepaid; null CustomerId remains the walk-in/cash path.
        if (command.CustomerId is Guid customerId)
            return StartPrepaidAsync(new StartPrepaidSessionCommand(
                command.StationId, customerId, command.IdempotencyKey, command.Actor), cancellationToken);

        var payload = new { command.StationId, command.TariffId, command.CustomerId, Prepaid = false };
        var scope = $"sessions.start:{command.Actor.ActorType}:{command.Actor.ActorId:D}";
        return StartCoreAsync(command, scope, payload, requirePrepaidWallet: false, cancellationToken);
    }

    public Task<Result<SessionResponse>> StartPrepaidAsync(
        StartPrepaidSessionCommand command,
        CancellationToken cancellationToken)
    {
        if (!command.Actor.Permissions.Contains(Permissions.SessionsOperate))
            return Task.FromResult(Fail<SessionResponse>("sessions.forbidden", "Not allowed to start sessions."));
        if (command.StationId == Guid.Empty || command.CustomerId == Guid.Empty)
            return Task.FromResult(Fail<SessionResponse>("sessions.prepaid_customer_required",
                "A registered customer and target Station are required for a prepaid Session."));

        // Tariff choice is a Server decision, not an amount/rate supplied by the UI or Client.
        var internalCommand = new StartSessionCommand(
            command.StationId, Guid.Empty, command.CustomerId, command.IdempotencyKey, command.Actor);
        var payload = new { command.StationId, command.CustomerId, Prepaid = true };
        var scope = $"sessions.prepaid-start:{command.Actor.ActorType}:{command.Actor.ActorId:D}";
        return StartCoreAsync(internalCommand, scope, payload, requirePrepaidWallet: true, cancellationToken);
    }

    private async Task<Result<SessionResponse>> StartCoreAsync(
        StartSessionCommand command,
        string scope,
        object payload,
        bool requirePrepaidWallet,
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
                    if (station.Type is not (StationTypeContract.Pc or StationTypeContract.Ps5 or StationTypeContract.Foosball))
                        return Fail<SessionResponse>("sessions.station_type_unsupported", "This station type cannot host a timed Session.");
                    if (requirePrepaidWallet && station.Type != StationTypeContract.Pc)
                        return Fail<SessionResponse>("sessions.pc_only", "Prepaid customer Sessions are supported on PC stations only.");
                    if (station.Status != StationStatusContract.Available)
                        return Fail<SessionResponse>("sessions.station_unavailable", "Station is not available.");
                    if (station.Type == StationTypeContract.Pc && !station.AgentOnline)
                        return Fail<SessionResponse>(
                            "sessions.agent_unavailable",
                            "PC Agent is unbound, offline, lacks an active credential, or has no fresh authoritative heartbeat/lease. Session was not started.");

                    var tariff = requirePrepaidWallet
                        ? await tariffs.FindDefaultActiveAsync(StationTypeContract.Pc, token)
                        : await tariffs.FindActiveAsync(command.TariffId, token);
                    if (tariff is null)
                        return Fail<SessionResponse>(
                            requirePrepaidWallet ? "sessions.default_pc_tariff_not_found" : "sessions.tariff_not_found",
                            requirePrepaidWallet ? "No active default PC tariff is configured." : "Active tariff was not found.");
                    if (tariff.StationType != station.Type)
                        return Fail<SessionResponse>("sessions.tariff_station_mismatch", "Tariff type must match the selected station type.");

                    var activeExists = await db.Sessions.AnyAsync(
                        x => x.StationId == station.Id && (x.Status == SessionStatus.Active || x.Status == SessionStatus.Paused), token);
                    if (activeExists)
                        return Fail<SessionResponse>("sessions.station_has_active_session", "Station already has an active session.");

                    var pendingSettlementExists = await db.Sessions.AnyAsync(
                        x => x.StationId == station.Id && x.Status == SessionStatus.PendingSettlement, token);
                    if (pendingSettlementExists)
                        return Fail<SessionResponse>("sessions.station_pending_settlement", "The previous Session must be paid before this station can be started again.");

                    var customer = command.CustomerId is Guid customerId
                        ? await customers.FindActiveAsync(customerId, token)
                        : await customers.CreateWalkInAsync(clock.UtcNow, token);
                    if (customer is null)
                        return Fail<SessionResponse>("sessions.customer_not_found", "Active customer was not found.");
                    if (customer.IsBlocked)
                        return Fail<SessionResponse>("sessions.customer_blocked", "Blocked customers cannot start a Session.");
                    if (requirePrepaidWallet && command.CustomerId is null)
                        return Fail<SessionResponse>("sessions.prepaid_customer_required",
                            "A registered customer is required for a prepaid Session.");

                    GameClockSnapshot startSnapshot;
                    try
                    {
                        startSnapshot = clock.Capture();
                    }
                    catch (Exception ex) when (ex is InvalidOperationException
                        or DllNotFoundException or EntryPointNotFoundException)
                    {
                        return Fail<SessionResponse>(
                            "sessions.timing_recovery_required",
                            "Authoritative timing source is unavailable. The Session was not started.");
                    }

                    var sessionId = Guid.NewGuid();
                    WalletSessionReservation? reservation = null;
                    long? prepaidBudgetTicks = null;
                    if (requirePrepaidWallet)
                    {
                        var reservationActorType = command.Actor.ActorType is "Operator" or "Customer"
                            ? command.Actor.ActorType
                            : "Operator";
                        var reserveResult = await walletFunding.ReserveForSessionAsync(
                            customer.Id, sessionId, command.Actor.ActorId, reservationActorType,
                            command.Actor.CorrelationId, command.Actor.Source, command.IdempotencyKey, token);
                        if (!reserveResult.IsSuccess)
                            return Fail<SessionResponse>(reserveResult.Error.Code, reserveResult.Error.Message);

                        // The Customer row remains locked through commit. This check, after the
                        // lock, prevents two PCs from acquiring active prepaid Sessions for one customer.
                        var customerAlreadyPlaying = await db.Sessions.AnyAsync(
                            x => x.CustomerId == customer.Id && (x.Status == SessionStatus.Active || x.Status == SessionStatus.Paused), token);
                        if (customerAlreadyPlaying)
                            return Fail<SessionResponse>("sessions.customer_has_active_session",
                                "This customer already has an active PC Session.");

                        reservation = reserveResult.Value;
                        prepaidBudgetTicks = PrepaidSessionBudget.CalculateMaximumElapsedTicks(
                            reservation.ReservedAmountToman, tariff.HourlyRateToman);
                        if (prepaidBudgetTicks <= 0)
                            return Fail<SessionResponse>("wallet.insufficient_balance",
                                "Available credit is too small to fund any Session time at this tariff.");
                    }

                    var session = Session.Start(
                        sessionId,
                        station.Id,
                        customer.Id,
                        tariff.Id,
                        tariff.Code,
                        tariff.Name,
                        tariff.HourlyRateToman,
                        command.Actor.ActorType == "Customer" ? Guid.Empty : command.Actor.OperatorId,
                        startSnapshot.UtcNow,
                        startSnapshot.Timestamp,
                        startSnapshot.TimestampFrequency,
                        startSnapshot.SystemUptimeMilliseconds,
                        startSnapshot.BootIdentifier,
                        command.Actor.ActorType == "Customer" ? command.Actor.CustomerId : null);
                    await db.Sessions.AddAsync(session, token);

                    var response = ToResponse(session);
                    if (reservation is not null)
                        response = response with
                        {
                            PrepaidReservedAmountToman = reservation.ReservedAmountToman,
                            PrepaidBudgetTicks = prepaidBudgetTicks
                        };
                    audit.Append(new AuditRecord(
                        clock.UtcNow,
                        command.Actor.ActorType,
                        command.Actor.ActorId.ToString("D"),
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
                cancellationToken,
                requirePrepaidWallet ? System.Data.IsolationLevel.ReadCommitted : System.Data.IsolationLevel.Serializable);
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

        var scope = $"sessions.end:{command.Actor.ActorType}:{command.Actor.ActorId:D}";
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
                    if (session.Status is not (SessionStatus.Active or SessionStatus.Paused))
                        return Fail<InvoicePreviewResponse>("sessions.invalid_state", "Only an active or paused Session can be ended.");

                    DateTimeOffset endedAt;
                    long elapsedTicks;
                    if (session.Status == SessionStatus.Paused)
                    {
                        // Paused time is never billable and no monotonic segment is active.
                        elapsedTicks = session.AccumulatedElapsedTicks;
                        try { endedAt = clock.UtcNow; }
                        catch (Exception ex) when (ex is InvalidOperationException or DllNotFoundException or EntryPointNotFoundException)
                        {
                            return Fail<InvoicePreviewResponse>("sessions.timing_recovery_required", "The Server event clock is unavailable. No invoice was created.");
                        }
                    }
                    else
                    {
                        if (session.CurrentSegmentStartedMonotonicTimestamp is not long segmentTimestamp ||
                            session.CurrentSegmentStartedMonotonicFrequency is not long segmentFrequency ||
                            session.CurrentSegmentStartedSystemUptimeMilliseconds is not long segmentUptime ||
                            session.CurrentSegmentStartedBootIdentifier is not Guid segmentBoot)
                            return Fail<InvoicePreviewResponse>("sessions.timing_recovery_required", "The active timing baseline is missing. No invoice was created.");

                        GameClockSnapshot endSnapshot;
                        try { endSnapshot = clock.Capture(); }
                        catch (Exception ex) when (ex is InvalidOperationException or DllNotFoundException or EntryPointNotFoundException)
                        {
                            return Fail<InvoicePreviewResponse>("sessions.timing_recovery_required", "Authoritative timing source is unavailable. No invoice was created.");
                        }

                        if (!MonotonicElapsedTime.TryCalculateElapsedTicks(
                                segmentTimestamp, segmentFrequency, segmentUptime, segmentBoot, endSnapshot,
                                out var activeSegmentTicks))
                            return Fail<InvoicePreviewResponse>("sessions.timing_recovery_required",
                                "Monotonic time continuity could not be verified. No invoice was created; timing recovery is required.");

                        try { elapsedTicks = checked(session.AccumulatedElapsedTicks + activeSegmentTicks); }
                        catch (OverflowException) { return Fail<InvoicePreviewResponse>("sessions.amount_overflow", "Billable Session duration exceeded the supported range."); }
                        endedAt = endSnapshot.UtcNow;
                    }

                    var activeReservation = await walletFunding.FindActiveReservationAsync(session.Id, token);
                    if (activeReservation is not null)
                    {
                        var maximumElapsed = PrepaidSessionBudget.CalculateMaximumElapsedTicks(
                            activeReservation.ReservedAmountToman, session.TariffHourlyRateTomanSnapshot);
                        if (session.AccumulatedElapsedTicks > maximumElapsed)
                            return Fail<InvoicePreviewResponse>("wallet.prepaid_budget_exceeded",
                                "Accumulated billable time exceeds this Session's prepaid budget; settlement requires review.");
                        // The expiry worker normally ends at the authoritative budget boundary.
                        elapsedTicks = Math.Min(elapsedTicks, maximumElapsed);
                    }

                    var before = ToResponse(session);
                    try
                    {
                        session.End(command.Actor.ActorId, endedAt, elapsedTicks,
                            endedBySystem: command.Actor.ActorType == "System");
                    }
                    catch (InvalidOperationException)
                    {
                        return Fail<InvoicePreviewResponse>("sessions.invalid_state", "Session could not be ended from its current state.");
                    }
                    catch (ArgumentOutOfRangeException)
                    {
                        return Fail<InvoicePreviewResponse>("sessions.timing_recovery_required", "Session elapsed time is invalid; no invoice was created.");
                    }

                    audit.Append(new AuditRecord(
                        endedAt,
                        command.Actor.ActorType,
                        command.Actor.ActorId.ToString("D"),
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

                    var invoiceResult = await invoices.CreatePendingAsync(
                        new CreatePendingInvoiceCommand(
                            Guid.NewGuid(), session.Id, session.StationId, session.CustomerId, session.TariffId,
                            session.TariffCodeSnapshot, session.TariffNameSnapshot, session.TariffHourlyRateTomanSnapshot,
                            session.ElapsedTicks!.Value, session.Version,
                            command.Actor.ActorType == "System" ? Guid.Empty : command.Actor.ActorId,
                            endedAt, command.Actor.CorrelationId, command.Actor.Source, command.IdempotencyKey,
                            command.Actor.ActorType == "System"),
                        token);
                    if (!invoiceResult.IsSuccess)
                        return Fail<InvoicePreviewResponse>(invoiceResult.Error.Code, invoiceResult.Error.Message);

                    return await CompletePrepaidSettlementAsync(
                        session, invoiceResult.Value, command.Actor, endedAt, command.IdempotencyKey, token);
                },
                cancellationToken);
        }
        catch (PersistenceConflictException)
        {
            return Fail<InvoicePreviewResponse>("sessions.version_conflict", "Session changed concurrently; refresh and try again.");
        }
    }


    private async Task<Result<InvoicePreviewResponse>> CompletePrepaidSettlementAsync(
        Session session,
        InvoicePreviewResponse invoice,
        SessionActorContext actor,
        DateTimeOffset settledAtUtc,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var reservation = await walletFunding.FindActiveReservationAsync(session.Id, cancellationToken);
        if (reservation is null)
            return Result<InvoicePreviewResponse>.Success(invoice);
        if (reservation.CustomerId != session.CustomerId ||
            invoice.AmountToman > reservation.ReservedAmountToman)
            return Fail<InvoicePreviewResponse>("sessions.prepaid_budget_exceeded",
                "The Session charge exceeds its valid prepaid reservation; Session settlement was rolled back.");

        var ledgerActorType = actor.ActorType;
        var ledgerActorId = actor.ActorId;
        var walletResult = await walletFunding.SettleSessionAsync(
            session.CustomerId, session.Id, invoice.AmountToman, ledgerActorId, ledgerActorType,
            actor.CorrelationId, actor.Source, idempotencyKey, cancellationToken);
        if (!walletResult.IsSuccess)
        {
            var errorCode = walletResult.Error.Code == "wallet.prepaid_budget_exceeded"
                ? "sessions.prepaid_budget_exceeded"
                : walletResult.Error.Code;
            return Fail<InvoicePreviewResponse>(errorCode, walletResult.Error.Message);
        }

        var invoicePaid = await invoices.MarkPrepaidPaidAsync(
            invoice.InvoiceId, session.Version, session.CustomerId, settledAtUtc,
            actor.CorrelationId, actor.Source, idempotencyKey, cancellationToken);
        if (!invoicePaid.IsSuccess)
            return Fail<InvoicePreviewResponse>(invoicePaid.Error.Code, invoicePaid.Error.Message);

        var beforePaid = ToResponse(session);
        try
        {
            session.MarkPaid(Guid.Empty, settledAtUtc, session.CustomerId);
        }
        catch (InvalidOperationException)
        {
            return Fail<InvoicePreviewResponse>("sessions.invalid_state",
                "Prepaid Session could not enter its paid state; all financial changes were rolled back.");
        }

        audit.Append(new AuditRecord(
            clock.UtcNow,
            ledgerActorType,
            ledgerActorId.ToString("D"),
            "sessions.prepaid_settled",
            "Session",
            session.Id.ToString("D"),
            null,
            actor.CorrelationId,
            actor.Source,
            "Succeeded",
            idempotencyKey,
            JsonSerializer.Serialize(beforePaid),
            JsonSerializer.Serialize(ToResponse(session))));

        return Result<InvoicePreviewResponse>.Success(invoicePaid.Value with
        {
            SessionVersion = session.Version,
            Status = InvoiceStatusContract.Paid,
            PaidAtUtc = settledAtUtc
        });
    }

    private static bool IsValidTimingRecoveryReason(string? reason) =>
        !string.IsNullOrWhiteSpace(reason) &&
        reason.Trim().Length is >= 3 and <= 256 &&
        !reason.Any(char.IsControl);

    public async Task<Result<IReadOnlyList<SessionResponse>>> ListActiveAsync(
        SessionActorContext actor,
        Guid? stationId,
        CancellationToken cancellationToken)
    {
        if (!actor.Permissions.Contains(Permissions.SessionsOperate))
            return Fail<IReadOnlyList<SessionResponse>>("sessions.forbidden", "Not allowed to read sessions.");

        var query = db.Sessions.AsNoTracking().Where(x => x.Status == SessionStatus.Active || x.Status == SessionStatus.Paused);
        if (stationId.HasValue)
            query = query.Where(x => x.StationId == stationId.Value);

        var rows = await query.OrderBy(x => x.StartedAtUtc).ToArrayAsync(cancellationToken);
        var responses = new List<SessionResponse>(rows.Length);
        foreach (var row in rows)
        {
            var response = ToResponse(row);
            var reservation = await walletFunding.FindActiveReservationAsync(row.Id, cancellationToken);
            if (reservation is not null)
                response = response with
                {
                    PrepaidReservedAmountToman = reservation.ReservedAmountToman,
                    PrepaidBudgetTicks = PrepaidSessionBudget.CalculateMaximumElapsedTicks(
                        reservation.ReservedAmountToman, row.TariffHourlyRateTomanSnapshot)
                };
            responses.Add(response);
        }
        return Result<IReadOnlyList<SessionResponse>>.Success(responses);
    }

    private static SessionResponse ToResponse(Session session) =>
        new(session.Id, session.StationId, session.CustomerId, session.TariffId,
            session.TariffCodeSnapshot, session.TariffNameSnapshot, session.TariffHourlyRateTomanSnapshot,
            session.StartedByOperatorId, session.StartedAtUtc, session.EndedByOperatorId, session.EndedAtUtc,
            session.PaidByOperatorId, session.PaidAtUtc, (SessionStatusContract)session.Status, session.Version,
            StartedByCustomerId: session.StartedByCustomerId,
            EndedBySystem: session.EndedBySystem,
            PaidByCustomerId: session.PaidByCustomerId,
            AccumulatedElapsedTicks: session.AccumulatedElapsedTicks);

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
            session.PaidByOperatorId, session.PaidAtUtc, (SessionStatusContract)session.Status, session.Version,
            StartedByCustomerId: session.StartedByCustomerId,
            EndedBySystem: session.EndedBySystem,
            PaidByCustomerId: session.PaidByCustomerId,
            AccumulatedElapsedTicks: session.AccumulatedElapsedTicks);
}
