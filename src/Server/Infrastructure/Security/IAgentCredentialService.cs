using GameNet.Shared.Contracts.V1.Security;

namespace GameNet.Server.Infrastructure.Security;

public interface IAgentCredentialService
{
    Task<AgentCredentialSecretResponse> ProvisionAsync(AgentCredentialProvisionRequest request, CancellationToken cancellationToken = default);
    Task<AgentCredentialRotationRequestedResponse> RequestRotationAsync(AgentCredentialRotateRequest request, CancellationToken cancellationToken = default);
    Task<AgentCredentialRotationStageResponse> StageRotationAsync(AgentCredentialRotationStageRequest request, CancellationToken cancellationToken = default);
    Task<AgentCredentialRotationConfirmationResponse> ConfirmRotationAsync(AgentCredentialRotationConfirmationRequest request, CancellationToken cancellationToken = default);
    Task<bool> RevokeAsync(AgentCredentialRevokeRequest request, CancellationToken cancellationToken = default);
    Task<bool> AuthenticateAsync(string deviceId, string secret, CancellationToken cancellationToken = default);
    Task<Guid?> AuthenticateAndGetCredentialIdAsync(string deviceId, string secret, CancellationToken cancellationToken = default);
    Task<bool> IsCredentialActiveAsync(Guid credentialId, string deviceId, CancellationToken cancellationToken = default);
    Task<Guid?> GetRequestedRotationOperationIdAsync(string deviceId, Guid credentialId, CancellationToken cancellationToken = default);
}
