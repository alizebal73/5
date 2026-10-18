using GameNet.Server.Infrastructure.Outbox;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace GameNet.Server.UnitTests;

public sealed class OutboxDeliveryWorkerConcurrencyTests
{
    [Fact]
    public void Outbox_delivery_defaults_to_order_preserving_single_handler()
    {
        var options = new OutboxDeliveryOptions();

        Assert.Equal(1, options.MaxConcurrentHandlers);
        Assert.True(options.IsValid);
    }

    [Fact]
    public async Task Worker_starts_claimed_handlers_concurrently_within_configured_limit()
    {
        var now = DateTimeOffset.UtcNow;
        var handler = new BlockingOutboxEventHandler(expectedConcurrentCalls: 2);
        var dispatcher = new OneBatchOutboxDispatcher(
        [
            CreateMessage(1, Guid.NewGuid(), "lease-one", now),
            CreateMessage(2, Guid.NewGuid(), "lease-two", now)
        ]);
        var services = new ServiceCollection();
        services.AddSingleton<IOutboxDispatcher>(dispatcher);
        services.AddSingleton<IOutboxInbox, InlineOutboxInbox>();
        services.AddSingleton<IOutboxEventHandler>(handler);
        services.AddScoped<OutboxHandlerRegistry>();
        using var provider = services.BuildServiceProvider();

        var settings = new OutboxDeliveryOptions
        {
            BatchSize = 10,
            MaxConcurrentHandlers = 2,
            PollIntervalSeconds = 1,
            LeaseDurationSeconds = 30,
            MaxAttempts = 3,
            InitialRetryDelaySeconds = 1,
            MaximumRetryDelaySeconds = 2
        };
        var worker = new OutboxDeliveryWorker(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(settings),
            TimeProvider.System,
            NullLogger<OutboxDeliveryWorker>.Instance);

        try
        {
            await worker.StartAsync(CancellationToken.None);

            // The first handler deliberately waits on Release. A sequential worker
            // cannot satisfy this barrier until the test times out; Task.WhenAll
            // starts the second handler while the first remains blocked.
            await handler.BothHandlersStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(2, dispatcher.LastRequestedBatchSize);
            Assert.Equal(2, handler.StartedCount);

            handler.Release.TrySetResult(true);
            await dispatcher.AllPublished.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(2, dispatcher.PublishedCount);
        }
        finally
        {
            handler.Release.TrySetResult(true);
            await worker.StopAsync(CancellationToken.None);
        }
    }

    private static OutboxMessage CreateMessage(
        long id,
        Guid eventId,
        string leaseToken,
        DateTimeOffset now)
    {
        var message = new OutboxMessage
        {
            Id = id,
            EventId = eventId,
            OccurredAtUtc = now,
            Type = "test.worker-concurrency.v1",
            PayloadJson = "{}"
        };
        message.Claim(leaseToken, now.AddMinutes(1));
        return message;
    }

    private sealed class BlockingOutboxEventHandler(int expectedConcurrentCalls) : IOutboxEventHandler
    {
        private int started;

        public string EventType => "test.worker-concurrency.v1";
        public int StartedCount => Volatile.Read(ref started);
        public TaskCompletionSource<bool> BothHandlersStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task HandleAsync(OutboxDeliveryEnvelope message, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref started) == expectedConcurrentCalls)
                BothHandlersStarted.TrySetResult(true);

            await Release.Task.WaitAsync(cancellationToken);
        }
    }

    private sealed class InlineOutboxInbox : IOutboxInbox
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

    private sealed class OneBatchOutboxDispatcher(IReadOnlyList<OutboxMessage> batch) : IOutboxDispatcher
    {
        private int claimCount;
        private int publishedCount;

        public int LastRequestedBatchSize { get; private set; }
        public int PublishedCount => Volatile.Read(ref publishedCount);
        public TaskCompletionSource<bool> AllPublished { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<IReadOnlyList<OutboxMessage>> ClaimBatchAsync(
            int batchSize,
            TimeSpan leaseDuration,
            CancellationToken cancellationToken = default,
            int maxAttempts = 10)
        {
            LastRequestedBatchSize = batchSize;
            return Interlocked.Increment(ref claimCount) == 1
                ? Task.FromResult(batch)
                : Task.FromResult<IReadOnlyList<OutboxMessage>>(Array.Empty<OutboxMessage>());
        }

        public Task MarkPublishedAsync(
            long messageId,
            string leaseToken,
            DateTimeOffset publishedAtUtc,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref publishedCount) == batch.Count)
                AllPublished.TrySetResult(true);
            return Task.CompletedTask;
        }

        public Task RecordFailureAsync(
            long messageId,
            string leaseToken,
            string failureCode,
            DateTimeOffset failedAtUtc,
            DateTimeOffset retryAtUtc,
            int maxAttempts,
            bool permanentFailure,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
