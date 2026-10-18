using GameNet.Server.Persistence;
using GameNet.Shared.Primitives;
using Microsoft.EntityFrameworkCore;

namespace GameNet.Server.Infrastructure.Outbox;

public sealed class EfOutboxInbox(
    GameNetDbContext dbContext,
    IGameClock clock) : IOutboxInbox
{
    public async Task<bool> ExecuteOnceAsync(
        long outboxMessageId,
        string leaseToken,
        OutboxDeliveryEnvelope message,
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken = default)
    {
        if (outboxMessageId <= 0)
            throw new ArgumentOutOfRangeException(nameof(outboxMessageId));
        ArgumentException.ThrowIfNullOrWhiteSpace(leaseToken);
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentException.ThrowIfNullOrWhiteSpace(message.Type);
        if (message.EventId == Guid.Empty)
            throw new ArgumentException("Outbox event ID cannot be empty.", nameof(message));
        if (message.Type.Length > 200)
            throw new ArgumentOutOfRangeException(nameof(message), "Outbox event type exceeds the storage contract.");

        await using var transaction =
            await dbContext.Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted, cancellationToken);

        // Lock the Outbox row before local work. A lease that expired while waiting in a
        // batch must not execute, and recovery/reclaim must skip a row with an active handler.
        var lockedIds = await dbContext.Database.SqlQueryRaw<long>(
            """
            SELECT id AS "Value"
            FROM outbox_messages
            WHERE id = {0} AND event_id = {1}
            FOR UPDATE
            """,
            outboxMessageId,
            message.EventId).ToListAsync(cancellationToken);

        if (lockedIds.Count != 1 || lockedIds[0] != outboxMessageId)
            throw new InvalidOperationException("OUTBOX_MESSAGE_NOT_FOUND");

        // A FOR UPDATE query can wait for another transaction. Read owner and expiry
        // only after the row lock is acquired so a stale claimant cannot start local work.
        var lease = await dbContext.OutboxMessages
            .AsNoTracking()
            .Where(x => x.Id == outboxMessageId)
            .Select(x => new
            {
                x.EventId,
                x.Type,
                x.LeaseToken,
                x.LeaseExpiresAtUtc,
                x.PublishedAtUtc,
                x.DeadLetteredAtUtc
            })
            .SingleAsync(cancellationToken);

        if (lease.EventId != message.EventId ||
            !string.Equals(lease.Type, message.Type, StringComparison.Ordinal))
            throw new OutboxPermanentDeliveryException("inbox_outbox_type_mismatch");

        if (!string.Equals(lease.LeaseToken, leaseToken, StringComparison.Ordinal) ||
            lease.LeaseExpiresAtUtc is null ||
            lease.LeaseExpiresAtUtc <= clock.UtcNow ||
            lease.PublishedAtUtc is not null ||
            lease.DeadLetteredAtUtc is not null)
            throw new InvalidOperationException("OUTBOX_LEASE_NOT_OWNED_OR_EXPIRED");

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
