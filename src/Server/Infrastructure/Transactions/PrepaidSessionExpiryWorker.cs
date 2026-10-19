using GameNet.Server.Modules.Sessions.Application;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GameNet.Server.Infrastructure.Transactions;

/// <summary>
/// Server-side expiry sweep for funded Sessions. The persisted Wallet reserve is the
/// authority; Desktop countdowns are only a display. A restart resumes this sweep.
/// </summary>
public sealed class PrepaidSessionExpiryWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<PrepaidSessionExpiryWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var expiry = scope.ServiceProvider.GetRequiredService<IPrepaidSessionExpiryService>();
                var expired = await expiry.ExpireDueAsync(stoppingToken);
                if (expired > 0)
                    logger.LogInformation("Expired {Count} prepaid GameNet Session(s).", expired);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Prepaid Session expiry sweep failed; it will retry on the next tick.");
            }
        }
    }
}
