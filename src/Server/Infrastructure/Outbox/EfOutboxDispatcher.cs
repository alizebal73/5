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
        CancellationToken cancellationToken = default,
        int maxAttempts = 10)
    {
        if (batchSize is < 1 or > 500)
            throw new ArgumentOutOfRangeException(nameof(batchSize));
        if (leaseDuration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(leaseDuration));
        if (maxAttempts is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(maxAttempts));

        await using var transaction =
            await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var now = clock.UtcNow;
        var until = now.Add(leaseDuration);

        // If the handler's Inbox transaction committed but the worker crashed before setting
        // the published marker, the durable receipt proves local work already completed.
        // Heal that state before applying the attempt-limit dead-letter transition.
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE outbox_messages AS message
            SET published_at_utc = {now},
                next_attempt_at_utc = NULL,
                last_failure_code = NULL,
                lease_token = NULL,
                lease_expires_at_utc = NULL
            WHERE message.published_at_utc IS NULL
              AND message.dead_lettered_at_utc IS NULL
              AND (message.lease_expires_at_utc IS NULL OR message.lease_expires_at_utc <= {now})
              AND EXISTS (
                  SELECT 1
                  FROM outbox_inbox_receipts AS receipt
                  WHERE receipt.event_id = message.event_id
                    AND receipt.type = message.type
              )
            """,
            cancellationToken);

        // A worker may crash after claiming an event and never record a handler failure.
        // When that final lease expires, transition it to a terminal state before claiming
        // more work; otherwise MaxAttempts could be bypassed by repeated process crashes.
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE outbox_messages
            SET dead_lettered_at_utc = {now},
                next_attempt_at_utc = NULL,
                last_failure_code = 'max_attempts_exhausted_after_lease_expiry',
                lease_token = NULL,
                lease_expires_at_utc = NULL
            WHERE published_at_utc IS NULL
              AND dead_lettered_at_utc IS NULL
              AND attempt_count >= {maxAttempts}
              AND (lease_expires_at_utc IS NULL OR lease_expires_at_utc <= {now})
            """,
            cancellationToken);

        var ids = await dbContext.Database.SqlQueryRaw<long>(
            """
            SELECT id AS "Value"
            FROM outbox_messages
            WHERE published_at_utc IS NULL
              AND dead_lettered_at_utc IS NULL
              AND attempt_count < {2}
              AND (next_attempt_at_utc IS NULL OR next_attempt_at_utc <= {0})
              AND (lease_expires_at_utc IS NULL OR lease_expires_at_utc <= {0})
            ORDER BY id
            FOR UPDATE SKIP LOCKED
            LIMIT {1}
            """,
            now,
            batchSize,
            maxAttempts).ToListAsync(cancellationToken);

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
                next_attempt_at_utc = NULL,
                last_failure_code = NULL,
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
                "OUTBOX_LEASE_NOT_OWNED_OR_EXPIRED");
    }

    public async Task RecordFailureAsync(
        long messageId,
        string leaseToken,
        string failureCode,
        DateTimeOffset failedAtUtc,
        DateTimeOffset retryAtUtc,
        int maxAttempts,
        bool permanentFailure,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(leaseToken);
        if (string.IsNullOrWhiteSpace(failureCode) ||
            failureCode.Length > 128 ||
            failureCode.Any(character =>
                !(char.IsAsciiLetterOrDigit(character) || character is '_' or '-' or '.')))
        {
            throw new ArgumentException("Failure code must be a stable, non-sensitive identifier.", nameof(failureCode));
        }
        if (maxAttempts is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(maxAttempts));

        var now = clock.UtcNow;
        var updated = await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE outbox_messages
            SET last_failure_code = {failureCode},
                next_attempt_at_utc = CASE
                    WHEN {permanentFailure} OR attempt_count >= {maxAttempts} THEN NULL
                    ELSE {retryAtUtc}
                END,
                dead_lettered_at_utc = CASE
                    WHEN {permanentFailure} OR attempt_count >= {maxAttempts} THEN {failedAtUtc}
                    ELSE NULL
                END,
                lease_token = NULL,
                lease_expires_at_utc = NULL
            WHERE id = {messageId}
              AND published_at_utc IS NULL
              AND dead_lettered_at_utc IS NULL
              AND lease_token = {leaseToken}
              AND lease_expires_at_utc > {now}
            """,
            cancellationToken);

        if (updated != 1)
            throw new InvalidOperationException(
                "OUTBOX_LEASE_NOT_OWNED_OR_EXPIRED");
    }
}
