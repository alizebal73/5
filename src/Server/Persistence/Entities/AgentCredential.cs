namespace GameNet.Server.Persistence.Entities;

public sealed class AgentCredential
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string DeviceId { get; set; } = string.Empty;
    public string SecretHash { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset? RevokedAtUtc { get; private set; }
    public DateTimeOffset? LastAuthenticatedAtUtc { get; set; }

    public bool IsPendingRotation { get; set; }
    public Guid? RotationOperationId { get; set; }
    public Guid? ReplacesCredentialId { get; set; }
    public DateTimeOffset? RotationExpiresAtUtc { get; set; }
    public Guid? RotationRequestedOperationId { get; set; }
    public DateTimeOffset? RotationRequestedAtUtc { get; set; }

    public bool IsActive => RevokedAtUtc is null && !IsPendingRotation;
    public bool HasRotationRequest => RevokedAtUtc is null && !IsPendingRotation && RotationRequestedOperationId.HasValue;

    public void RequestRotation(Guid operationId, DateTimeOffset requestedAtUtc)
    {
        RotationRequestedOperationId = operationId;
        RotationRequestedAtUtc = requestedAtUtc;
    }

    public void ClearRotationRequest()
    {
        RotationRequestedOperationId = null;
        RotationRequestedAtUtc = null;
    }

    public void CompleteRotation()
    {
        IsPendingRotation = false;
        RotationExpiresAtUtc = null;
    }

    public void Revoke(DateTimeOffset revokedAtUtc) => RevokedAtUtc = revokedAtUtc;
}
