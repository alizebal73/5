namespace GameNet.Agent.Transport;

public sealed class AgentTransportOptions
{
    public const string SectionName = "GameNet:AgentTransport";

    public string ServerBaseUrl { get; init; } = "http://127.0.0.1:5080";
    public string BootstrapCredentialEnvironmentVariableName { get; init; } = "GAMENET_AGENT_BOOTSTRAP_SECRET";
    public int HeartbeatIntervalSeconds { get; init; } = 5;
    public int InitialRetrySeconds { get; init; } = 5;
}
