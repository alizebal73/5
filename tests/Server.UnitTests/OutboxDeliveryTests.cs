using GameNet.Server.Infrastructure.Outbox;
using GameNet.Server.Persistence;
using GameNet.Shared.Primitives;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GameNet.Server.UnitTests;

[Collection("Outbox PostgreSQL serial")]
public sealed class OutboxDeliveryTests
{
    [Fact]
    public void Claim_counts_attempts_and_published_messages_are_terminal()
    {
        var now = DateTimeOffset.Parse("2026-10-12T00:00:00Z");
        var message = CreateMessage(Guid.NewGuid(), now);

        message.Claim("lease-one", now.AddMinutes(1));
        message.Claim("lease-two", now.AddMinutes(1));

        Assert.Equal(2, message.AttemptCount);
        Assert.Equal("lease-two", message.LeaseToken);

        message.MarkPublished(now.AddSeconds(1));

        Assert.Equal(now.AddSeconds(1), message.PublishedAtUtc);
        Assert.Null(message.LeaseToken);
        Assert.Null(message.LeaseExpiresAtUtc);
        Assert.Throws<InvalidOperationException>(() => message.Claim("lease-three", now.AddMinutes(2)));
    }

    [Fact]
    public void Retry_policy_applies_exponential_backoff_jitter_and_a_hard_cap()
    {
        var initial = TimeSpan.FromSeconds(2);
        var maximum = TimeSpan.FromSeconds(5);

        Assert.Equal(TimeSpan.FromMilliseconds(1600),
            OutboxRetryPolicy.CalculateDelay(1, initial, maximum, jitterSample: 0));
        Assert.Equal(TimeSpan.FromSeconds(4),
            OutboxRetryPolicy.CalculateDelay(2, initial, maximum, jitterSample: 1));
        Assert.Equal(maximum,
            OutboxRetryPolicy.CalculateDelay(10, initial, maximum, jitterSample: 1));
    }

    [Fact]
    public async Task Registry_passes_stable_event_identity_and_rejects_unknown_types()
    {
        var now = DateTimeOffset.Parse("2026-10-12T00:00:00Z");
        var eventId = Guid.NewGuid();
        var handler = new CapturingHandler("station.changed.v1");
        var registry = new OutboxHandlerRegistry([handler], new InlineInbox());
        var message = CreateMessage(eventId, now, "station.changed.v1");
        message.Id = 101;
        message.Claim("unit-test-lease", now.AddMinutes(1));

        await registry.DispatchAsync(message);

        Assert.NotNull(handler.Received);
        Assert.Equal(eventId, handler.Received!.EventId);
        Assert.Equal(message.PayloadJson, handler.Received.PayloadJson);
        Assert.Equal(message.Type, handler.Received.Type);

        var unknown = CreateMessage(Guid.NewGuid(), now, "unregistered.v1");
        var exception = await Assert.ThrowsAsync<OutboxPermanentDeliveryException>(
            () => registry.DispatchAsync(unknown));
        Assert.Equal("handler_not_registered", exception.FailureCode);
    }

    [Fact]
    public void Registry_fails_fast_when_two_handlers_claim_the_same_event_type()
    {
        var handlers = new IOutboxEventHandler[]
        {
            new CapturingHandler("station.changed.v1"),
            new CapturingHandler("station.changed.v1")
        };

        var exception = Assert.Throws<InvalidOperationException>(() => new OutboxHandlerRegistry(handlers, new InlineInbox()));
        Assert.Equal("OUTBOX_HANDLER_EVENT_TYPE_DUPLICATE", exception.Message);
    }

    private static OutboxMessage CreateMessage(
        Guid eventId,
        DateTimeOffset occurredAt,
        string type = "test.event.v1") =>
        new()
        {
            EventId = eventId,
            OccurredAtUtc = occurredAt,
            Type = type,
            PayloadJson = "{\"version\":1}"
        };

    private sealed class InlineInbox : IOutboxInbox
    {
        public async Task<bool> ExecuteOnceAsync(
            long outboxMessageId,
            string leaseToken,
            OutboxDeliveryEnvelope message,
            Func<CancellationToken, Task> operation,
            CancellationToken cancellationToken = default)
        {
            Assert.True(outboxMessageId > 0);
            Assert.False(string.IsNullOrWhiteSpace(leaseToken));
            await operation(cancellationToken);
            return true;
        }
    }

