using GameNet.Shared.Contracts.V1.Protocol;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Options;

namespace GameNet.Agent.Transport;

public sealed class SignalRAgentTransport(
    IOptions<AgentTransportOptions> options,
    IAgentAccessTokenProvider accessTokenProvider,
    ILogger<SignalRAgentTransport> logger,
    TimeProvider timeProvider) : IAgentTransport
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private HubConnection? connection;
    private GameNet.Agent.AgentIdentity? identity;
    private string? leaseToken;

    public bool IsConnected =>
        connection?.State == HubConnectionState.Connected &&
        !string.IsNullOrWhiteSpace(leaseToken);

    public async Task ConnectAsync(GameNet.Agent.AgentIdentity agentIdentity, CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            identity = agentIdentity;

            if (connection is null)
            {
                var hubUrl = new Uri($"{options.Value.ServerBaseUrl.TrimEnd('/')}/hubs/agent");
                connection = new HubConnectionBuilder()
                    .WithUrl(hubUrl, builder =>
                    {
                        builder.AccessTokenProvider = async () =>
                            await accessTokenProvider.GetAccessTokenAsync(agentIdentity.DeviceId, CancellationToken.None);
                    })
                    .WithAutomaticReconnect(new[]
                    {
                        TimeSpan.Zero,
                        TimeSpan.FromSeconds(2),
                        TimeSpan.FromSeconds(10),
                        TimeSpan.FromSeconds(30)
                    })
                    .Build();

                connection.Reconnecting += error =>
                {
                    leaseToken = null;
                    logger.LogWarning(error, "Agent transport reconnecting. DeviceId={DeviceId}", agentIdentity.DeviceId);
                    return Task.CompletedTask;
                };

                connection.Reconnected += _ =>
                {
                    leaseToken = null;
                    logger.LogInformation("Agent transport reconnected; lease will be reacquired. DeviceId={DeviceId}", agentIdentity.DeviceId);
                    return Task.CompletedTask;
                };

                connection.Closed += error =>
                {
                    leaseToken = null;
                    logger.LogWarning(error, "Agent transport closed. DeviceId={DeviceId}", agentIdentity.DeviceId);
                    return Task.CompletedTask;
                };
            }

            if (connection.State == HubConnectionState.Disconnected)
                await connection.StartAsync(cancellationToken);

            if (string.IsNullOrWhiteSpace(leaseToken))
                await AcquireLeaseAsync(cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<bool> HeartbeatAsync(AgentHeartbeat heartbeat, CancellationToken cancellationToken = default)
    {
        if (!IsConnected)
            await ConnectAsync(identity ?? throw new InvalidOperationException("Agent identity is not initialized."), cancellationToken);

        var current = connection ?? throw new InvalidOperationException("Agent transport is not initialized.");
        try
        {
            return await current.InvokeAsync<bool>("HeartbeatAsync", heartbeat, cancellationToken);
        }
        catch (HubException)
        {
            leaseToken = null;
            throw;
        }
    }

    public async Task<AgentReconciliationResponse> ReconcileAsync(
        string deviceId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        if (!IsConnected)
            await ConnectAsync(identity ?? throw new InvalidOperationException("Agent identity is not initialized."), cancellationToken);

        var current = connection ?? throw new InvalidOperationException("Agent transport is not initialized.");
        return await current.InvokeAsync<AgentReconciliationResponse>(
            "ReconcileAsync",
            new AgentReconciliationRequest(deviceId, timeProvider.GetUtcNow(), reason),
            cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (connection is not null)
            await connection.DisposeAsync();
        gate.Dispose();
    }

    private async Task AcquireLeaseAsync(CancellationToken cancellationToken)
    {
        var agentIdentity = identity ?? throw new InvalidOperationException("Agent identity is not initialized.");
        var current = connection ?? throw new InvalidOperationException("Agent transport is not initialized.");

        var lease = await current.InvokeAsync<AgentConnectionLeaseState>(
            "ConnectAsync",
            new AgentConnectionLeaseRequest(
                agentIdentity.DeviceId,
                current.ConnectionId ?? string.Empty,
                timeProvider.GetUtcNow()),
            cancellationToken);

        if (!lease.IsAuthoritative)
            throw new InvalidOperationException($"Server did not grant authoritative Agent lease for DeviceId={agentIdentity.DeviceId}.");

        leaseToken = lease.LeaseToken;
    }
}
