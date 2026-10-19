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
/// End an active Session using verified monotonic elapsed time, then create the pending
/// invoice in the same idempotent transaction. If timing continuity cannot be proven the
/// command fails closed and no invoice is created.
/// </summary>
internal sealed class EndSessionHandler(
    GameNetDbContext db,
    CommandMutationPipeline mutationPipeline,
    IRequestFingerprint requestFingerprint,
    IBillingInvoiceWriter invoices,
    IAuditWriter audit,
    IGameClock clock)
{
    public async Task<Result<InvoicePreviewResponse>> HandleAsync(
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
                SessionMutationPolicy.IdempotencyLease,
                SessionMutationPolicy.IdempotencyRetention,
                async token =>
                {
                    var session = await db.Sessions.SingleOrDefaultAsync(x => x.Id == command.SessionId, token);
                    if (session is null)
                        return Fail<InvoicePreviewResponse>("sessions.not_found", "Session was not found.");
                    if (session.Version != command.ExpectedVersion)
                        return Fail<InvoicePreviewResponse>("sessions.version_conflict", "Session changed; refresh before ending it.");
                    if (session.Status != SessionStatus.Active)
                        return Fail<InvoicePreviewResponse>("sessions.invalid_state", "Only an active session can be ended.");

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
                            session.StartedMonotonicTimestamp,
                            session.StartedMonotonicFrequency,
                            session.StartedSystemUptimeMilliseconds,
                            session.StartedBootIdentifier,
                            endSnapshot,
                            out var elapsedTicks))
                    {
                        return Fail<InvoicePreviewResponse>(
                            "sessions.timing_recovery_required",
                            "Monotonic time continuity could not be verified. No invoice was created; timing recovery is required.");
                    }

                    var endedAt = endSnapshot.UtcNow;
                    var before = ToResponse(session);
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

                    audit.Append(SessionAuditRecords.Succeeded(
                        clock.UtcNow,
                        command.Actor,
                        "sessions.end",
                        session,
                        command.IdempotencyKey,
                        null,
                        JsonSerializer.Serialize(before),
                        JsonSerializer.Serialize(ToResponse(session))));

                    var invoiceResult = await invoices.CreatePendingAsync(
                        PendingInvoiceRequests.ForEndedSession(session, command.Actor, endedAt, command.IdempotencyKey),
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
}
