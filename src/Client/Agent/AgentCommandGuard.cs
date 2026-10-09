using GameNet.Shared.Contracts.V1.Protocol;

namespace GameNet.Agent;

/// <summary>
/// Fail-closed checks performed at the final Agent boundary before a command handler may run.
/// </summary>
public static class AgentCommandGuard
{
    public static string? Validate(
        AgentCommandEnvelope command,
        string? currentDeviceId,
        string? currentLeaseToken,
        DateTimeOffset? currentLeaseExpiresAtUtc,
        DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.CommandId == Guid.Empty || command.StationId == Guid.Empty)
            return "agent.command.invalid_identity";

        if (string.IsNullOrWhiteSpace(command.DeviceId) ||
            !string.Equals(command.DeviceId, currentDeviceId, StringComparison.Ordinal))
            return "agent.command.device_mismatch";

        if (string.IsNullOrWhiteSpace(currentLeaseToken) ||
            string.IsNullOrWhiteSpace(command.LeaseToken) ||
            !string.Equals(command.LeaseToken, currentLeaseToken, StringComparison.Ordinal))
            return "agent.command.lease_mismatch";

        if (!currentLeaseExpiresAtUtc.HasValue || currentLeaseExpiresAtUtc.Value <= nowUtc)
            return "agent.command.lease_expired";

        if (command.IssuedAtUtc > nowUtc.AddSeconds(2) ||
            command.ExpiresAtUtc <= nowUtc ||
            command.ExpiresAtUtc <= command.IssuedAtUtc ||
            command.ExpiresAtUtc - command.IssuedAtUtc > TimeSpan.FromSeconds(30) ||
            command.IssuedAtUtc < nowUtc.AddSeconds(-30))
            return "agent.command.stale";

        if (!Enum.IsDefined(command.Type) || command.Type != AgentCommandType.HealthProbe)
            return "agent.command.unsupported";

        return null;
    }
}
