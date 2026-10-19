using System.Text.Json;
using GameNet.Server.Modules.Sessions.Application;
using GameNet.Server.Modules.Sessions.Domain;
using GameNet.Server.Persistence;
using GameNet.Shared.Contracts.V1.Security;
using GameNet.Shared.Contracts.V1.Sessions;
using GameNet.Shared.Primitives;
using Microsoft.EntityFrameworkCore;

namespace GameNet.Server.Modules.Sessions.Infrastructure;

public sealed partial class SessionService
{
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
    
                        // Manual reconciliation is not an alternate fast path. If the monotonic
                        // baseline is still trustworthy, the operator must use the normal End command.
                        try
                        {
                            var current = clock.Capture();
                            if (session.CurrentSegmentStartedMonotonicTimestamp is long currentSegmentTimestamp &&
                                session.CurrentSegmentStartedMonotonicFrequency is long currentSegmentFrequency &&
                                session.CurrentSegmentStartedSystemUptimeMilliseconds is long currentSegmentUptime &&
                                session.CurrentSegmentStartedBootIdentifier is Guid currentSegmentBoot &&
                                MonotonicElapsedTime.TryCalculateElapsedTicks(
                                    currentSegmentTimestamp, currentSegmentFrequency, currentSegmentUptime, currentSegmentBoot, current,
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
    
                        var activeReservation = await walletFunding.FindActiveReservationAsync(session.Id, token);
                        if (activeReservation is not null)
                        {
                            elapsedTicks = Math.Min(
                                elapsedTicks,
                                PrepaidSessionBudget.CalculateMaximumElapsedTicks(
                                    activeReservation.ReservedAmountToman, session.TariffHourlyRateTomanSnapshot));
                            if (elapsedTicks <= 0)
                                return Fail<InvoicePreviewResponse>("wallet.insufficient_balance",
                                    "The prepaid Wallet reservation does not cover a billable Session duration.");
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
                            session.End(
                                command.Actor.OperatorId, reconciledAtUtc, elapsedTicks,
                                endedBySystem: command.Actor.ActorType == "System");
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
                                command.Actor.ActorType == "System" ? Guid.Empty : command.Actor.OperatorId,
                                reconciledAtUtc,
                                command.Actor.CorrelationId,
                                command.Actor.Source,
                                command.IdempotencyKey,
                                command.Actor.ActorType == "System"),
                            token);
                        if (!invoiceResult.IsSuccess)
                            return Fail<InvoicePreviewResponse>(invoiceResult.Error.Code, invoiceResult.Error.Message);
    
                        return await CompletePrepaidSettlementAsync(
                            session, invoiceResult.Value, command.Actor, reconciledAtUtc, command.IdempotencyKey, token);
                    },
                    cancellationToken);
            }
            catch (PersistenceConflictException)
            {
                return Fail<InvoicePreviewResponse>("sessions.version_conflict", "Session changed concurrently; refresh before reconciling timing.");
            }
        }
}
