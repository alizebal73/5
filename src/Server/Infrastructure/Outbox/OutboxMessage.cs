namespace GameNet.Server.Infrastructure.Outbox;

public sealed class OutboxMessage
{
    public long Id { get; set; }
    public Guid EventId { get; init; } = Guid.NewGuid();
    public DateTimeOffset OccurredAtUtc { get; init; }
    public string Type { get; init; } = string.Empty;
    public string PayloadJson { get; init; } = string.Empty;
    public DateTimeOffset? PublishedAtUtc { get; private set; }
    public string? LeaseToken { get; private set; }
    public DateTimeOffset? LeaseExpiresAtUtc { get; private set; }

    public void Claim(string leaseToken, DateTimeOffset leaseExpiresAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(leaseToken);
        LeaseToken = leaseToken;
        LeaseExpiresAtUtc = leaseExpiresAtUtc;
    }

    public void MarkPublished(DateTimeOffset publishedAtUtc)
    {
        PublishedAtUtc = publishedAtUtc;
        LeaseToken = null;
        LeaseExpiresAtUtc = null;
    }
}
