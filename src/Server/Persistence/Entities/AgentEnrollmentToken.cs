namespace GameNet.Server.Persistence.Entities;

public sealed class AgentEnrollmentToken
{
    private AgentEnrollmentToken() { }

    public Guid Id { get; private set; }
    public string DeviceId { get; private set; } = string.Empty;
    public string TokenHash { get; private set; } = string.Empty;
    public Guid IssuedByOperatorId { get; private set; }
    public DateTimeOffset IssuedAtUtc { get; private set; }
    public DateTimeOffset ExpiresAtUtc { get; private set; }
    public DateTimeOffset? RedeemedAtUtc { get; private set; }
    public DateTimeOffset? RevokedAtUtc { get; private set; }

    public bool CanRedeem(DateTimeOffset now) =>
        RedeemedAtUtc is null && RevokedAtUtc is null && ExpiresAtUtc > now;

    public static AgentEnrollmentToken Create(
        Guid id,
        string deviceId,
        string tokenHash,
        Guid issuedByOperatorId,
        DateTimeOffset issuedAtUtc,
        DateTimeOffset expiresAtUtc)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("Token id is required.", nameof(id));
        if (string.IsNullOrWhiteSpace(deviceId) ||
            deviceId.Length > 128 ||
            deviceId != deviceId.Trim() ||
            deviceId.Any(character =>
                !(char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.')))
        {
            throw new ArgumentException(
                "DeviceId must contain 1-128 ASCII letters, digits, dashes, underscores or dots.",
                nameof(deviceId));
        }
        if (string.IsNullOrWhiteSpace(tokenHash) || tokenHash.Length != 64 || tokenHash.Any(ch => !Uri.IsHexDigit(ch)))
            throw new ArgumentException("Token hash must be a SHA-256 hexadecimal digest.", nameof(tokenHash));
        if (issuedByOperatorId == Guid.Empty)
            throw new ArgumentException("Issuing operator id is required.", nameof(issuedByOperatorId));
        if (expiresAtUtc <= issuedAtUtc)
            throw new ArgumentException("Enrollment token expiry must be later than issue time.", nameof(expiresAtUtc));

        return new AgentEnrollmentToken
        {
            Id = id,
            DeviceId = deviceId,
            TokenHash = tokenHash.ToUpperInvariant(),
            IssuedByOperatorId = issuedByOperatorId,
            IssuedAtUtc = issuedAtUtc,
            ExpiresAtUtc = expiresAtUtc
        };
    }

    public void MarkRedeemed(DateTimeOffset now)
    {
        if (!CanRedeem(now))
            throw new InvalidOperationException("AGENT_ENROLLMENT_TOKEN_NOT_REDEEMABLE");
        RedeemedAtUtc = now;
    }

    public bool Revoke(DateTimeOffset now)
    {
        if (RedeemedAtUtc is not null || RevokedAtUtc is not null)
            return false;
        RevokedAtUtc = now;
        return true;
    }
}
