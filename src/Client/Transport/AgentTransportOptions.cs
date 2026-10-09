namespace GameNet.Agent.Transport;

public sealed class AgentTransportOptions
{
    public const string SectionName = "GameNet:AgentTransport";

    public string ServerBaseUrl { get; init; } = "http://127.0.0.1:5080";
    public string BootstrapCredentialEnvironmentVariableName { get; init; } = "GAMENET_AGENT_BOOTSTRAP_SECRET";
    public int HeartbeatIntervalSeconds { get; init; } = 5;
    public int InitialRetrySeconds { get; init; } = 5;

    public void Validate()
    {
        if (!Uri.TryCreate(ServerBaseUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment))
        {
            throw new InvalidOperationException(
                "Agent ServerBaseUrl must be an absolute HTTP(S) URL without embedded credentials, query or fragment.");
        }

        if (HeartbeatIntervalSeconds is < 1 or > 60)
            throw new InvalidOperationException("Agent HeartbeatIntervalSeconds must be between 1 and 60.");

        if (InitialRetrySeconds is < 1 or > 120)
            throw new InvalidOperationException("Agent InitialRetrySeconds must be between 1 and 120.");
    }
}
