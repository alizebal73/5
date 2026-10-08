namespace GameNet.Server.Infrastructure.Outbox;

public interface IOutboxDispatcher
{
    Task<IReadOnlyList<OutboxMessage>> ClaimBatchAsync(
        int batchSize,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default);

    Task MarkPublishedAsync(
        long messageId,
        string leaseToken,
        DateTimeOffset publishedAtUtc,
        CancellationToken cancellationToken = default);
}
