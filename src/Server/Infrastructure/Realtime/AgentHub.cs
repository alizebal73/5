using GameNet.Server.Infrastructure.Configuration;
using GameNet.Server.Infrastructure.Security;
using GameNet.Shared.Contracts.V1.Protocol;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;

namespace GameNet.Server.Infrastructure.Realtime;

[Authorize(Policy = "AgentTransport")]
public sealed class AgentHub(
    IAgentCredentialService credentials,
    IAgentConnectionLeaseStore leases,
    AgentCommandResultCoordinator commandResults,
    GameNet.Shared.Primitives.IGameClock clock,
    IOptions<GameNetOptions> options) : Hub
{
    private const string LeaseTokenKey = "GameNet.Agent.LeaseToken";
    private const string DeviceIdKey = "GameNet.Agent.DeviceId";

    public async Task<AgentConnectionLeaseState> ConnectAsync(AgentConnectionLeaseRequest request)
    {
        var deviceId = RequireDeviceId();
        var credentialId = RequireCredentialId();
        await EnsureCredentialActiveAsync(deviceId, credentialId);

        if (!string.Equals(deviceId, request.DeviceId, StringComparison.Ordinal))
            throw new HubException("AGENT_DEVICE_ID_MISMATCH");

        var lease = await leases.TryAcquireAsync(
            request with { ConnectionId = Context.ConnectionId },
            credentialId,
            TimeSpan.FromSeconds(options.Value.Agent.LeaseDurationSeconds),
            Context.ConnectionAborted);

        if (lease is null)
            throw new HubException("AGENT_LEASE_ACQUISITION_FAILED");

        try
        {
            if (!lease.IsAuthoritative ||
                !string.Equals(lease.DeviceId, deviceId, StringComparison.Ordinal) ||
                !string.Equals(lease.ConnectionId, Context.ConnectionId, StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(lease.LeaseToken))
            {
                throw new HubException("AGENT_LEASE_ALREADY_HELD");
            }

            // Re-check after acquiring the lease. If a credential was revoked or
            // rotated between the first check and lease acquisition, release only
            // this connection's lease and fail closed.
            await EnsureCredentialActiveAsync(deviceId, credentialId);
        }
        catch
        {
            if (!string.IsNullOrWhiteSpace(lease.LeaseToken))
            {
                await leases.ReleaseIfOwnerAsync(
                    deviceId,
                    Context.ConnectionId,
                    lease.LeaseToken,
                    CancellationToken.None);
            }
            throw;
        }

        Context.Items[LeaseTokenKey] = lease.LeaseToken;
        Context.Items[DeviceIdKey] = deviceId;
        return lease;
    }

    public async Task<bool> HeartbeatAsync(AgentHeartbeat heartbeat)
    {
        var deviceId = RequireDeviceId();
        await EnsureCredentialActiveAsync(deviceId);
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

    public async Task<AgentReconciliationResponse> ReconcileAsync(AgentReconciliationRequest request)
    {
        var deviceId = RequireDeviceId();
        await EnsureCredentialActiveAsync(deviceId);
        var token = RequireLeaseToken();

        if (!string.Equals(deviceId, request.DeviceId, StringComparison.Ordinal))
            throw new HubException("AGENT_DEVICE_ID_MISMATCH");

        if (!await leases.IsCurrentLeaseAsync(deviceId, Context.ConnectionId, token, Context.ConnectionAborted))
            throw new HubException("AGENT_LEASE_NOT_AUTHORITATIVE");

        var rotationOperationId = await credentials.GetRequestedRotationOperationIdAsync(
            deviceId, RequireCredentialId(), Context.ConnectionAborted);

        return new AgentReconciliationResponse(
            deviceId,
            clock.UtcNow,
            AgentProtocolVersions.V1,
            true,
            AgentReconciliationScope.LeaseAndIdentity,
            rotationOperationId);
    }

    public async Task<bool> AcknowledgeCommandAsync(AgentCommandAcknowledgement acknowledgement)
    {
        ArgumentNullException.ThrowIfNull(acknowledgement);
        var deviceId = RequireDeviceId();
        await EnsureCredentialActiveAsync(deviceId);
        var token = RequireLeaseToken();

        if (!string.Equals(deviceId, acknowledgement.DeviceId, StringComparison.Ordinal))
            throw new HubException("AGENT_DEVICE_ID_MISMATCH");

        if (!await leases.IsCurrentLeaseAsync(deviceId, Context.ConnectionId, token, Context.ConnectionAborted))
            throw new HubException("AGENT_LEASE_NOT_AUTHORITATIVE");

        return commandResults.TryComplete(deviceId, Context.ConnectionId, token, acknowledgement);
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

    private Task EnsureCredentialActiveAsync(string deviceId) =>
        EnsureCredentialActiveAsync(deviceId, RequireCredentialId());

    private async Task EnsureCredentialActiveAsync(string deviceId, Guid credentialId)
    {
        if (!await credentials.IsCredentialActiveAsync(credentialId, deviceId, Context.ConnectionAborted))
            throw new HubException("AGENT_CREDENTIAL_REVOKED");
    }

    private string RequireDeviceId() =>
        Context.User?.FindFirst("device_id")?.Value
        ?? throw new HubException("AGENT_DEVICE_ID_CLAIM_MISSING");

    private Guid RequireCredentialId() =>
        Guid.TryParse(Context.User?.FindFirst("credential_id")?.Value, out var credentialId) &&
        credentialId != Guid.Empty
            ? credentialId
            : throw new HubException("AGENT_CREDENTIAL_ID_MISSING");

    private string RequireLeaseToken() =>
        Context.Items.TryGetValue(LeaseTokenKey, out var tokenValue) &&
        tokenValue is string token &&
        !string.IsNullOrWhiteSpace(token)
            ? token
            : throw new HubException("AGENT_LEASE_NOT_ACQUIRED");
}
