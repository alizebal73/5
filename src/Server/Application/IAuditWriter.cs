namespace GameNet.Server.Application;

public interface IAuditWriter
{
    void Append(AuditRecord record);
}

public sealed record AuditRecord(
    DateTimeOffset OccurredAtUtc,
    string ActorType,
    string? ActorId,
    string Operation,
    string ReferenceType,
    string ReferenceId,
    string? Reason,
    string? BeforeJson,
    string? AfterJson,
    string Source,
    string? CommandId,
    string? IdempotencyKey,
    string Outcome);