    private sealed class CapturingHandler(string eventType) : IOutboxEventHandler
    {
        public string EventType { get; } = eventType;
        public OutboxDeliveryEnvelope? Received { get; private set; }

        public Task HandleAsync(OutboxDeliveryEnvelope message, CancellationToken cancellationToken = default)
        {
            Received = message;
            return Task.CompletedTask;
        }
    }
}

/// <summary>
/// Runs only when verify-postgresql.ps1 enables the explicit disposable-database integration lane.
/// The normal unit-test lane does not require a machine or database connection.
/// </summary>
[Collection("Outbox PostgreSQL serial")]
public sealed class OutboxDispatcherPostgresTests
{
    [Fact]
    public async Task Concurrent_claims_retry_dead_letter_and_published_marker_are_persisted()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("GAMENET_OUTBOX_POSTGRES_TESTS"),
                "true",
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var connectionString = Environment.GetEnvironmentVariable("GAMENET_DATABASE_CONNECTION");
        Assert.False(string.IsNullOrWhiteSpace(connectionString));

        var clock = new MutableGameClock(DateTimeOffset.UtcNow);
        var firstEventId = Guid.NewGuid();
        var secondEventId = Guid.NewGuid();
        await using (var seed = CreateContext(connectionString!))
        {
            seed.OutboxMessages.Add(CreateMessage(firstEventId, clock.UtcNow));
            seed.OutboxMessages.Add(CreateMessage(secondEventId, clock.UtcNow));
            await seed.SaveChangesAsync();
        }

        try
        {
            var concurrentClaims = await Task.WhenAll(
                ClaimOneAsync(connectionString!, clock),
                ClaimOneAsync(connectionString!, clock));

            var firstClaim = Assert.Single(concurrentClaims[0]);
            var secondClaim = Assert.Single(concurrentClaims[1]);
            Assert.NotEqual(firstClaim.EventId, secondClaim.EventId);
            Assert.Equal(1, firstClaim.AttemptCount);
            Assert.Equal(1, secondClaim.AttemptCount);
            Assert.False(string.IsNullOrWhiteSpace(firstClaim.LeaseToken));
            Assert.False(string.IsNullOrWhiteSpace(secondClaim.LeaseToken));

            var retryClaim = firstClaim;
            var originalLeaseToken = retryClaim.LeaseToken!;
            var retryAt = clock.UtcNow.AddSeconds(10);
            await using (var failContext = CreateContext(connectionString!))
            {
                var dispatcher = new EfOutboxDispatcher(failContext, clock);
                await dispatcher.RecordFailureAsync(
                    retryClaim.Id,
                    originalLeaseToken,
                    "handler_failed",
                    clock.UtcNow,
                    retryAt,
                    maxAttempts: 2,
                    permanentFailure: false);
            }

            clock.UtcNow = retryAt.AddSeconds(-1);
            var notDue = await ClaimOneAsync(connectionString!, clock);
            Assert.Empty(notDue);

            clock.UtcNow = retryAt.AddSeconds(1);
            var retried = await ClaimOneAsync(connectionString!, clock);
            var secondAttempt = Assert.Single(retried);
            Assert.Equal(retryClaim.EventId, secondAttempt.EventId);
            Assert.Equal(2, secondAttempt.AttemptCount);
            Assert.NotEqual(originalLeaseToken, secondAttempt.LeaseToken);

            await using (var staleContext = CreateContext(connectionString!))
            {
                var dispatcher = new EfOutboxDispatcher(staleContext, clock);
                await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    dispatcher.MarkPublishedAsync(
                        secondAttempt.Id,
                        originalLeaseToken,
                        clock.UtcNow));
            }

            await using (var deadLetterContext = CreateContext(connectionString!))
            {
                var dispatcher = new EfOutboxDispatcher(deadLetterContext, clock);
                await dispatcher.RecordFailureAsync(
                    secondAttempt.Id,
                    secondAttempt.LeaseToken!,
                    "handler_failed",
                    clock.UtcNow,
                    clock.UtcNow,
                    maxAttempts: 2,
                    permanentFailure: false);
                deadLetterContext.ChangeTracker.Clear();
                var deadLettered = await deadLetterContext.OutboxMessages
                    .SingleAsync(x => x.EventId == retryClaim.EventId);
                Assert.NotNull(deadLettered.DeadLetteredAtUtc);
                Assert.Null(deadLettered.NextAttemptAtUtc);
                Assert.Equal("handler_failed", deadLettered.LastFailureCode);
            }

