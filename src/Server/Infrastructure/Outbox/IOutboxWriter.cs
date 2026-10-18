namespace GameNet.Server.Infrastructure.Outbox;

public interface IOutboxWriter
{
    OutboxMessage Append(OutboxRecord record);
}

public sealed record OutboxRecord(
    Guid EventId,
    string Type,
    string PayloadJson,
    DateTimeOffset OccurredAtUtc);
