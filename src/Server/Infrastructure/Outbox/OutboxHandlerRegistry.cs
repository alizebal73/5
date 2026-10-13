namespace GameNet.Server.Infrastructure.Outbox;

public sealed class OutboxPermanentDeliveryException(string failureCode)
    : Exception("OUTBOX_PERMANENT_DELIVERY_FAILURE")
{
    public string FailureCode { get; } = ValidateFailureCode(failureCode);

    private static string ValidateFailureCode(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (value.Length > 128 ||
            value.Any(character =>
                !(char.IsAsciiLetterOrDigit(character) || character is '_' or '-' or '.')))
        {
            throw new ArgumentException("Failure code must be a stable, non-sensitive identifier.", nameof(value));
        }

        return value;
    }
}

public sealed class OutboxHandlerRegistry
{
    private readonly IReadOnlyDictionary<string, IOutboxEventHandler> handlers;
    private readonly IOutboxInbox inbox;

    public OutboxHandlerRegistry(
        IEnumerable<IOutboxEventHandler> eventHandlers,
        IOutboxInbox inbox)
    {
        ArgumentNullException.ThrowIfNull(eventHandlers);
        ArgumentNullException.ThrowIfNull(inbox);
        this.inbox = inbox;

        var registry = new Dictionary<string, IOutboxEventHandler>(StringComparer.Ordinal);
        foreach (var handler in eventHandlers)
        {
            ArgumentNullException.ThrowIfNull(handler);
            if (string.IsNullOrWhiteSpace(handler.EventType) || handler.EventType.Length > 200)
                throw new InvalidOperationException("OUTBOX_HANDLER_EVENT_TYPE_INVALID");

            if (!registry.TryAdd(handler.EventType, handler))
                throw new InvalidOperationException("OUTBOX_HANDLER_EVENT_TYPE_DUPLICATE");
        }

        handlers = registry;
    }

    public Task<bool> DispatchAsync(OutboxMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (!handlers.TryGetValue(message.Type, out var handler))
            throw new OutboxPermanentDeliveryException("handler_not_registered");

        var envelope = new OutboxDeliveryEnvelope(
            message.EventId,
            message.OccurredAtUtc,
            message.Type,
            message.PayloadJson);

        if (message.Id <= 0 || string.IsNullOrWhiteSpace(message.LeaseToken))
            throw new InvalidOperationException("OUTBOX_CLAIM_RETURNED_NO_LEASE");

        return inbox.ExecuteOnceAsync(
            message.Id,
            message.LeaseToken,
            envelope,
            token => handler.HandleAsync(envelope, token),
            cancellationToken);
    }
}
