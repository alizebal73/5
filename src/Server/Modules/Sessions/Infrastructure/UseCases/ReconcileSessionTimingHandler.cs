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
using static GameNet.Server.Modules.Sessions.Infrastructure.SessionMapping;

namespace GameNet.Server.Modules.Sessions.Infrastructure.UseCases;

/// <summary>
/// Operator-authorized, audited recovery for an active Session whose monotonic timing
/// baseline can no longer be trusted (for example after a Server reboot). It is never an
/// alternate fast path: while timing continuity is still valid the normal End command
/// must be used.
/// </summary>
internal sealed class ReconcileSessionTimingHandler(
    GameNetDbContext db,
    CommandMutationPipeline mutationPipeline,
    IRequestFingerprint requestFingerprint,
    IBillingInvoiceWriter invoices,
    IAuditWriter audit,
    IGameClock clock)
{
    public async Task<Result<InvoicePreviewResponse>> HandleAsync(
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
                SessionMutationPolicy.IdempotencyLease,
                SessionMutationPolicy.IdempotencyRetention,
                async token =>
                {
                    var session = await db.Sessions.SingleOrDefaultAsync(x => x.Id == command.SessionId, token);
                    if (session is null)
                        return Fail<InvoicePreviewResponse>("sessions.not_found", "Session was not found.");
                    if (session.Version != command.ExpectedSessionVersion)
                        return Fail<InvoicePreviewResponse>("sessions.version_conflict", "Session changed; refresh before reconciling timing.");
                    if (session.Status != SessionStatus.Active)
                        return Fail<InvoicePreviewResponse>("sessions.invalid_state", "Only an active Session may enter timing recovery.");

                    // Manual reconciliation is not an alternate fast path. If the monotonic
                    // baseline is still trustworthy, the operator must use the normal End command.
                    try
                    {
                        var current = clock.Capture();
                        if (MonotonicElapsedTime.TryCalculateElapsedTicks(
                                session.StartedMonotonicTimestamp,
                                session.StartedMonotonicFrequency,
                                session.StartedSystemUptimeMilliseconds,
                                session.StartedBootIdentifier,
                                current,
                                out _))
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

                    var before = ToResponse(session);
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
                        ReconciledElapsedMinutes = command.ReconciledElapsedMinutes,
                        Reason = reason,
                        Method = "operator_reconciliation"
                    };
                    audit.Append(SessionAuditRecords.Succeeded(
                        reconciledAtUtc,
                        command.Actor,
                        "sessions.timing.reconciled",
                        session,
                        command.IdempotencyKey,
                        reason,
                        JsonSerializer.Serialize(before),
                        JsonSerializer.Serialize(after)));

                    var invoiceResult = await invoices.CreatePendingAsync(
                        PendingInvoiceRequests.ForEndedSession(session, command.Actor, reconciledAtUtc, command.IdempotencyKey),
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
}
