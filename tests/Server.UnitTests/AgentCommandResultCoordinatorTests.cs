using GameNet.Server.Infrastructure.Realtime;
using GameNet.Shared.Contracts.V1.Protocol;
using Xunit;

namespace GameNet.Server.UnitTests;

public sealed class AgentCommandResultCoordinatorTests
{
    [Fact]
    public async Task Accepts_ack_only_for_original_connection_current_lease_and_command()
    {
        var coordinator = new AgentCommandResultCoordinator();
        var command = new AgentCommandEnvelope(Guid.NewGuid(), "pc-01", Guid.NewGuid(), "lease-current", AgentCommandType.HealthProbe,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddSeconds(10));
        var completion = coordinator.Register(command, "connection-1");
        var now = command.IssuedAtUtc.AddSeconds(1);
        var ack = new AgentCommandAcknowledgement(command.CommandId, command.DeviceId, command.StationId, AgentCommandStatus.Succeeded,
            now, now, null, "1.0.0", "Ready");

        Assert.False(coordinator.TryComplete(command.DeviceId, "connection-2", command.LeaseToken, ack));
        Assert.False(coordinator.TryComplete(command.DeviceId, "connection-1", "lease-old", ack));
        Assert.False(completion.IsCompleted);
        Assert.True(coordinator.TryComplete(command.DeviceId, "connection-1", command.LeaseToken, ack));
        var result = await completion.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.Equal(command.CommandId, result.CommandId);
        coordinator.Remove(command.CommandId);
    }

    [Fact]
    public void Rejects_wrong_station_and_expired_acknowledgements()
    {
        var coordinator = new AgentCommandResultCoordinator();
        var issued = DateTimeOffset.UtcNow;
        var command = new AgentCommandEnvelope(Guid.NewGuid(), "pc-01", Guid.NewGuid(), "lease-current", AgentCommandType.HealthProbe,
            issued, issued.AddSeconds(2));
        var completion = coordinator.Register(command, "connection-1");
        var wrongStation = new AgentCommandAcknowledgement(command.CommandId, command.DeviceId, Guid.NewGuid(), AgentCommandStatus.Succeeded,
            issued.AddSeconds(1), issued.AddSeconds(1), null, "1.0.0", "Ready");
        var expired = wrongStation with { StationId = command.StationId, FinishedAtUtc = issued.AddSeconds(3) };

        Assert.False(coordinator.TryComplete(command.DeviceId, "connection-1", command.LeaseToken, wrongStation));
        Assert.False(coordinator.TryComplete(command.DeviceId, "connection-1", command.LeaseToken, expired));
        Assert.False(completion.IsCompleted);
        coordinator.Remove(command.CommandId);
    }
}
