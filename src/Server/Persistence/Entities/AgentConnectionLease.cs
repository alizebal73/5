namespace GameNet.Server.Persistence.Entities;

public sealed class AgentConnectionLease
{
    public string DeviceId { get; set; } = string.Empty;
    public string ConnectionId { get; set; } = string.Empty;
    public string LeaseToken { get; set; } = string.Empty;
    public DateTimeOffset LeaseExpiresAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public DateTimeOffset? LastHeartbeatAtUtc { get; set; }
    public string? AgentVersion { get; set; }
    public string? StationState { get; set; }
}
