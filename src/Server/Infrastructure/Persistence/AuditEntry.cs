namespace GameNet.Server.Infrastructure.Persistence;

public sealed class AuditEntry
{
    public long Id { get; set; }
    public DateTimeOffset OccurredAtUtc { get; set; }
    public string ActorType { get; set; } = null!;
    public string? ActorId { get; set; }
    public string Operation { get; set; } = null!;
    public string ReferenceType { get; set; } = null!;
    public string ReferenceId { get; set; } = null!;
    public string? Reason { get; set; }
    public string? BeforeJson { get; set; }
    public string? AfterJson { get; set; }
    public string Source { get; set; } = null!;
    public string? CommandId { get; set; }
    public string? IdempotencyKey { get; set; }
    public string Outcome { get; set; } = null!;
}
