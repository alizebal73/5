namespace GameNet.Server.Infrastructure.Outbox;

public sealed class OutboxMessage
{
    public long Id { get; set; }
    public Guid EventId { get; init; } = Guid.NewGuid();
    public DateTimeOffset OccurredAtUtc { get; init; }
    public string Type { get; init; } = string.Empty;
    public string PayloadJson { get; init; } = string.Empty;
    public DateTimeOffset? PublishedAtUtc { get; private set; }
    public int AttemptCount { get; private set; }
    public DateTimeOffset? NextAttemptAtUtc { get; private set; }
    public string? LastFailureCode { get; private set; }
    public DateTimeOffset? DeadLetteredAtUtc { get; private set; }
    public string? LeaseToken { get; private set; }
    public DateTimeOffset? LeaseExpiresAtUtc { get; private set; }

    public void Claim(string leaseToken, DateTimeOffset leaseExpiresAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(leaseToken);
        if (PublishedAtUtc is not null || DeadLetteredAtUtc is not null)
            throw new InvalidOperationException("OUTBOX_MESSAGE_IS_TERMINAL");

        AttemptCount = checked(AttemptCount + 1);
        NextAttemptAtUtc = null;
        LeaseToken = leaseToken;
        LeaseExpiresAtUtc = leaseExpiresAtUtc;
    }

    public void MarkPublished(DateTimeOffset publishedAtUtc)
    {
        if (PublishedAtUtc is not null || DeadLetteredAtUtc is not null)
            throw new InvalidOperationException("OUTBOX_MESSAGE_IS_TERMINAL");

        PublishedAtUtc = publishedAtUtc;
        NextAttemptAtUtc = null;
        LastFailureCode = null;
        LeaseToken = null;
        LeaseExpiresAtUtc = null;
    }
}
