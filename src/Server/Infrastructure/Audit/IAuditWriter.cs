namespace GameNet.Server.Infrastructure.Audit;

public interface IAuditWriter
{
    void Append(AuditRecord record);
}

public sealed record AuditRecord(DateTimeOffset OccurredAtUtc,string ActorType,string? ActorId,string Operation,string ReferenceType,string ReferenceId,string? Reason,string? BeforeJson,string? AfterJson);
