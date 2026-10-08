namespace GameNet.Server.Infrastructure.Persistence;

public sealed class IdempotencyEntry
{
    public string Scope { get; set; } = null!;
    public string Key { get; set; } = null!;
    public string Operation { get; set; } = null!;
    public string RequestHash { get; set; } = null!;
    public string LeaseToken { get; set; } = null!;
    public DateTimeOffset ClaimedAtUtc { get; set; }
    public bool Completed { get; set; }
    public int? StatusCode { get; set; }
    public string? ResponseJson { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
}
