namespace GameNet.Server.Infrastructure.Outbox;

public interface IOutboxWriter
{
    void Append(OutboxRecord record);
}

public sealed record OutboxRecord(Guid EventId,string Type,string PayloadJson,DateTimeOffset OccurredAtUtc);
