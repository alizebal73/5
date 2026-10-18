using GameNet.Agent;
using GameNet.Shared.Contracts.V1.Protocol;
using Xunit;

namespace GameNet.Agent.Tests;

public sealed class AgentCommandGuardTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-09T12:00:00Z");
    private static readonly AgentCommandEnvelope Valid = new(Guid.NewGuid(), "pc-01", Guid.NewGuid(), "lease-current",
        AgentCommandType.HealthProbe, Now.AddSeconds(-1), Now.AddSeconds(9));

    [Fact]
    public void Accepts_a_fresh_allowlisted_command_for_the_current_lease() =>
        Assert.Null(AgentCommandGuard.Validate(Valid, "pc-01", "lease-current", Now.AddSeconds(15), Now));

    [Theory]
    [InlineData("pc-02", "lease-current", "agent.command.device_mismatch")]
    [InlineData("pc-01", "lease-old", "agent.command.lease_mismatch")]
    [InlineData("pc-01", "", "agent.command.lease_mismatch")]
    public void Rejects_commands_from_another_device_or_lease(string deviceId, string token, string expected)
    {
        var error = AgentCommandGuard.Validate(Valid, deviceId, token, Now.AddSeconds(15), Now);
        Assert.Equal(expected, error);
    }

    [Fact]
    public void Rejects_expired_commands() =>
        Assert.Equal("agent.command.stale", AgentCommandGuard.Validate(Valid with { ExpiresAtUtc = Now }, "pc-01", "lease-current", Now.AddSeconds(15), Now));

    [Fact]
    public void Rejects_commands_issued_too_far_in_the_future() =>
        Assert.Equal("agent.command.stale", AgentCommandGuard.Validate(Valid with { IssuedAtUtc = Now.AddSeconds(3), ExpiresAtUtc = Now.AddSeconds(8) }, "pc-01", "lease-current", Now.AddSeconds(15), Now));

    [Fact]
    public void Rejects_commands_when_the_current_lease_has_expired() =>
        Assert.Equal("agent.command.lease_expired", AgentCommandGuard.Validate(Valid, "pc-01", "lease-current", Now, Now));

    [Fact]
    public void Rejects_unknown_command_types_fail_closed() =>
        Assert.Equal("agent.command.unsupported", AgentCommandGuard.Validate(Valid with { Type = (AgentCommandType)999 }, "pc-01", "lease-current", Now.AddSeconds(15), Now));

    [Fact]
    public void Rejects_empty_command_and_station_ids() =>
        Assert.Equal("agent.command.invalid_identity", AgentCommandGuard.Validate(Valid with { CommandId = Guid.Empty }, "pc-01", "lease-current", Now.AddSeconds(15), Now));
}
