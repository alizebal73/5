using GameNet.Shared.Contracts.V1.Protocol;

namespace GameNet.Agent.Transport;

public interface IAgentTransport : IAsyncDisposable
{
    bool IsConnected { get; }

    event Func<AgentCommandEnvelope, CancellationToken, Task<AgentCommandAcknowledgement>>? CommandReceived;

    Task ConnectAsync(GameNet.Agent.AgentIdentity identity, CancellationToken cancellationToken = default);

    Task<bool> HeartbeatAsync(AgentHeartbeat heartbeat, CancellationToken cancellationToken = default);

    Task<AgentReconciliationResponse> ReconcileAsync(string deviceId, string reason, CancellationToken cancellationToken = default);

    Task<bool> AcknowledgeCommandAsync(
        AgentCommandAcknowledgement acknowledgement,
        CancellationToken cancellationToken = default);
}
