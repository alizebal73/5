using GameNet.Shared.Contracts.V1.Security;

namespace GameNet.Server.Infrastructure.Security;

public interface IAgentCredentialService
{
    Task<AgentCredentialSecretResponse> ProvisionAsync(AgentCredentialProvisionRequest request, CancellationToken cancellationToken = default);
    Task<AgentCredentialSecretResponse> RotateAsync(AgentCredentialRotateRequest request, CancellationToken cancellationToken = default);
    Task<bool> RevokeAsync(AgentCredentialRevokeRequest request, CancellationToken cancellationToken = default);
    Task<bool> AuthenticateAsync(string deviceId, string secret, CancellationToken cancellationToken = default);
}
