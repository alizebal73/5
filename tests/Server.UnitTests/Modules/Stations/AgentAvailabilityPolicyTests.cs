using GameNet.Server.Modules.Stations.Application;
using Xunit;

namespace GameNet.Server.UnitTests.Modules.Stations;

public sealed class AgentAvailabilityPolicyTests
{
    [Fact]
    public void Requires_active_credential_and_unexpired_lease()
    {
        var now = DateTimeOffset.Parse("2026-10-16T00:00:00Z");

        Assert.False(AgentAvailabilityPolicy.IsOnline(
            hasActiveCredential: false,
            leaseExpiresAtUtc: now.AddMinutes(5),
            lastHeartbeatAtUtc: now,
            nowUtc: now));
        Assert.False(AgentAvailabilityPolicy.IsOnline(
            hasActiveCredential: true,
            leaseExpiresAtUtc: now,
            lastHeartbeatAtUtc: now,
            nowUtc: now));
    }

    [Fact]
    public void Requires_fresh_heartbeat_and_rejects_far_future_heartbeat()
    {
        var now = DateTimeOffset.Parse("2026-10-16T00:00:00Z");
        var expires = now.AddMinutes(5);

        Assert.False(AgentAvailabilityPolicy.IsOnline(true, expires, null, now));
        Assert.False(AgentAvailabilityPolicy.IsOnline(true, expires, now.AddMinutes(-2).AddTicks(-1), now));
        Assert.False(AgentAvailabilityPolicy.IsOnline(true, expires, now.AddSeconds(2).AddTicks(1), now));
    }

    [Fact]
    public void Accepts_fresh_heartbeat_at_policy_boundaries()
    {
        var now = DateTimeOffset.Parse("2026-10-16T00:00:00Z");
        var expires = now.AddMinutes(5);

        Assert.True(AgentAvailabilityPolicy.IsOnline(true, expires, now, now));
        Assert.True(AgentAvailabilityPolicy.IsOnline(
            true, expires, now.Subtract(AgentAvailabilityPolicy.HeartbeatFreshness), now));
        Assert.True(AgentAvailabilityPolicy.IsOnline(
            true, expires, now.Add(AgentAvailabilityPolicy.MaximumFutureHeartbeatSkew), now));
    }
}
