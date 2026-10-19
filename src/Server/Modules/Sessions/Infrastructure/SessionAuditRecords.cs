using GameNet.Server.Infrastructure.Audit;
using GameNet.Server.Modules.Sessions.Application;
using GameNet.Server.Modules.Sessions.Domain;

namespace GameNet.Server.Modules.Sessions.Infrastructure;

/// <summary>
/// Builds the operator audit record shared by Session commands. Callers serialize the
/// before/after payloads themselves so the stored JSON stays exactly what each use case
/// has always produced.
/// </summary>
internal static class SessionAuditRecords
{
    public static AuditRecord Succeeded(
        DateTimeOffset occurredAtUtc,
        SessionActorContext actor,
        string operation,
        Session session,
        string idempotencyKey,
        string? reason,
        string? beforeJson,
        string? afterJson) =>
        new(
            occurredAtUtc,
            "Operator",
            actor.OperatorId.ToString("D"),
            operation,
            "Session",
            session.Id.ToString("D"),
            reason,
            actor.CorrelationId,
            actor.Source,
            "Succeeded",
            idempotencyKey,
            beforeJson,
            afterJson);
}
