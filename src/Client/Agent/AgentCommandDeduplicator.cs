using GameNet.Shared.Contracts.V1.Protocol;

namespace GameNet.Agent;

/// <summary>
/// Deduplicates delivery by CommandId for this Agent process. Entries are retained through their
/// command expiry. When capacity is exhausted the Agent fails closed instead of evicting a live ID.
/// </summary>
public sealed class AgentCommandDeduplicator(TimeProvider timeProvider, int capacity = 1024)
{
    private readonly object gate = new();
    private readonly Dictionary<Guid, Entry> entries = new();

    public Task<AgentCommandAcknowledgement> ExecuteOnceAsync(
        AgentCommandEnvelope command,
        Func<AgentCommandEnvelope, CancellationToken, Task<AgentCommandAcknowledgement>> execute,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(execute);
        if (capacity is < 1 or > 100_000)
            throw new ArgumentOutOfRangeException(nameof(capacity));

        Entry entry;
        lock (gate)
        {
            var now = timeProvider.GetUtcNow();
            foreach (var expiredId in entries
                .Where(pair => pair.Value.Command.ExpiresAtUtc <= now)
                .Select(pair => pair.Key)
                .ToArray())
            {
                entries.Remove(expiredId);
            }

            if (entries.TryGetValue(command.CommandId, out var existing))
            {
                if (existing.Command != command)
                {
                    return Task.FromResult(new AgentCommandAcknowledgement(
                        command.CommandId,
                        command.DeviceId,
                        command.StationId,
                        AgentCommandStatus.Rejected,
                        now,
                        now,
                        "agent.command.id_reused",
                        null,
                        null));
                }

                return existing.Completion.Task;
            }

            if (entries.Count >= capacity)
            {
                return Task.FromResult(new AgentCommandAcknowledgement(
                    command.CommandId,
                    command.DeviceId,
                    command.StationId,
                    AgentCommandStatus.Rejected,
                    now,
                    now,
                    "agent.command.dedup_capacity",
                    null,
                    null));
            }

            entry = new Entry(command);
            entries.Add(command.CommandId, entry);
        }

        _ = CompleteAsync(entry, execute, cancellationToken);
        return entry.Completion.Task;
    }

    private async Task CompleteAsync(
        Entry entry,
        Func<AgentCommandEnvelope, CancellationToken, Task<AgentCommandAcknowledgement>> execute,
        CancellationToken cancellationToken)
    {
        try
        {
            var acknowledgement = await execute(entry.Command, cancellationToken);
            if (acknowledgement.CommandId != entry.Command.CommandId ||
                !string.Equals(acknowledgement.DeviceId, entry.Command.DeviceId, StringComparison.Ordinal) ||
                acknowledgement.StationId != entry.Command.StationId ||
                acknowledgement.FinishedAtUtc < acknowledgement.StartedAtUtc)
            {
                var now = timeProvider.GetUtcNow();
                acknowledgement = new AgentCommandAcknowledgement(
                    entry.Command.CommandId,
                    entry.Command.DeviceId,
                    entry.Command.StationId,
                    AgentCommandStatus.Failed,
                    now,
                    now,
                    "agent.command.invalid_acknowledgement",
                    null,
                    null);
            }

            entry.Completion.TrySetResult(acknowledgement);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            entry.Completion.TrySetCanceled(cancellationToken);
        }
        catch
        {
            var now = timeProvider.GetUtcNow();
            entry.Completion.TrySetResult(new AgentCommandAcknowledgement(
                entry.Command.CommandId,
                entry.Command.DeviceId,
                entry.Command.StationId,
                AgentCommandStatus.Failed,
                now,
                now,
                "agent.command.execution_failed",
                null,
                null));
        }
    }

    private sealed class Entry(AgentCommandEnvelope command)
    {
        public AgentCommandEnvelope Command { get; } = command;
        public TaskCompletionSource<AgentCommandAcknowledgement> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
