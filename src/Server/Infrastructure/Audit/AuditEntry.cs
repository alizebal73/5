namespace GameNet.Server.Infrastructure.Audit;

public sealed class AuditEntry
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public DateTimeOffset OccurredAtUtc { get; init; }
    public string ActorType { get; init; } = string.Empty;
    public string? ActorId { get; init; }
    public string Operation { get; init; } = string.Empty;
    public string? ReferenceType { get; init; }
    public string? ReferenceId { get; init; }
    public string? Reason { get; init; }
    public string CorrelationId { get; init; } = string.Empty;
    public string Source { get; init; } = string.Empty;
    public string Outcome { get; init; } = string.Empty;
    public string? IdempotencyKey { get; init; }
    public string? BeforeJson { get; init; }
    public string? AfterJson { get; init; }
}
