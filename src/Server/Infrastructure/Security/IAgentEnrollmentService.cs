using GameNet.Shared.Contracts.V1.Security;

namespace GameNet.Server.Infrastructure.Security;

public sealed record AgentEnrollmentIssueCommand(
    string DeviceId,
    Guid IssuedByOperatorId,
    string CorrelationId,
    string Source);

public interface IAgentEnrollmentService
{
    Task<AgentEnrollmentIssueResponse> IssueAsync(
        AgentEnrollmentIssueCommand command,
        CancellationToken cancellationToken = default);

    Task<AgentCredentialSecretResponse?> RedeemAsync(
        AgentEnrollmentRedeemRequest request,
        string correlationId,
        CancellationToken cancellationToken = default);

    Task<bool> RevokeAsync(
        Guid tokenId,
        string reason,
        Guid actorId,
        string correlationId,
        string source,
        CancellationToken cancellationToken = default);
}
