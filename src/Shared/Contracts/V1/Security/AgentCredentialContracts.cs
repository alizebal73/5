namespace GameNet.Shared.Contracts.V1.Security;

public sealed record AgentTokenRequest(string DeviceId, string Secret);
public sealed record AgentTokenResponse(string AccessToken, DateTimeOffset ExpiresAtUtc);
public sealed record AgentCredentialProvisionRequest(string DeviceId);
public sealed record AgentCredentialRotateRequest(string DeviceId, Guid OperationId);
public sealed record AgentCredentialRotationRequestedResponse(Guid OperationId, string DeviceId, string State, DateTimeOffset? ExpiresAtUtc);
public sealed record AgentCredentialRotationStageRequest(Guid OperationId, string DeviceId, string CurrentSecret, string ReplacementSecret);
public sealed record AgentCredentialRotationStageResponse(Guid OperationId, string DeviceId, string State, DateTimeOffset? ExpiresAtUtc);
public sealed record AgentCredentialRotationConfirmationRequest(Guid OperationId, string DeviceId, string CurrentSecret, string ReplacementSecret);
public sealed record AgentCredentialRotationConfirmationResponse(Guid OperationId, string DeviceId, string State, Guid CredentialId);
public sealed record AgentCredentialRevokeRequest(string DeviceId, string Reason);
public sealed record AgentCredentialSecretResponse(Guid CredentialId, string DeviceId, string Secret, DateTimeOffset CreatedAtUtc);
public sealed record AgentEnrollmentIssueRequest(string DeviceId);
public sealed record AgentEnrollmentRecoverRequest(string DeviceId, string Reason);
public sealed record AgentEnrollmentIssueResponse(Guid TokenId, string DeviceId, string Token, DateTimeOffset ExpiresAtUtc);
public sealed record AgentEnrollmentRedeemRequest(string DeviceId, string Token);
public sealed record AgentEnrollmentRevokeRequest(string Reason);
