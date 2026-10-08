using GameNet.Server.Persistence;

namespace GameNet.Server.Infrastructure.Audit;

public sealed class EfAuditWriter(GameNetDbContext dbContext) : IAuditWriter
{
    public void Append(AuditRecord record)
    {
        dbContext.AuditEntries.Add(new AuditEntry
        {
            OccurredAtUtc = record.OccurredAtUtc,
            ActorType = record.ActorType,
            ActorId = record.ActorId,
            Operation = record.Operation,
            ReferenceType = record.ReferenceType,
            ReferenceId = record.ReferenceId,
            Reason = record.Reason,
            CorrelationId = record.CorrelationId,
            Source = record.Source,
            Outcome = record.Outcome,
            IdempotencyKey = record.IdempotencyKey,
            BeforeJson = record.BeforeJson,
            AfterJson = record.AfterJson
        });
    }
}
