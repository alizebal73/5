using GameNet.Shared.Contracts.V1.Protocol;

namespace GameNet.Server.Infrastructure.Realtime;

public interface IAgentConnectionLeaseStore
{
    Task<AgentConnectionLeaseState?> TryAcquireAsync(
        AgentConnectionLeaseRequest request,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default);

    Task<bool> RenewAsync(
        string deviceId,
        string connectionId,
        string leaseToken,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default);

    Task<bool> RecordHeartbeatAsync(
        AgentHeartbeat heartbeat,
        string connectionId,
        string leaseToken,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default);

    Task<AgentCommandLeaseTarget?> GetFreshLeaseForCommandAsync(
        string deviceId,
        TimeSpan heartbeatFreshness,
        CancellationToken cancellationToken = default);

    Task<bool> IsCurrentLeaseAsync(
        string deviceId,
        string connectionId,
        string leaseToken,
        CancellationToken cancellationToken = default);

    Task ReleaseIfOwnerAsync(
        string deviceId,
        string connectionId,
        string leaseToken,
        CancellationToken cancellationToken = default);
}

public sealed record AgentCommandLeaseTarget(
    string DeviceId,
    string ConnectionId,
    string LeaseToken,
    DateTimeOffset LeaseExpiresAtUtc,
    DateTimeOffset LastHeartbeatAtUtc,
    string? AgentVersion,
    string? StationState);
