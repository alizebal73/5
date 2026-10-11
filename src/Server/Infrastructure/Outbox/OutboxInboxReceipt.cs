namespace GameNet.Server.Infrastructure.Outbox;

/// <summary>
/// Durable idempotency receipt for an Outbox event. Written in the same transaction
/// as the local effects of its handler.
/// </summary>
public sealed class OutboxInboxReceipt
{
    public Guid EventId { get; init; }
    public string Type { get; init; } = string.Empty;
    public DateTimeOffset ProcessedAtUtc { get; init; }
}
