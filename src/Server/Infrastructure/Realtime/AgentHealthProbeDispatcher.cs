using System.Collections.Concurrent;
using GameNet.Shared.Contracts.V1.Protocol;
using Microsoft.AspNetCore.SignalR;

namespace GameNet.Server.Infrastructure.Realtime;

public interface IAgentHealthProbeDispatcher
{
    Task<AgentCommandAcknowledgement?> ProbeAsync(Guid stationId, string deviceId, CancellationToken cancellationToken = default);
}

public sealed class AgentCommandResultCoordinator
{
    private readonly ConcurrentDictionary<Guid, PendingCommand> pending = new();

    public Task<AgentCommandAcknowledgement> Register(AgentCommandEnvelope command, string connectionId)
    {
        var handle = new PendingCommand(command, connectionId);
        if (!pending.TryAdd(command.CommandId, handle))
            throw new InvalidOperationException("AGENT_COMMAND_ID_COLLISION");
        return handle.Completion.Task;
    }

    public bool TryComplete(
        string deviceId,
        string connectionId,
        string currentLeaseToken,
        AgentCommandAcknowledgement acknowledgement)
    {
        ArgumentNullException.ThrowIfNull(acknowledgement);
        if (!pending.TryGetValue(acknowledgement.CommandId, out var handle))
            return false;

        var command = handle.Command;
        if (!string.Equals(deviceId, command.DeviceId, StringComparison.Ordinal) ||
            !string.Equals(connectionId, handle.ConnectionId, StringComparison.Ordinal) ||
            !string.Equals(currentLeaseToken, command.LeaseToken, StringComparison.Ordinal) ||
            !string.Equals(acknowledgement.DeviceId, command.DeviceId, StringComparison.Ordinal) ||
            acknowledgement.StationId != command.StationId ||
            acknowledgement.FinishedAtUtc < acknowledgement.StartedAtUtc ||
            acknowledgement.StartedAtUtc < command.IssuedAtUtc.AddSeconds(-2) ||
            acknowledgement.FinishedAtUtc > command.ExpiresAtUtc ||
            !Enum.IsDefined(acknowledgement.Status))
        {
            return false;
        }

        return handle.Completion.TrySetResult(acknowledgement);
    }

    public async Task<AgentCommandAcknowledgement?> WaitAsync(
        Task<AgentCommandAcknowledgement> completion,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        try
        {
            return await completion.WaitAsync(timeout, cancellationToken);
        }
        catch (TimeoutException)
        {
            return null;
        }
    }

    public void Remove(Guid commandId) => pending.TryRemove(commandId, out _);

    private sealed class PendingCommand(AgentCommandEnvelope command, string connectionId)
    {
        public AgentCommandEnvelope Command { get; } = command;
        public string ConnectionId { get; } = connectionId;
        public TaskCompletionSource<AgentCommandAcknowledgement> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}

public sealed class AgentHealthProbeDispatcher(
    IAgentConnectionLeaseStore leases,
    AgentCommandResultCoordinator commandResults,
    IHubContext<AgentHub> hub,
    TimeProvider timeProvider) : IAgentHealthProbeDispatcher
{
    public async Task<AgentCommandAcknowledgement?> ProbeAsync(
        Guid stationId,
        string deviceId,
        CancellationToken cancellationToken = default)
    {
        if (stationId == Guid.Empty)
            throw new ArgumentException("Station id is required.", nameof(stationId));
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);

        var lease = await leases.GetFreshLeaseForCommandAsync(
            deviceId, TimeSpan.FromSeconds(20), cancellationToken);
        if (lease is null)
            return null;

        var issuedAt = timeProvider.GetUtcNow();
        if (lease.LeaseExpiresAtUtc <= issuedAt)
            return null;

        var command = new AgentCommandEnvelope(
            Guid.NewGuid(),
            deviceId,
            stationId,
            lease.LeaseToken,
            AgentCommandType.HealthProbe,
            issuedAt,
            issuedAt.AddSeconds(10));
        var completion = commandResults.Register(command, lease.ConnectionId);
        try
        {
            await hub.Clients.Client(lease.ConnectionId).SendAsync(
                "ReceiveCommand", command, cancellationToken);
            return await commandResults.WaitAsync(completion, TimeSpan.FromSeconds(8), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return null;
        }
        finally
        {
            commandResults.Remove(command.CommandId);
        }
    }
}
