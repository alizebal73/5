namespace GameNet.Server.Modules.Identity.Domain;

public sealed class AuthSession
{
    private AuthSession() { }
    public AuthSession(Guid id, Guid userId, string jti, DateTimeOffset createdAtUtc, DateTimeOffset expiresAtUtc)
    {
        if (id == Guid.Empty) throw new ArgumentException("Session id is required.", nameof(id));
        if (userId == Guid.Empty) throw new ArgumentException("User id is required.", nameof(userId));
        if (string.IsNullOrWhiteSpace(jti) || jti.Length > 128) throw new ArgumentException("Session token id is invalid.", nameof(jti));
        if (expiresAtUtc <= createdAtUtc) throw new ArgumentException("Session expiry must follow creation.", nameof(expiresAtUtc));
        Id = id; UserId = userId; Jti = jti; CreatedAtUtc = createdAtUtc; ExpiresAtUtc = expiresAtUtc;
    }
    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public string Jti { get; private set; } = null!;
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset ExpiresAtUtc { get; private set; }
    public DateTimeOffset? RevokedAtUtc { get; private set; }
    public bool IsUsable(DateTimeOffset utcNow) => RevokedAtUtc is null && ExpiresAtUtc > utcNow;
    public void Revoke(DateTimeOffset utcNow) => RevokedAtUtc ??= utcNow;
}
