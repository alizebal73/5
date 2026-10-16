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
                    if (!station.AgentOnline)
                        return Fail<SessionResponse>(
                            "sessions.agent_unavailable",
                            "PC Agent is unbound, offline, lacks an active credential, or has no fresh authoritative heartbeat/lease. Session was not started.");

                    var tariff = await tariffs.FindActiveAsync(command.TariffId, token);
                    if (tariff is null)
                        return Fail<SessionResponse>("sessions.tariff_not_found", "Active tariff was not found.");
                    if (tariff.StationType != StationTypeContract.Pc)
                        return Fail<SessionResponse>("sessions.tariff_station_mismatch", "Tariff is not for PC stations.");

                    var activeExists = await db.Sessions.AnyAsync(
                        x => x.StationId == station.Id && (x.Status == SessionStatus.Active || x.Status == SessionStatus.Paused), token);
                    if (activeExists)
                        return Fail<SessionResponse>("sessions.station_has_active_session", "Station already has an active or paused Session.");

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

                    var session = Session.Start(
                        Guid.NewGuid(),
                        station.Id,
                        customer.Id,
                        tariff.Id,
                        tariff.Code,
                        tariff.Name,
                        tariff.HourlyRateToman,
                        command.Actor.OperatorId,
                        startSnapshot.UtcNow,
                        startSnapshot.Timestamp,
                        startSnapshot.TimestampFrequency,
                        startSnapshot.SystemUptimeMilliseconds,
                        startSnapshot.BootIdentifier);
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
                    if (session.Status is not (SessionStatus.Active or SessionStatus.Paused))
                        return Fail<InvoicePreviewResponse>("sessions.invalid_state", "Only an active or paused session can be ended.");

                    long elapsedTicks;
                    DateTimeOffset endedAt;
                    if (session.Status == SessionStatus.Paused)
                    {
                        elapsedTicks = session.AccumulatedElapsedTicks;
                        endedAt = clock.UtcNow;
                    }
                    else
                    {
                        if (session.CurrentSegmentStartedMonotonicTimestamp is not long segmentTimestamp ||
                            session.CurrentSegmentStartedMonotonicFrequency is not long segmentFrequency ||
                            session.CurrentSegmentStartedSystemUptimeMilliseconds is not long segmentUptime ||
                            session.CurrentSegmentStartedBootIdentifier is not Guid segmentBoot)
                            return Fail<InvoicePreviewResponse>("sessions.timing_recovery_required", "The active timing baseline is missing; no invoice was created.");

                        GameClockSnapshot endSnapshot;
                        try
                        {
                            endSnapshot = clock.Capture();
                        }
                        catch (Exception ex) when (ex is InvalidOperationException
                            or DllNotFoundException or EntryPointNotFoundException)
                        {
                            return Fail<InvoicePreviewResponse>(
                                "sessions.timing_recovery_required",
                                "Authoritative timing source is unavailable. No invoice was created.");
                        }

                        if (!MonotonicElapsedTime.TryCalculateElapsedTicks(
                                segmentTimestamp, segmentFrequency, segmentUptime, segmentBoot,
                                endSnapshot, out var currentSegmentTicks))
                        {
                            return Fail<InvoicePreviewResponse>(
                                "sessions.timing_recovery_required",
                                "Monotonic time continuity could not be verified. No invoice was created; timing recovery is required.");
                        }

                        try { elapsedTicks = checked(session.AccumulatedElapsedTicks + currentSegmentTicks); }
                        catch (OverflowException)
                        {
                            return Fail<InvoicePreviewResponse>("sessions.amount_overflow", "Billable Session duration exceeded the supported range.");
                        }
                        endedAt = endSnapshot.UtcNow;
                    }

                    if (elapsedTicks <= 0)
                        return Fail<InvoicePreviewResponse>("sessions.timing_recovery_invalid", "Billable Session duration must be positive.");
                    var before = new
                    {
                        Session = ToResponse(session),
                        AccumulatedElapsedTicks = session.AccumulatedElapsedTicks,
                        FinalSegmentElapsedTicks = elapsedTicks - session.AccumulatedElapsedTicks
                    };
                    try
                    {
                        session.End(command.Actor.OperatorId, endedAt, elapsedTicks);
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
                        JsonSerializer.Serialize(new
                        {
                            Session = ToResponse(session),
                            BillableElapsedTicks = elapsedTicks,
                            AccumulatedElapsedTicks = session.AccumulatedElapsedTicks
                        })));

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
                            session.ElapsedTicks!.Value,
                            session.Version,
                            command.Actor.OperatorId,
                            endedAt,
                            command.Actor.CorrelationId,
                            command.Actor.Source,
                            command.IdempotencyKey),
                        token);
                    if (!invoiceResult.IsSuccess)
                        return Fail<InvoicePreviewResponse>(invoiceResult.Error.Code, invoiceResult.Error.Message);



                    return invoiceResult;
                },
                cancellationToken);
        }
        catch (PersistenceConflictException)
        {
            return Fail<InvoicePreviewResponse>("sessions.version_conflict", "Session changed concurrently; refresh and try again.");
        }
    }

    public async Task<Result<SessionResponse>> PauseAsync(
        PauseSessionCommand command,
        CancellationToken cancellationToken)
    {
        if (!command.Actor.Permissions.Contains(Permissions.SessionsOperate))
            return Fail<SessionResponse>("sessions.forbidden", "Not allowed to pause sessions.");

        try
        {
            return await mutationPipeline.ExecuteAsync(
                $"sessions.pause:{command.Actor.OperatorId:D}", "session.pause",
                command.IdempotencyKey,
                requestFingerprint.Compute(new { command.SessionId, command.ExpectedVersion }),
                200, Lease, Retention,
                async token =>
                {
                    var session = await db.Sessions.SingleOrDefaultAsync(x => x.Id == command.SessionId, token);
                    if (session is null)
                        return Fail<SessionResponse>("sessions.not_found", "Session was not found.");
                    if (session.Version != command.ExpectedVersion)
                        return Fail<SessionResponse>("sessions.version_conflict", "Session changed; refresh before pausing it.");
                    if (session.Status != SessionStatus.Active)
                        return Fail<SessionResponse>("sessions.invalid_state", "Only an active Session can be paused.");

                    if (session.CurrentSegmentStartedMonotonicTimestamp is not long segmentTimestamp ||
                        session.CurrentSegmentStartedMonotonicFrequency is not long segmentFrequency ||
                        session.CurrentSegmentStartedSystemUptimeMilliseconds is not long segmentUptime ||
                        session.CurrentSegmentStartedBootIdentifier is not Guid segmentBoot)
                        return Fail<SessionResponse>("sessions.timing_recovery_required", "The active timing baseline is missing; no state changed.");

                    GameClockSnapshot snapshot;
                    try { snapshot = clock.Capture(); }
                    catch (Exception ex) when (ex is InvalidOperationException or DllNotFoundException or EntryPointNotFoundException)
                    {
                        return Fail<SessionResponse>("sessions.timing_recovery_required", "Authoritative timing source is unavailable; no state changed.");
                    }

                    if (!MonotonicElapsedTime.TryCalculateElapsedTicks(
                            segmentTimestamp, segmentFrequency, segmentUptime, segmentBoot, snapshot,
                            out var segmentTicks))
                        return Fail<SessionResponse>("sessions.timing_recovery_required", "Monotonic continuity cannot be proven. Recover timing or leave the Session unchanged.");

                    var before = new
                    {
                        Session = ToResponse(session),
                        AccumulatedElapsedTicks = session.AccumulatedElapsedTicks
                    };
                    try { session.Pause(command.Actor.OperatorId, segmentTicks); }
                    catch (InvalidOperationException) { return Fail<SessionResponse>("sessions.invalid_state", "Session could not be paused from its current state."); }
                    catch (ArgumentOutOfRangeException) { return Fail<SessionResponse>("sessions.timing_recovery_invalid", "Active segment duration is invalid."); }
                    catch (OverflowException) { return Fail<SessionResponse>("sessions.amount_overflow", "Accumulated billable Session duration exceeded the supported range."); }

                    var after = new
                    {
                        Session = ToResponse(session),
                        AccumulatedElapsedTicks = session.AccumulatedElapsedTicks,
                        PausedSegmentElapsedTicks = segmentTicks
                    };
                    audit.Append(new AuditRecord(
                        snapshot.UtcNow, "Operator", command.Actor.OperatorId.ToString("D"), "sessions.pause",
                        "Session", session.Id.ToString("D"), null, command.Actor.CorrelationId,
                        command.Actor.Source, "Succeeded", command.IdempotencyKey,
                        JsonSerializer.Serialize(before), JsonSerializer.Serialize(after)));
                    return Result<SessionResponse>.Success(after);
                },
                cancellationToken);
        }
        catch (PersistenceConflictException)
        {
            return Fail<SessionResponse>("sessions.version_conflict", "Session changed concurrently; refresh and try again.");
        }
    }

    public async Task<Result<SessionResponse>> ResumeAsync(
        ResumeSessionCommand command,
        CancellationToken cancellationToken)
    {
        if (!command.Actor.Permissions.Contains(Permissions.SessionsOperate))
            return Fail<SessionResponse>("sessions.forbidden", "Not allowed to resume sessions.");

        try
        {
            return await mutationPipeline.ExecuteAsync(
                $"sessions.resume:{command.Actor.OperatorId:D}", "session.resume",
                command.IdempotencyKey,
                requestFingerprint.Compute(new { command.SessionId, command.ExpectedVersion }),
                200, Lease, Retention,
                async token =>
                {
                    var session = await db.Sessions.SingleOrDefaultAsync(x => x.Id == command.SessionId, token);
                    if (session is null)
                        return Fail<SessionResponse>("sessions.not_found", "Session was not found.");
                    if (session.Version != command.ExpectedVersion)
                        return Fail<SessionResponse>("sessions.version_conflict", "Session changed; refresh before resuming it.");
                    if (session.Status != SessionStatus.Paused)
                        return Fail<SessionResponse>("sessions.invalid_state", "Only a paused Session can be resumed.");

                    GameClockSnapshot snapshot;
                    try { snapshot = clock.Capture(); }
                    catch (Exception ex) when (ex is InvalidOperationException or DllNotFoundException or EntryPointNotFoundException)
                    {
                        return Fail<SessionResponse>("sessions.timing_recovery_required", "Authoritative timing source is unavailable; the Session remains paused.");
                    }

                    var before = new
                    {
                        Session = ToResponse(session),
                        AccumulatedElapsedTicks = session.AccumulatedElapsedTicks
                    };
                    try { session.Resume(command.Actor.OperatorId, snapshot); }
                    catch (InvalidOperationException) { return Fail<SessionResponse>("sessions.invalid_state", "Session could not be resumed from its current state."); }
                    catch (ArgumentException) { return Fail<SessionResponse>("sessions.timing_recovery_required", "Clock snapshot is invalid; the Session remains paused."); }

                    var after = new
                    {
                        Session = ToResponse(session),
                        AccumulatedElapsedTicks = session.AccumulatedElapsedTicks,
                        NewSegmentBootIdentifier = snapshot.BootIdentifier
                    };
                    audit.Append(new AuditRecord(
                        snapshot.UtcNow, "Operator", command.Actor.OperatorId.ToString("D"), "sessions.resume",
                        "Session", session.Id.ToString("D"), null, command.Actor.CorrelationId,
                        command.Actor.Source, "Succeeded", command.IdempotencyKey,
                        JsonSerializer.Serialize(before), JsonSerializer.Serialize(after)));
                    return Result<SessionResponse>.Success(after);
                },
                cancellationToken);
        }
        catch (PersistenceConflictException)
        {
            return Fail<SessionResponse>("sessions.version_conflict", "Session changed concurrently; refresh and try again.");
        }
    }

    public async Task<Result<InvoicePreviewResponse>> ReconcileTimingAsync(
        ReconcileSessionTimingCommand command,
        CancellationToken cancellationToken)
    {
        if (!command.Actor.Permissions.Contains(Permissions.SessionsOperate) ||
            !command.Actor.Permissions.Contains(Permissions.SessionsTimingRecover))
            return Fail<InvoicePreviewResponse>("sessions.forbidden", "Not allowed to reconcile Session timing.");

        var reason = command.Reason?.Trim() ?? string.Empty;
        if (!command.Confirmed ||
            command.ReconciledElapsedMinutes is < 1 or > 43_200 ||
            !IsValidTimingRecoveryReason(reason))
            return Fail<InvoicePreviewResponse>(
                "sessions.timing_recovery_invalid",
                "Explicit confirmation, a duration from 1 through 43200 whole minutes, and a printable reason of 3–256 characters are required.");

        var elapsedTicks = checked(command.ReconciledElapsedMinutes * TimeSpan.TicksPerMinute);
        var payload = new
        {
            command.SessionId,
            command.ExpectedSessionVersion,
            command.ReconciledElapsedMinutes,
            Reason = reason,
            command.Confirmed
        };
        var scope = $"sessions.timing.recovery:{command.Actor.OperatorId:D}";

        try
        {
            return await mutationPipeline.ExecuteAsync(
                scope,
                "session.timing.recovery",
                command.IdempotencyKey,
                requestFingerprint.Compute(payload),
                200,
                Lease,
                Retention,
                async token =>
                {
                    var session = await db.Sessions.SingleOrDefaultAsync(x => x.Id == command.SessionId, token);
                    if (session is null)
                        return Fail<InvoicePreviewResponse>("sessions.not_found", "Session was not found.");
                    if (session.Version != command.ExpectedSessionVersion)
                        return Fail<InvoicePreviewResponse>("sessions.version_conflict", "Session changed; refresh before reconciling timing.");
                    if (session.Status != SessionStatus.Active)
                        return Fail<InvoicePreviewResponse>("sessions.invalid_state", "Only an active Session may enter timing recovery.");
                    if (elapsedTicks < session.AccumulatedElapsedTicks)
                        return Fail<InvoicePreviewResponse>("sessions.timing_recovery_invalid", "Reconciled billable duration cannot be less than already recorded active segments.");

                    // Manual reconciliation is not an alternate fast path. If the monotonic
                    // baseline is still trustworthy, the operator must use the normal End command.
                    try
                    {
                        var current = clock.Capture();
                        if (session.CurrentSegmentStartedMonotonicTimestamp is long segmentTimestamp &&
                            session.CurrentSegmentStartedMonotonicFrequency is long segmentFrequency &&
                            session.CurrentSegmentStartedSystemUptimeMilliseconds is long segmentUptime &&
                            session.CurrentSegmentStartedBootIdentifier is Guid segmentBoot &&
                            MonotonicElapsedTime.TryCalculateElapsedTicks(
                                segmentTimestamp, segmentFrequency, segmentUptime, segmentBoot, current, out _))
                            return Fail<InvoicePreviewResponse>(
                                "sessions.timing_recovery_not_required",
                                "Timing continuity is valid. End the Session using the normal monotonic timing path.");
                    }
                    catch (Exception ex) when (ex is InvalidOperationException
                        or DllNotFoundException or EntryPointNotFoundException)
                    {
                        // Timing-source failure is a valid reason for an explicitly authorized,
                        // audited manual reconciliation; it never permits an automatic estimate.
                    }

                    DateTimeOffset reconciledAtUtc;
                    try
                    {
                        reconciledAtUtc = clock.UtcNow;
                    }
                    catch (Exception ex) when (ex is InvalidOperationException
                        or DllNotFoundException or EntryPointNotFoundException)
                    {
                        return Fail<InvoicePreviewResponse>(
                            "sessions.timing_recovery_required",
                            "The Server UTC event clock is unavailable. No invoice was created.");
                    }

                    var before = new
                    {
                        Session = ToResponse(session),
                        PreviouslyAccumulatedElapsedTicks = session.AccumulatedElapsedTicks,
                        ReconciledBillableElapsedTicks = elapsedTicks
                    };
                    try
                    {
                        session.End(command.Actor.OperatorId, reconciledAtUtc, elapsedTicks);
                    }
                    catch (InvalidOperationException)
                    {
                        return Fail<InvoicePreviewResponse>("sessions.invalid_state", "Session could not be reconciled from its current state.");
                    }
                    catch (ArgumentOutOfRangeException)
                    {
                        return Fail<InvoicePreviewResponse>("sessions.timing_recovery_invalid", "Reconciled elapsed duration is invalid.");
                    }

                    var after = new
                    {
                        Session = ToResponse(session),
                        AccumulatedElapsedTicks = session.AccumulatedElapsedTicks,
                        ReconciledElapsedMinutes = command.ReconciledElapsedMinutes,
                        Reason = reason,
                        Method = "operator_reconciliation"
                    };
                    audit.Append(new AuditRecord(
                        reconciledAtUtc,
                        "Operator",
                        command.Actor.OperatorId.ToString("D"),
                        "sessions.timing.reconciled",
                        "Session",
                        session.Id.ToString("D"),
                        reason,
                        command.Actor.CorrelationId,
                        command.Actor.Source,
                        "Succeeded",
                        command.IdempotencyKey,
                        JsonSerializer.Serialize(before),
                        JsonSerializer.Serialize(after)));

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
                            session.ElapsedTicks!.Value,
                            session.Version,
                            command.Actor.OperatorId,
                            reconciledAtUtc,
                            command.Actor.CorrelationId,
                            command.Actor.Source,
                            command.IdempotencyKey),
                        token);
                    if (!invoiceResult.IsSuccess)
                        return Fail<InvoicePreviewResponse>(invoiceResult.Error.Code, invoiceResult.Error.Message);

                    return invoiceResult;
                },
                cancellationToken);
        }
        catch (PersistenceConflictException)
        {
            return Fail<InvoicePreviewResponse>("sessions.version_conflict", "Session changed concurrently; refresh before reconciling timing.");
        }
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
