using GameNet.Server.Persistence;
using GameNet.Shared.Primitives;
using Microsoft.EntityFrameworkCore;

namespace GameNet.Server.Infrastructure.Outbox;

public sealed class EfOutboxInbox(
    GameNetDbContext dbContext,
    IGameClock clock) : IOutboxInbox
{
    public async Task<bool> ExecuteOnceAsync(
        OutboxDeliveryEnvelope message,
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentException.ThrowIfNullOrWhiteSpace(message.Type);
        if (message.EventId == Guid.Empty)
            throw new ArgumentException("Outbox event ID cannot be empty.", nameof(message));
        if (message.Type.Length > 200)
            throw new ArgumentOutOfRangeException(nameof(message), "Outbox event type exceeds the storage contract.");

        await using var transaction =
            await dbContext.Database.BeginTransactionAsync(cancellationToken);

        // The unique primary key and ON CONFLICT make the claim atomic between concurrent workers.
        // If an earlier operation is still running, PostgreSQL waits for its transaction to commit
        // or roll back before deciding whether this insert wins.
        var inserted = await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO outbox_inbox_receipts (event_id, type, processed_at_utc)
            VALUES ({message.EventId}, {message.Type}, {clock.UtcNow})
            ON CONFLICT (event_id) DO NOTHING
            """,
            cancellationToken);

        if (inserted == 0)
        {
            var existingType = await dbContext.OutboxInboxReceipts
                .AsNoTracking()
                .Where(receipt => receipt.EventId == message.EventId)
                .Select(receipt => receipt.Type)
                .SingleOrDefaultAsync(cancellationToken);

            if (existingType is null)
                throw new InvalidOperationException("OUTBOX_INBOX_RECEIPT_DISAPPEARED");
            if (!string.Equals(existingType, message.Type, StringComparison.Ordinal))
                throw new OutboxPermanentDeliveryException("inbox_event_type_mismatch");

            await transaction.CommitAsync(cancellationToken);
            return false;
        }

        // Handler database writes share this scoped DbContext/transaction. If it throws, disposing
        // the transaction rolls the receipt and all tracked local changes back together.
        await operation(cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }
}