            await using (var inboxContext = CreateContext(connectionString!))
            {
                var inbox = new EfOutboxInbox(inboxContext, clock);
                var processed = await inbox.ExecuteOnceAsync(
                    secondClaim.Id,
                    secondClaim.LeaseToken!,
                    new OutboxDeliveryEnvelope(
                        secondClaim.EventId,
                        secondClaim.OccurredAtUtc,
                        secondClaim.Type,
                        secondClaim.PayloadJson),
                    _ => Task.CompletedTask);
                Assert.True(processed);
            }

            await using (var publishContext = CreateContext(connectionString!))
            {
                var dispatcher = new EfOutboxDispatcher(publishContext, clock);
                await dispatcher.MarkPublishedAsync(
                    secondClaim.Id,
                    secondClaim.LeaseToken!,
                    clock.UtcNow);
                publishContext.ChangeTracker.Clear();
                var published = await publishContext.OutboxMessages
                    .SingleAsync(x => x.EventId == secondClaim.EventId);
                Assert.NotNull(published.PublishedAtUtc);
                Assert.Null(published.LeaseToken);
            }

            clock.UtcNow = clock.UtcNow.AddMinutes(5);
            var remaining = await ClaimBatchAsync(connectionString!, clock, batchSize: 10);
            Assert.DoesNotContain(remaining, x => x.EventId == retryClaim.EventId);
        }
        finally
        {
            await using var cleanup = CreateContext(connectionString!);
            await cleanup.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM outbox_inbox_receipts WHERE event_id = {firstEventId} OR event_id = {secondEventId}");
            await cleanup.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM outbox_messages WHERE event_id = {firstEventId} OR event_id = {secondEventId}");
        }
    }


    [Fact]
    public async Task Mark_published_requires_a_durable_inbox_receipt()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("GAMENET_OUTBOX_POSTGRES_TESTS"),
                "true",
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var connectionString = Environment.GetEnvironmentVariable("GAMENET_DATABASE_CONNECTION");
        Assert.False(string.IsNullOrWhiteSpace(connectionString));
        var clock = new MutableGameClock(DateTimeOffset.UtcNow);
        var eventId = Guid.NewGuid();

        try
        {
            await using (var seed = CreateContext(connectionString!))
            {
                seed.OutboxMessages.Add(CreateMessage(eventId, clock.UtcNow));
                await seed.SaveChangesAsync();
            }

            var claimed = Assert.Single(await ClaimBatchAsync(
                connectionString!,
                clock,
                batchSize: 1,
                maxAttempts: 1));

            await using (var context = CreateContext(connectionString!))
            {
                var dispatcher = new EfOutboxDispatcher(context, clock);
                await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    dispatcher.MarkPublishedAsync(claimed.Id, claimed.LeaseToken!, clock.UtcNow));
            }

            await using var verify = CreateContext(connectionString!);
            var message = await verify.OutboxMessages.SingleAsync(x => x.EventId == eventId);
            Assert.Null(message.PublishedAtUtc);
            Assert.Null(message.DeadLetteredAtUtc);
            Assert.False(await verify.OutboxInboxReceipts.AnyAsync(x => x.EventId == eventId));
        }
        finally
        {
            await using var cleanup = CreateContext(connectionString!);
            await cleanup.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM outbox_inbox_receipts WHERE event_id = {eventId}");
            await cleanup.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM outbox_messages WHERE event_id = {eventId}");
        }
    }

    [Fact]
    public async Task Active_inbox_transaction_is_not_dead_lettered_when_final_attempt_lease_expires()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("GAMENET_OUTBOX_POSTGRES_TESTS"),
                "true",
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var connectionString = Environment.GetEnvironmentVariable("GAMENET_DATABASE_CONNECTION");
        Assert.False(string.IsNullOrWhiteSpace(connectionString));
        var clock = new MutableGameClock(DateTimeOffset.UtcNow);
        var eventId = Guid.NewGuid();
        var enteredHandler = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseHandler = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<bool>? processing = null;

        try
        {
            await using (var seed = CreateContext(connectionString!))
            {
                seed.OutboxMessages.Add(CreateMessage(eventId, clock.UtcNow));
                await seed.SaveChangesAsync();
            }

            var claimed = Assert.Single(await ClaimBatchAsync(
                connectionString!,
                clock,
                batchSize: 1,
                maxAttempts: 1));

            processing = Task.Run(async () =>
            {
                await using var db = CreateContext(connectionString!);
                var inbox = new EfOutboxInbox(db, clock);
                return await inbox.ExecuteOnceAsync(
                    claimed.Id,
                    claimed.LeaseToken!,
                    new OutboxDeliveryEnvelope(eventId, clock.UtcNow, claimed.Type, claimed.PayloadJson),
                    async token =>
                    {
                        enteredHandler.TrySetResult(true);
                        await releaseHandler.Task.WaitAsync(token);
                    });
            });

            await enteredHandler.Task.WaitAsync(TimeSpan.FromSeconds(10));
            // Advance beyond the lease while the Inbox transaction intentionally still
            // holds the Outbox row lock. Recovery must skip it rather than dead-letter it.
            clock.UtcNow = clock.UtcNow.AddMinutes(3);

            var concurrentClaim = await ClaimBatchAsync(
                connectionString!,
                clock,
                batchSize: 1,
                maxAttempts: 1);
            Assert.Empty(concurrentClaim);

            await using (var interim = CreateContext(connectionString!))
            {
                var state = await interim.OutboxMessages.SingleAsync(x => x.EventId == eventId);
                Assert.Null(state.DeadLetteredAtUtc);
                Assert.Null(state.PublishedAtUtc);
            }

            releaseHandler.TrySetResult(true);
            Assert.True(await processing.WaitAsync(TimeSpan.FromSeconds(10)));

            // Expiry alone does not revoke this owner if no new claim replaced its token.
            // The committed Inbox receipt proves local work completed successfully.
            await using (var publishContext = CreateContext(connectionString!))
            {
                var dispatcher = new EfOutboxDispatcher(publishContext, clock);
                await dispatcher.MarkPublishedAsync(claimed.Id, claimed.LeaseToken!, clock.UtcNow);
            }

            await using var verify = CreateContext(connectionString!);
            var completed = await verify.OutboxMessages.SingleAsync(x => x.EventId == eventId);
            Assert.NotNull(completed.PublishedAtUtc);
            Assert.Null(completed.DeadLetteredAtUtc);
            Assert.True(await verify.OutboxInboxReceipts.AnyAsync(x => x.EventId == eventId));
        }
        finally
        {
            releaseHandler.TrySetResult(true);
            if (processing is not null)
            {
                try { await processing.WaitAsync(TimeSpan.FromSeconds(10)); }
                catch { /* Preserve the assertion failure; cleanup still must run. */ }
            }
            await using var cleanup = CreateContext(connectionString!);
            await cleanup.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM outbox_inbox_receipts WHERE event_id = {eventId}");
            await cleanup.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM outbox_messages WHERE event_id = {eventId}");
        }
    }

    [Fact]
    public async Task Expired_lease_after_max_attempts_dead_letters_instead_of_reclaiming_forever()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("GAMENET_OUTBOX_POSTGRES_TESTS"),
                "true",
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var connectionString = Environment.GetEnvironmentVariable("GAMENET_DATABASE_CONNECTION");
        Assert.False(string.IsNullOrWhiteSpace(connectionString));
        var clock = new MutableGameClock(DateTimeOffset.UtcNow);
        var eventId = Guid.NewGuid();

        try
        {
            await using (var seed = CreateContext(connectionString!))
            {
                seed.OutboxMessages.Add(CreateMessage(eventId, clock.UtcNow));
                await seed.SaveChangesAsync();
            }

            var claimed = Assert.Single(await ClaimBatchAsync(connectionString!, clock, batchSize: 1, maxAttempts: 1));
            Assert.Equal(1, claimed.AttemptCount);

            // Simulate a crash after claim: no handler result/failure marker is written.
            clock.UtcNow = clock.UtcNow.AddMinutes(3);
            var nextBatch = await ClaimBatchAsync(connectionString!, clock, batchSize: 1, maxAttempts: 1);
            Assert.Empty(nextBatch);

            await using var verify = CreateContext(connectionString!);
            var deadLettered = await verify.OutboxMessages.SingleAsync(message => message.EventId == eventId);
            Assert.NotNull(deadLettered.DeadLetteredAtUtc);
            Assert.Equal("max_attempts_exhausted_after_lease_expiry", deadLettered.LastFailureCode);
            Assert.Null(deadLettered.LeaseToken);
            Assert.Null(deadLettered.LeaseExpiresAtUtc);
        }
        finally
        {
            await using var cleanup = CreateContext(connectionString!);
            await cleanup.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM outbox_messages WHERE event_id = {eventId}");
        }
    }

    [Fact]
    public async Task Expired_lease_with_committed_inbox_receipt_recovers_to_published_not_dead_letter()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("GAMENET_OUTBOX_POSTGRES_TESTS"),
                "true",
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var connectionString = Environment.GetEnvironmentVariable("GAMENET_DATABASE_CONNECTION");
        Assert.False(string.IsNullOrWhiteSpace(connectionString));
        var clock = new MutableGameClock(DateTimeOffset.UtcNow);
        var eventId = Guid.NewGuid();

        try
        {
            await using (var seed = CreateContext(connectionString!))
            {
                seed.OutboxMessages.Add(CreateMessage(eventId, clock.UtcNow));
                await seed.SaveChangesAsync();
            }

            var claimed = Assert.Single(await ClaimBatchAsync(
                connectionString!,
                clock,
                batchSize: 1,
                maxAttempts: 1));

            await using (var inboxContext = CreateContext(connectionString!))
            {
                var inbox = new EfOutboxInbox(inboxContext, clock);
                var processed = await inbox.ExecuteOnceAsync(
                    claimed.Id,
                    claimed.LeaseToken!,
                    new OutboxDeliveryEnvelope(
                        eventId,
                        clock.UtcNow,
                        "test.event.v1",
                        "{\"version\":1}"),
                    _ => Task.CompletedTask);

                Assert.True(processed);
            }

            // Simulate a worker crash after the Inbox transaction commits and before
            // MarkPublishedAsync runs. Recovery must observe the receipt and heal state.
            clock.UtcNow = clock.UtcNow.AddMinutes(3);
            var nextBatch = await ClaimBatchAsync(connectionString!, clock, batchSize: 1, maxAttempts: 1);
            Assert.Empty(nextBatch);

            await using var verify = CreateContext(connectionString!);
            var recovered = await verify.OutboxMessages.SingleAsync(message => message.EventId == eventId);
            Assert.NotNull(recovered.PublishedAtUtc);
            Assert.Null(recovered.DeadLetteredAtUtc);
            Assert.Null(recovered.LastFailureCode);
            Assert.Null(recovered.LeaseToken);
            Assert.Null(recovered.LeaseExpiresAtUtc);
        }
        finally
        {
            await using var cleanup = CreateContext(connectionString!);
            await cleanup.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM outbox_inbox_receipts WHERE event_id = {eventId}");
            await cleanup.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM outbox_messages WHERE event_id = {eventId}");
        }
    }

    private static async Task<IReadOnlyList<OutboxMessage>> ClaimOneAsync(
        string connectionString,
        IGameClock clock) =>
        await ClaimBatchAsync(connectionString, clock, batchSize: 1);

    private static async Task<IReadOnlyList<OutboxMessage>> ClaimBatchAsync(
        string connectionString,
        IGameClock clock,
        int batchSize,
        int maxAttempts = 10)
    {
        await using var db = CreateContext(connectionString);
        var dispatcher = new EfOutboxDispatcher(db, clock);
        return await dispatcher.ClaimBatchAsync(
            batchSize,
            TimeSpan.FromMinutes(2),
            maxAttempts: maxAttempts);
    }

    private static GameNetDbContext CreateContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<GameNetDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        return new GameNetDbContext(options);
    }

    private static OutboxMessage CreateMessage(Guid eventId, DateTimeOffset occurredAt) =>
        new()
        {
            EventId = eventId,
            OccurredAtUtc = occurredAt,
            Type = "test.event.v1",
            PayloadJson = "{\"version\":1}"
        };

    private sealed class MutableGameClock(DateTimeOffset utcNow) : IGameClock
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;
    }
}
