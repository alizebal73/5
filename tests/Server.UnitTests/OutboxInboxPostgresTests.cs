using GameNet.Server.Infrastructure.Outbox;
using GameNet.Server.Persistence;
using GameNet.Shared.Primitives;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GameNet.Server.UnitTests;

/// <summary>
/// Executes only in the explicit disposable-PostgreSQL certification lane.
/// The receipt and local handler writes must share one transaction.
/// </summary>
[Collection("Outbox PostgreSQL serial")]
public sealed class OutboxInboxPostgresTests
{
    [Fact]
    public async Task Concurrent_delivery_processes_event_once_and_failed_handler_can_retry()
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

        var clock = new TestGameClock(DateTimeOffset.UtcNow);
        var duplicateEventId = Guid.NewGuid();
        var failedThenRetriedEventId = Guid.NewGuid();
        var rolledBackSideEffectId = Guid.NewGuid();
        var duplicateEnvelope = CreateEnvelope(duplicateEventId, clock.UtcNow);
        var retryEnvelope = CreateEnvelope(failedThenRetriedEventId, clock.UtcNow);
        var duplicateClaim = await SeedClaimedMessageAsync(connectionString!, duplicateEnvelope, clock);
        var retryClaim = await SeedClaimedMessageAsync(connectionString!, retryEnvelope, clock);
        var handlerInvocations = 0;

        try
        {
            var results = await Task.WhenAll(
                ProcessAsync(connectionString!, clock, duplicateClaim, duplicateEnvelope, async token =>
                {
                    Interlocked.Increment(ref handlerInvocations);
                    await Task.Delay(TimeSpan.FromMilliseconds(50), token);
                }),
                ProcessAsync(connectionString!, clock, duplicateClaim, duplicateEnvelope, async token =>
                {
                    Interlocked.Increment(ref handlerInvocations);
                    await Task.Delay(TimeSpan.FromMilliseconds(50), token);
                }));

            Assert.Equal(1, results.Count(processed => processed));
            Assert.Equal(1, results.Count(processed => !processed));
            Assert.Equal(1, Volatile.Read(ref handlerInvocations));

            var typeMismatch = new OutboxDeliveryEnvelope(
                duplicateEventId,
                clock.UtcNow,
                "inbox.other-version.v1",
                "{\"version\":1}");
            await Assert.ThrowsAsync<OutboxPermanentDeliveryException>(() =>
                ProcessAsync(connectionString!, clock, duplicateClaim, typeMismatch, _ => Task.CompletedTask));

            await using (var failingContext = CreateContext(connectionString!))
            {
                var failingInbox = new EfOutboxInbox(failingContext, clock);
                await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    failingInbox.ExecuteOnceAsync(retryClaim.Id, retryClaim.LeaseToken, retryEnvelope, async token =>
                    {
                        // Simulate a consumer saving a local side effect before failing.
                        failingContext.OutboxMessages.Add(new OutboxMessage
                        {
                            EventId = rolledBackSideEffectId,
                            OccurredAtUtc = clock.UtcNow,
                            Type = "test.follow-up.v1",
                            PayloadJson = "{\"version\":1}"
                        });
                        await failingContext.SaveChangesAsync(token);
                        throw new InvalidOperationException("expected test failure");
                    }));
            }

            await using (var rollbackCheck = CreateContext(connectionString!))
            {
                Assert.False(await rollbackCheck.OutboxMessages
                    .AnyAsync(message => message.EventId == rolledBackSideEffectId));
                Assert.False(await rollbackCheck.OutboxInboxReceipts
                    .AnyAsync(receipt => receipt.EventId == failedThenRetriedEventId));
            }

            var retryResult = await ProcessAsync(
                connectionString!,
                clock,
                retryClaim,
                retryEnvelope,
                _ =>
                {
                    Interlocked.Increment(ref handlerInvocations);
                    return Task.CompletedTask;
                });

            Assert.True(retryResult);
            Assert.Equal(2, Volatile.Read(ref handlerInvocations));

            await using var verify = CreateContext(connectionString!);
            var receipts = await verify.OutboxInboxReceipts
                .Where(receipt => receipt.EventId == duplicateEventId ||
                                  receipt.EventId == failedThenRetriedEventId)
                .ToListAsync();
            Assert.Equal(2, receipts.Count);
            Assert.All(receipts, receipt => Assert.False(string.IsNullOrWhiteSpace(receipt.Type)));
        }
        finally
        {
            await using var cleanup = CreateContext(connectionString!);
            await cleanup.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM outbox_inbox_receipts WHERE event_id = {duplicateEventId} OR event_id = {failedThenRetriedEventId}");
            await cleanup.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM outbox_messages WHERE event_id = {duplicateEventId} OR event_id = {failedThenRetriedEventId} OR event_id = {rolledBackSideEffectId}");
        }
    }

    private static async Task<ClaimedOutboxMessage> SeedClaimedMessageAsync(
        string connectionString,
        OutboxDeliveryEnvelope envelope,
        IGameClock clock)
    {
        await using var db = CreateContext(connectionString);
        var message = new OutboxMessage
        {
            EventId = envelope.EventId,
            OccurredAtUtc = envelope.OccurredAtUtc,
            Type = envelope.Type,
            PayloadJson = envelope.PayloadJson
        };
        message.Claim(Guid.NewGuid().ToString("N"), clock.UtcNow.AddMinutes(5));
        db.OutboxMessages.Add(message);
        await db.SaveChangesAsync();
        return new ClaimedOutboxMessage(message.Id, message.LeaseToken!);
    }

    private static async Task<bool> ProcessAsync(
        string connectionString,
        IGameClock clock,
        ClaimedOutboxMessage claim,
        OutboxDeliveryEnvelope message,
        Func<CancellationToken, Task> operation)
    {
        await using var db = CreateContext(connectionString);
        var inbox = new EfOutboxInbox(db, clock);
        return await inbox.ExecuteOnceAsync(claim.Id, claim.LeaseToken, message, operation);
    }

    private static GameNetDbContext CreateContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<GameNetDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        return new GameNetDbContext(options);
    }

    private static OutboxDeliveryEnvelope CreateEnvelope(Guid eventId, DateTimeOffset occurredAt) =>
        new(eventId, occurredAt, "inbox.test.v1", "{\"version\":1}");

    private sealed record ClaimedOutboxMessage(long Id, string LeaseToken);

    private sealed class TestGameClock(DateTimeOffset utcNow) : IGameClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }
}
