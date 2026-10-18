namespace GameNet.Shared.Contracts.V1.Protocol;

public enum AgentReconciliationScope
{
    LeaseAndIdentity = 1
}

public sealed record AgentReconciliationRequest(
    string DeviceId,
    DateTimeOffset RequestedAtUtc,
    string Reason);

public sealed record AgentReconciliationResponse(
    string DeviceId,
    DateTimeOffset ServerTimeUtc,
    int AgentProtocolVersion,
    bool AuthoritativeConnection,
    AgentReconciliationScope Scope);

public sealed record CommandExpiry(DateTimeOffset ExpiresAtUtc);
