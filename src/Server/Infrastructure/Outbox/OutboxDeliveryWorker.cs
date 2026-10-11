using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GameNet.Server.Infrastructure.Outbox;

public sealed class OutboxDeliveryWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<OutboxDeliveryOptions> options,
    TimeProvider timeProvider,
    ILogger<OutboxDeliveryWorker> logger) : BackgroundService
{
    private readonly OutboxDeliveryOptions settings = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var consecutiveClaimFailures = 0;

        while (!stoppingToken.IsCancellationRequested)
        {
            IReadOnlyList<OutboxMessage> batch;
            try
            {
                await using var claimScope = scopeFactory.CreateAsyncScope();
                var dispatcher = claimScope.ServiceProvider.GetRequiredService<IOutboxDispatcher>();
                batch = await dispatcher.ClaimBatchAsync(
                    settings.BatchSize,
                    TimeSpan.FromSeconds(settings.LeaseDurationSeconds),
                    stoppingToken,
                    settings.MaxAttempts);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                consecutiveClaimFailures = Math.Min(consecutiveClaimFailures + 1, 30);
                var retryDelay = OutboxRetryPolicy.CalculateDelay(
                    consecutiveClaimFailures,
                    TimeSpan.FromSeconds(settings.PollIntervalSeconds),
                    TimeSpan.FromSeconds(Math.Max(
                        settings.PollIntervalSeconds,
                        settings.MaximumRetryDelaySeconds)),
                    Random.Shared.NextDouble());

                // Do not write exception messages or payloads to logs; they may contain sensitive data.
                logger.LogError(
                    "Outbox claim failed ({FailureType}); next retry is delayed using bounded backoff.",
                    exception.GetType().Name);
                if (!await DelayAsync(retryDelay, stoppingToken))
                    break;
                continue;
            }

            consecutiveClaimFailures = 0;
            if (batch.Count == 0)
            {
                if (!await DelayAsync(TimeSpan.FromSeconds(settings.PollIntervalSeconds), stoppingToken))
                    break;
                continue;
            }

            foreach (var message in batch)
            {
                if (stoppingToken.IsCancellationRequested)
                    break;

                try
                {
                    await ProcessMessageAsync(message, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exception)
                {
                    // A lost lease is intentionally not force-completed. It becomes claimable
                    // after expiry and the consumer's EventId idempotency protects redelivery.
                    logger.LogError(
                        "Outbox processing step failed ({FailureType}) for event {EventId}; lease recovery will handle it.",
                        exception.GetType().Name,
                        message.EventId);
                }
            }
        }
    }

    private async Task ProcessMessageAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        var leaseToken = message.LeaseToken
            ?? throw new InvalidOperationException("OUTBOX_CLAIM_RETURNED_NO_LEASE");
        await using var scope = scopeFactory.CreateAsyncScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IOutboxDispatcher>();
        var registry = scope.ServiceProvider.GetRequiredService<OutboxHandlerRegistry>();
        var now = timeProvider.GetUtcNow();

        bool processed;
        try
        {
            processed = await registry.DispatchAsync(message, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OutboxPermanentDeliveryException exception)
        {
            await dispatcher.RecordFailureAsync(
                message.Id,
                leaseToken,
                exception.FailureCode,
                now,
                now,
                settings.MaxAttempts,
                permanentFailure: true,
                cancellationToken);

            logger.LogError(
                "Outbox event {EventId} was dead-lettered with code {FailureCode}.",
                message.EventId,
                exception.FailureCode);
            return;
        }
        catch (Exception exception)
        {
            var delay = OutboxRetryPolicy.CalculateDelay(
                message.AttemptCount,
                TimeSpan.FromSeconds(settings.InitialRetryDelaySeconds),
                TimeSpan.FromSeconds(settings.MaximumRetryDelaySeconds),
                Random.Shared.NextDouble());
            await dispatcher.RecordFailureAsync(
                message.Id,
                leaseToken,
                "handler_failed",
                now,
                now.Add(delay),
                settings.MaxAttempts,
                permanentFailure: false,
                cancellationToken);

            logger.LogWarning(
                "Outbox event {EventId} delivery failed on attempt {AttemptCount} ({FailureType}); retry/dead-letter policy was applied.",
                message.EventId,
                message.AttemptCount,
                exception.GetType().Name);
            return;
        }

        await dispatcher.MarkPublishedAsync(message.Id, leaseToken, timeProvider.GetUtcNow(), cancellationToken);
        if (processed)
        {
            logger.LogInformation(
                "Outbox event {EventId} of type {EventType} was processed.",
                message.EventId,
                message.Type);
        }
        else
        {
            logger.LogInformation(
                "Duplicate Outbox event {EventId} was suppressed by the durable inbox.",
                message.EventId);
        }
    }

    private static async Task<bool> DelayAsync(
        TimeSpan delay,
        CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(delay, cancellationToken);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }
}
