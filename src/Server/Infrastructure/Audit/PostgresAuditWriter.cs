using GameNet.Server.Application;
using GameNet.Server.Infrastructure.Persistence;
using GameNet.Server.Persistence;

namespace GameNet.Server.Infrastructure.Audit;

public sealed class PostgresAuditWriter(GameNetDbContext db) : IAuditWriter
{
    public void Append(AuditRecord record)
    {
        db.AuditEntries.Add(new AuditEntry
        {
            OccurredAtUtc = record.OccurredAtUtc,
            ActorType = record.ActorType,
            ActorId = record.ActorId,
            Operation = record.Operation,
            ReferenceType = record.ReferenceType,
            ReferenceId = record.ReferenceId,
            Reason = record.Reason,
            BeforeJson = record.BeforeJson,
            AfterJson = record.AfterJson,
            Source = record.Source,
            CommandId = record.CommandId,
            IdempotencyKey = record.IdempotencyKey,
            Outcome = record.Outcome
        });
    }
}
