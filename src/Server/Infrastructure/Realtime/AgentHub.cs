using GameNet.Server.Infrastructure.Configuration;
using GameNet.Server.Infrastructure.Security;
using GameNet.Shared.Contracts.V1.Protocol;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;

namespace GameNet.Server.Infrastructure.Realtime;

[Authorize(Policy = "AgentTransport")]
public sealed class AgentHub(
    IAgentConnectionLeaseStore leases,
    GameNet.Shared.Primitives.IGameClock clock,
    IOptions<GameNetOptions> options) : Hub
{
    private const string LeaseTokenKey = "GameNet.Agent.LeaseToken";
    private const string DeviceIdKey = "GameNet.Agent.DeviceId";

    public async Task<AgentConnectionLeaseState> ConnectAsync(AgentConnectionLeaseRequest request)
    {
        var deviceId = RequireDeviceId();
        if (!string.Equals(deviceId, request.DeviceId, StringComparison.Ordinal))
            throw new HubException("AGENT_DEVICE_ID_MISMATCH");

        var lease = await leases.TryAcquireAsync(
            request with { ConnectionId = Context.ConnectionId },
            TimeSpan.FromSeconds(options.Value.Agent.LeaseDurationSeconds),
            Context.ConnectionAborted);

        if (lease is null)
            throw new HubException("AGENT_LEASE_ACQUISITION_FAILED");

        Context.Items[LeaseTokenKey] = lease.LeaseToken;
        Context.Items[DeviceIdKey] = deviceId;
        return lease;
    }

    public async Task<bool> HeartbeatAsync(AgentHeartbeat heartbeat)
    {
        var deviceId = RequireDeviceId();
        var token = RequireLeaseToken();

        if (!string.Equals(deviceId, heartbeat.DeviceId, StringComparison.Ordinal))
            throw new HubException("AGENT_DEVICE_ID_MISMATCH");

        return await leases.RecordHeartbeatAsync(
            heartbeat,
            Context.ConnectionId,
            token,
            TimeSpan.FromSeconds(options.Value.Agent.LeaseDurationSeconds),
            Context.ConnectionAborted);
    }

    public Task<AgentReconciliationResponse> ReconcileAsync(AgentReconciliationRequest request)
    {
        var deviceId = RequireDeviceId();
        _ = RequireLeaseToken();

        if (!string.Equals(deviceId, request.DeviceId, StringComparison.Ordinal))
            throw new HubException("AGENT_DEVICE_ID_MISMATCH");

        return Task.FromResult(new AgentReconciliationResponse(
            deviceId,
            clock.UtcNow,
            AgentProtocolVersions.V1,
            true,
            AgentReconciliationScope.LeaseAndIdentity));
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (Context.Items.TryGetValue(LeaseTokenKey, out var tokenValue) &&
            tokenValue is string token &&
            Context.Items.TryGetValue(DeviceIdKey, out var deviceValue) &&
            deviceValue is string deviceId)
        {
            await leases.ReleaseIfOwnerAsync(deviceId, Context.ConnectionId, token, CancellationToken.None);
        }

        await base.OnDisconnectedAsync(exception);
    }

    private string RequireDeviceId() =>
        Context.User?.FindFirst("device_id")?.Value
        ?? throw new HubException("AGENT_DEVICE_ID_CLAIM_MISSING");

    private string RequireLeaseToken() =>
        Context.Items.TryGetValue(LeaseTokenKey, out var tokenValue) &&
        tokenValue is string token &&
        !string.IsNullOrWhiteSpace(token)
            ? token
            : throw new HubException("AGENT_LEASE_NOT_ACQUIRED");
}
