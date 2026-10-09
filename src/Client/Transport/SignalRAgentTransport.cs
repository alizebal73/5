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
    public event Func<AgentCommandEnvelope, CancellationToken, Task<AgentCommandAcknowledgement>>? CommandReceived;

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

                connection.On<AgentCommandEnvelope>("ReceiveCommand", HandleIncomingCommandAsync);

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

    public async Task<bool> AcknowledgeCommandAsync(
        AgentCommandAcknowledgement acknowledgement,
        CancellationToken cancellationToken = default)
    {
        var current = connection ?? throw new InvalidOperationException("Agent transport is not initialized.");
        if (!IsConnected)
            throw new InvalidOperationException("Agent cannot acknowledge a command without its current authoritative lease.");

        var accepted = await current.InvokeAsync<bool>(
            "AcknowledgeCommandAsync", acknowledgement, cancellationToken);
        if (!accepted)
            logger.LogWarning(
                "Server rejected command acknowledgement. CommandId={CommandId}; DeviceId={DeviceId}; Status={Status}",
                acknowledgement.CommandId, acknowledgement.DeviceId, acknowledgement.Status);
        return accepted;
    }

    public async ValueTask DisposeAsync()
    {
        if (connection is not null)
            await connection.DisposeAsync();
        gate.Dispose();
    }

    private async Task HandleIncomingCommandAsync(AgentCommandEnvelope command)
    {
        var identitySnapshot = identity;
        var tokenSnapshot = leaseToken;
        var expirySnapshot = leaseExpiresAtUtc;
        var now = timeProvider.GetUtcNow();
        var errorCode = identitySnapshot is null
            ? "agent.command.identity_unavailable"
            : AgentCommandGuard.Validate(command, identitySnapshot.DeviceId, tokenSnapshot, expirySnapshot, now);

        AgentCommandAcknowledgement acknowledgement;
        if (errorCode is not null)
        {
            acknowledgement = new AgentCommandAcknowledgement(
                command.CommandId, command.DeviceId, command.StationId,
                AgentCommandStatus.Rejected, now, now, errorCode,
                typeof(SignalRAgentTransport).Assembly.GetName().Version?.ToString() ?? "0.0.0",
                null);
        }
        else
        {
            try
            {
                var handler = CommandReceived;
                acknowledgement = handler is null
                    ? new AgentCommandAcknowledgement(
                        command.CommandId, command.DeviceId, command.StationId,
                        AgentCommandStatus.Rejected, now, timeProvider.GetUtcNow(),
                        "agent.command.handler_unavailable", null, null)
                    : await handler(command, CancellationToken.None);

                if (acknowledgement.CommandId != command.CommandId ||
                    !string.Equals(acknowledgement.DeviceId, command.DeviceId, StringComparison.Ordinal) ||
                    acknowledgement.StationId != command.StationId ||
                    acknowledgement.FinishedAtUtc < acknowledgement.StartedAtUtc)
                {
                    throw new InvalidOperationException("Agent command handler returned a mismatched acknowledgement.");
                }
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Agent command handling failed. CommandId={CommandId}; DeviceId={DeviceId}", command.CommandId, command.DeviceId);
                var failedAt = timeProvider.GetUtcNow();
                acknowledgement = new AgentCommandAcknowledgement(
                    command.CommandId, command.DeviceId, command.StationId,
                    AgentCommandStatus.Failed, now, failedAt, "agent.command.execution_failed",
                    typeof(SignalRAgentTransport).Assembly.GetName().Version?.ToString() ?? "0.0.0",
                    null);
            }
        }

        try
        {
            _ = await AcknowledgeCommandAsync(acknowledgement, CancellationToken.None);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Could not acknowledge Agent command. CommandId={CommandId}; DeviceId={DeviceId}", command.CommandId, command.DeviceId);
        }
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
