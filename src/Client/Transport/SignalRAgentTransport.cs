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
    private DateTimeOffset? leaseExpiresAtUtc;

    public bool IsConnected =>
        connection?.State == HubConnectionState.Connected &&
        !string.IsNullOrWhiteSpace(leaseToken) &&
        leaseExpiresAtUtc > timeProvider.GetUtcNow();

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
                    ClearLease();
                    logger.LogWarning(error, "Agent transport reconnecting. DeviceId={DeviceId}", agentIdentity.DeviceId);
                    return Task.CompletedTask;
                };

                connection.Reconnected += _ =>
                {
                    ClearLease();
                    logger.LogInformation("Agent transport reconnected; lease will be reacquired. DeviceId={DeviceId}", agentIdentity.DeviceId);
                    return Task.CompletedTask;
                };

                connection.Closed += error =>
                {
                    ClearLease();
                    logger.LogWarning(error, "Agent transport closed. DeviceId={DeviceId}", agentIdentity.DeviceId);
                    return Task.CompletedTask;
                };
            }

            if (connection.State == HubConnectionState.Disconnected)
            {
                ClearLease();
                await connection.StartAsync(cancellationToken);
            }

            if (connection.State != HubConnectionState.Connected)
                throw new InvalidOperationException("Agent Hub connection is not ready; retry after reconnect.");

            if (string.IsNullOrWhiteSpace(leaseToken) ||
                !leaseExpiresAtUtc.HasValue ||
                leaseExpiresAtUtc.Value <= timeProvider.GetUtcNow().AddSeconds(1))
            {
                await AcquireLeaseAsync(cancellationToken);
            }
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
            var accepted = await current.InvokeAsync<bool>("HeartbeatAsync", heartbeat, cancellationToken);
            if (!accepted)
            {
                ClearLease();
                logger.LogWarning(
                    "Server rejected the current Agent lease during heartbeat. DeviceId={DeviceId}; lease will be reacquired.",
                    heartbeat.DeviceId);
            }

            return accepted;
        }
        catch
        {
            ClearLease();
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
        try
        {
            return await current.InvokeAsync<AgentReconciliationResponse>(
                "ReconcileAsync",
                new AgentReconciliationRequest(deviceId, timeProvider.GetUtcNow(), reason),
                cancellationToken);
        }
        catch
        {
            ClearLease();
            throw;
        }
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

        var connectionId = current.ConnectionId;
        if (string.IsNullOrWhiteSpace(connectionId))
            throw new InvalidOperationException("Agent Hub connection has no connection ID.");

        var lease = await current.InvokeAsync<AgentConnectionLeaseState>(
            "ConnectAsync",
            new AgentConnectionLeaseRequest(
                agentIdentity.DeviceId,
                connectionId,
                timeProvider.GetUtcNow()),
            cancellationToken);

        if (!lease.IsAuthoritative ||
            !string.Equals(lease.DeviceId, agentIdentity.DeviceId, StringComparison.Ordinal) ||
            !string.Equals(lease.ConnectionId, connectionId, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(lease.LeaseToken) ||
            lease.LeaseExpiresAtUtc <= timeProvider.GetUtcNow())
        {
            ClearLease();
            throw new InvalidOperationException(
                $"Server did not grant a valid authoritative Agent lease for DeviceId={agentIdentity.DeviceId}.");
        }

        leaseToken = lease.LeaseToken;
        leaseExpiresAtUtc = lease.LeaseExpiresAtUtc;
    }

    private void ClearLease()
    {
        leaseToken = null;
        leaseExpiresAtUtc = null;
    }
}
