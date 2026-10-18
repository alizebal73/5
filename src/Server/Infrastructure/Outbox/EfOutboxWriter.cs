using GameNet.Server.Persistence;

namespace GameNet.Server.Infrastructure.Outbox;

public sealed class EfOutboxWriter(GameNetDbContext dbContext) : IOutboxWriter
{
    public OutboxMessage Append(OutboxRecord record)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(record.Type);
        ArgumentException.ThrowIfNullOrWhiteSpace(record.PayloadJson);

        var message = new OutboxMessage
        {
            EventId = record.EventId,
            Type = record.Type,
            PayloadJson = record.PayloadJson,
            OccurredAtUtc = record.OccurredAtUtc
        };

        dbContext.OutboxMessages.Add(message);
        return message;
    }
}
