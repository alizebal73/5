namespace GameNet.Server.Infrastructure.Outbox;

/// <summary>
/// Executes a local consumer operation at most once per EventId in this database.
/// The receipt and handler's local database effects commit in one transaction.
/// Handler code must not make network, SignalR, file-system or other external side effects
/// in this transaction; instead append a follow-up Outbox event and dispatch it separately.
/// </summary>
public interface IOutboxInbox
{
    Task<bool> ExecuteOnceAsync(
        long outboxMessageId,
        string leaseToken,
        OutboxDeliveryEnvelope message,
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken = default);
}
