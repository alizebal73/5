namespace GameNet.Agent.Identity;

public interface IAgentCredentialStore
{
    Task<string> GetOrBootstrapAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(string secret, CancellationToken cancellationToken = default);
}
