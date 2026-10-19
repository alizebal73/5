using System.Text.Json;
using GameNet.Server.Infrastructure.Audit;
using GameNet.Server.Modules.Sessions.Contracts;
using GameNet.Server.Modules.Sessions.Domain;
using GameNet.Server.Persistence;
using GameNet.Shared.Contracts.V1.Sessions;
using GameNet.Shared.Primitives;
using Microsoft.EntityFrameworkCore;
using static GameNet.Server.Modules.Sessions.Infrastructure.SessionMapping;

namespace GameNet.Server.Modules.Sessions.Infrastructure;

/// <summary>
/// Billing-facing contract implementation that marks a pending-settlement Session as paid.
/// It only runs inside the Billing mutation transaction and never opens its own.
/// </summary>
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
}
