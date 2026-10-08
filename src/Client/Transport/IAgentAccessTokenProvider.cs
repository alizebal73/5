namespace GameNet.Agent.Transport;

public interface IAgentAccessTokenProvider
{
    Task<string> GetAccessTokenAsync(string deviceId, CancellationToken cancellationToken = default);
}
