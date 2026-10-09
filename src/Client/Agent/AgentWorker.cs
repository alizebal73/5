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

                    var reconciliation = await transport.ReconcileAsync(identity.DeviceId, "heartbeat", stoppingToken);
                    if (!string.Equals(reconciliation.DeviceId, identity.DeviceId, StringComparison.Ordinal) ||
                        reconciliation.AgentProtocolVersion != AgentProtocolVersions.V1 ||
                        !reconciliation.AuthoritativeConnection ||
                        reconciliation.Scope != AgentReconciliationScope.LeaseAndIdentity)
                    {
                        throw new InvalidOperationException("Server reconciliation did not confirm the authoritative Agent identity and lease.");
                    }

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
