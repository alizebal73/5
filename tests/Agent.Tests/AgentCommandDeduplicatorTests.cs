using GameNet.Agent;
using GameNet.Shared.Contracts.V1.Protocol;
using Xunit;

namespace GameNet.Agent.Tests;

public sealed class AgentCommandDeduplicatorTests
{
    [Fact]
    public async Task Repeated_identical_command_executes_only_once_and_returns_same_acknowledgement()
    {
        var deduplicator = new AgentCommandDeduplicator(TimeProvider.System);
        var command = NewCommand();
        var executions = 0;

        Task<AgentCommandAcknowledgement> Execute(AgentCommandEnvelope envelope, CancellationToken ct)
        {
            Interlocked.Increment(ref executions);
            var now = DateTimeOffset.UtcNow;
            return Task.FromResult(new AgentCommandAcknowledgement(
                envelope.CommandId, envelope.DeviceId, envelope.StationId, AgentCommandStatus.Succeeded,
                now, now, null, "1.0.0", "Ready"));
        }

        var first = deduplicator.ExecuteOnceAsync(command, Execute);
        var second = deduplicator.ExecuteOnceAsync(command, Execute);
        var a = await first;
        var b = await second;

        Assert.Equal(1, executions);
        Assert.Equal(a, b);
        Assert.Equal(AgentCommandStatus.Succeeded, b.Status);
    }

    [Fact]
    public async Task Reusing_command_id_with_different_payload_is_rejected_without_execution()
    {
        var deduplicator = new AgentCommandDeduplicator(TimeProvider.System);
        var command = NewCommand();
        var executions = 0;
        Task<AgentCommandAcknowledgement> Execute(AgentCommandEnvelope envelope, CancellationToken ct)
        {
            Interlocked.Increment(ref executions);
            var now = DateTimeOffset.UtcNow;
            return Task.FromResult(new AgentCommandAcknowledgement(
                envelope.CommandId, envelope.DeviceId, envelope.StationId, AgentCommandStatus.Succeeded,
                now, now, null, "1.0.0", "Ready"));
        }

        var first = await deduplicator.ExecuteOnceAsync(command, Execute);
        var conflicting = await deduplicator.ExecuteOnceAsync(command with { StationId = Guid.NewGuid() }, Execute);

        Assert.Equal(1, executions);
        Assert.Equal(AgentCommandStatus.Succeeded, first.Status);
        Assert.Equal(AgentCommandStatus.Rejected, conflicting.Status);
        Assert.Equal("agent.command.id_reused", conflicting.ErrorCode);
    }

    [Fact]
    public async Task Cache_saturation_fails_closed_without_evicting_unexpired_command_ids()
    {
        var deduplicator = new AgentCommandDeduplicator(TimeProvider.System, capacity: 1);
        var first = NewCommand();
        var second = NewCommand();
        var executions = 0;
        Task<AgentCommandAcknowledgement> Execute(AgentCommandEnvelope envelope, CancellationToken ct)
        {
            Interlocked.Increment(ref executions);
            var now = DateTimeOffset.UtcNow;
            return Task.FromResult(new AgentCommandAcknowledgement(
                envelope.CommandId, envelope.DeviceId, envelope.StationId, AgentCommandStatus.Succeeded,
                now, now, null, "1.0.0", "Ready"));
        }

        var firstAck = await deduplicator.ExecuteOnceAsync(first, Execute);
        var overflowAck = await deduplicator.ExecuteOnceAsync(second, Execute);
        var repeatedAck = await deduplicator.ExecuteOnceAsync(first, Execute);

        Assert.Equal(1, executions);
        Assert.Equal(AgentCommandStatus.Rejected, overflowAck.Status);
        Assert.Equal("agent.command.dedup_capacity", overflowAck.ErrorCode);
        Assert.Equal(firstAck, repeatedAck);
    }

    private static AgentCommandEnvelope NewCommand() => new(
        Guid.NewGuid(), "pc-01", Guid.NewGuid(), "lease-1", AgentCommandType.HealthProbe,
        DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddSeconds(10));
}
