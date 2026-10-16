namespace GameNet.Server.Modules.Stations.Application;

/// <summary>
/// One definition of an online Agent for operator display and Session-start admission.
/// A bound device is not online unless its credential is active and its lease/heartbeat is fresh.
/// </summary>
public static class AgentAvailabilityPolicy
{
    public static readonly TimeSpan HeartbeatFreshness = TimeSpan.FromMinutes(2);
    public static readonly TimeSpan MaximumFutureHeartbeatSkew = TimeSpan.FromSeconds(2);

    public static bool IsOnline(
        bool hasActiveCredential,
        DateTimeOffset leaseExpiresAtUtc,
        DateTimeOffset? lastHeartbeatAtUtc,
        DateTimeOffset nowUtc)
    {
        if (!hasActiveCredential || leaseExpiresAtUtc <= nowUtc || lastHeartbeatAtUtc is not DateTimeOffset heartbeat)
            return false;

        return heartbeat >= nowUtc.Subtract(HeartbeatFreshness) &&
               heartbeat <= nowUtc.Add(MaximumFutureHeartbeatSkew);
    }
}
