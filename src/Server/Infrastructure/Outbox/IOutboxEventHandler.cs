namespace GameNet.Server.Infrastructure.Outbox;

/// <summary>
/// Handles one versioned event type inside the durable Inbox transaction. The Inbox receipt
/// and local database changes commit atomically; failed handlers leave no receipt and may retry.
/// Handlers must use the caller's scoped persistence context, must not start a nested transaction,
/// and must not perform network, SignalR, file-system or other external side effects. Write a
/// follow-up Outbox row in the current transaction for those effects. Delivery remains
/// at-least-once across external boundaries; remote consumers must deduplicate by a stable ID.
/// </summary>
public interface IOutboxEventHandler
{
    string EventType { get; }

    Task HandleAsync(OutboxDeliveryEnvelope message, CancellationToken cancellationToken = default);
}

public sealed record OutboxDeliveryEnvelope(
    Guid EventId,
    DateTimeOffset OccurredAtUtc,
    string Type,
    string PayloadJson);
