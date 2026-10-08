using GameNet.Agent.Identity;
using GameNet.Agent.Transport;
using GameNet.Shared.Contracts.V1.Protocol;
using Microsoft.Extensions.Options;

namespace GameNet.Agent;

public sealed class AgentWorker(
    IAgentIdentityStore identityStore,
    IAgentTransport transport,
    IOptions<AgentTransportOptions> options,
    ILogger<AgentWorker> logger,
    TimeProvider timeProvider) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var identity = await identityStore.GetOrCreateAsync(stoppingToken);

        logger.LogInformation(
            "GameNet Agent service started. DeviceId={DeviceId} at {Time}",
            identity.DeviceId,
            timeProvider.GetUtcNow());

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var succeeded = false;

                try
                {
                    await transport.ConnectAsync(identity, stoppingToken);

                    var heartbeat = new AgentHeartbeat(
                        identity.DeviceId,
                        timeProvider.GetUtcNow(),
                        typeof(AgentWorker).Assembly.GetName().Version?.ToString() ?? "0.0.0",
                        "Ready");

                    if (!await transport.HeartbeatAsync(heartbeat, stoppingToken))
                        throw new InvalidOperationException("Server rejected the Agent heartbeat lease.");

                    await transport.ReconcileAsync(identity.DeviceId, "heartbeat", stoppingToken);
                    succeeded = true;
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exception)
                {
                    logger.LogWarning(
                        exception,
                        "Agent transport cycle failed. DeviceId={DeviceId}; retrying in {RetrySeconds}s",
                        identity.DeviceId,
                        options.Value.InitialRetrySeconds);
                }

                await Task.Delay(
                    TimeSpan.FromSeconds(succeeded ? options.Value.HeartbeatIntervalSeconds : options.Value.InitialRetrySeconds),
                    stoppingToken);
            }
        }
        finally
        {
            await transport.DisposeAsync();
        }
    }
}
