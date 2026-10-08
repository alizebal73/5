using GameNet.Shared.Primitives;
using GameNet.Server.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GameNet.Server.Infrastructure.Outbox;

public sealed class EfOutboxDispatcher(
    GameNetDbContext dbContext,
    IGameClock clock) : IOutboxDispatcher
{
    public async Task<IReadOnlyList<OutboxMessage>> ClaimBatchAsync(
        int batchSize,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default)
    {
        if (batchSize is < 1 or > 500)
            throw new ArgumentOutOfRangeException(nameof(batchSize));
        if (leaseDuration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(leaseDuration));

        await using var transaction =
            await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var now = clock.UtcNow;
        var until = now.Add(leaseDuration);

        var ids = await dbContext.Database.SqlQueryRaw<long>(
            """
            SELECT id AS "Value"
            FROM outbox_messages
            WHERE published_at_utc IS NULL
              AND (lease_expires_at_utc IS NULL OR lease_expires_at_utc <= {0})
            ORDER BY id
            FOR UPDATE SKIP LOCKED
            LIMIT {1}
            """,
            now,
            batchSize).ToListAsync(cancellationToken);

        if (ids.Count == 0)
        {
            await transaction.CommitAsync(cancellationToken);
            return Array.Empty<OutboxMessage>();
        }

        var messages = await dbContext.OutboxMessages
            .Where(x => ids.Contains(x.Id))
            .OrderBy(x => x.Id)
            .ToListAsync(cancellationToken);

        foreach (var message in messages)
            message.Claim(Guid.NewGuid().ToString("N"), until);

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return messages;
    }

    public async Task MarkPublishedAsync(
        long messageId,
        string leaseToken,
        DateTimeOffset publishedAtUtc,
        CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;
        var updated = await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE outbox_messages
            SET published_at_utc = {publishedAtUtc},
                lease_token = NULL,
                lease_expires_at_utc = NULL
            WHERE id = {messageId}
              AND published_at_utc IS NULL
              AND lease_token = {leaseToken}
              AND lease_expires_at_utc > {now}
            """,
            cancellationToken);

        if (updated != 1)
            throw new InvalidOperationException(
                "Outbox lease is no longer owned or has expired.");
    }
}
