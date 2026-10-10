using GameNet.Shared.Primitives;

namespace GameNet.Agent.Transport;

public sealed class AgentTransportOptions
{
    public const string SectionName = "GameNet:AgentTransport";

    public string ServerBaseUrl { get; init; } = string.Empty;
    public string BootstrapCredentialEnvironmentVariableName { get; init; } = "GAMENET_AGENT_BOOTSTRAP_SECRET";
    public int HeartbeatIntervalSeconds { get; init; } = 5;
    public int InitialRetrySeconds { get; init; } = 5;

    public void Validate()
    {
        if (!ServerEndpointAddress.IsAllowed(ServerBaseUrl))
        {
            throw new InvalidOperationException(
                "Agent ServerBaseUrl must be a root HTTPS origin, except for loopback development, without embedded credentials, query or fragment.");
        }

        if (HeartbeatIntervalSeconds is < 1 or > 60)
            throw new InvalidOperationException("Agent HeartbeatIntervalSeconds must be between 1 and 60.");

        if (InitialRetrySeconds is < 1 or > 120)
            throw new InvalidOperationException("Agent InitialRetrySeconds must be between 1 and 120.");
    }
}
