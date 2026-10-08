namespace GameNet.Server.Persistence.Entities;

public sealed class AgentCredential
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string DeviceId { get; set; } = string.Empty;
    public string SecretHash { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset? RevokedAtUtc { get; private set; }
    public DateTimeOffset? LastAuthenticatedAtUtc { get; set; }

    public bool IsActive => RevokedAtUtc is null;

    public void Revoke(DateTimeOffset revokedAtUtc) => RevokedAtUtc = revokedAtUtc;
}
