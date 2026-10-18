namespace GameNet.Server.Infrastructure.Outbox;

public interface IOutboxDispatcher
{
    Task<IReadOnlyList<OutboxMessage>> ClaimBatchAsync(
        int batchSize,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default,
        int maxAttempts = 10);

    Task MarkPublishedAsync(
        long messageId,
        string leaseToken,
        DateTimeOffset publishedAtUtc,
        CancellationToken cancellationToken = default);

    Task RecordFailureAsync(
        long messageId,
        string leaseToken,
        string failureCode,
        DateTimeOffset failedAtUtc,
        DateTimeOffset retryAtUtc,
        int maxAttempts,
        bool permanentFailure,
        CancellationToken cancellationToken = default);
}
