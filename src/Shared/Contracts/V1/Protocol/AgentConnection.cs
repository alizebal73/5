namespace GameNet.Shared.Contracts.V1.Protocol;

public static class AgentProtocolVersions
{
    public const int V1 = 1;
}

public sealed record AgentConnectionLeaseRequest(
    string DeviceId,
    string ConnectionId,
    DateTimeOffset RequestedAtUtc);

public sealed record AgentConnectionLeaseState(
    string DeviceId,
    string ConnectionId,
    string LeaseToken,
    DateTimeOffset LeaseExpiresAtUtc,
    bool IsAuthoritative);

public sealed record AgentReconnectRequest(
    string DeviceId,
    string ConnectionId,
    DateTimeOffset RequestedAtUtc);
