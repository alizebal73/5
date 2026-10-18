namespace GameNet.Shared.Contracts.V1.Protocol;

public enum AgentCommandType
{
    HealthProbe = 1
}

public enum AgentCommandStatus
{
    Succeeded = 1,
    Rejected = 2,
    Failed = 3
}

/// <summary>
/// A short-lived Server-issued command tied to one station, one DeviceId and one current lease.
/// Commands are allow-listed; this protocol never accepts an arbitrary shell command or executable path.
/// </summary>
public sealed record AgentCommandEnvelope(
    Guid CommandId,
    string DeviceId,
    Guid StationId,
    string LeaseToken,
    AgentCommandType Type,
    DateTimeOffset IssuedAtUtc,
    DateTimeOffset ExpiresAtUtc);

public sealed record AgentCommandAcknowledgement(
    Guid CommandId,
    string DeviceId,
    Guid StationId,
    AgentCommandStatus Status,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset FinishedAtUtc,
    string? ErrorCode,
    string? AgentVersion,
    string? StationState);
