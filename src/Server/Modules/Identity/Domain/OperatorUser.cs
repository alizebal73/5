namespace GameNet.Server.Modules.Identity.Domain;

public sealed class OperatorUser
{
    private OperatorUser() { }
    private OperatorUser(Guid id, string username, string displayName, string passwordHash, DateTimeOffset createdAtUtc)
    {
        Id = id;
        Username = NormalizeUsername(username);
        DisplayName = Normalize(displayName, 120, nameof(displayName));
        PasswordHash = passwordHash;
        IsActive = true;
        CreatedAtUtc = createdAtUtc;
    }
    public Guid Id { get; private set; }
    public string Username { get; private set; } = null!;
    public string DisplayName { get; private set; } = null!;
    public string PasswordHash { get; private set; } = null!;
    public bool IsActive { get; private set; }
    public int FailedLoginCount { get; private set; }
    public DateTimeOffset? LockoutUntilUtc { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? LastLoginAtUtc { get; private set; }

    public static OperatorUser Create(Guid id, string username, string displayName, string passwordHash, DateTimeOffset createdAtUtc)
    {
        if (id == Guid.Empty) throw new ArgumentException("User id is required.", nameof(id));
        if (string.IsNullOrWhiteSpace(passwordHash)) throw new ArgumentException("Password hash is required.", nameof(passwordHash));
        if (passwordHash.Length > 512) throw new ArgumentException("Password hash is too long.", nameof(passwordHash));
        return new OperatorUser(id, username, displayName, passwordHash, createdAtUtc);
    }

    public bool IsLocked(DateTimeOffset utcNow) => LockoutUntilUtc is { } until && until > utcNow;
    public void RecordFailedLogin(DateTimeOffset utcNow)
    {
        FailedLoginCount++;
        if (FailedLoginCount >= 5) LockoutUntilUtc = utcNow.AddMinutes(10);
    }
    public void RecordSuccessfulLogin(DateTimeOffset utcNow)
    {
        FailedLoginCount = 0;
        LockoutUntilUtc = null;
        LastLoginAtUtc = utcNow;
    }
    public void ChangePassword(string passwordHash)
    {
        if (string.IsNullOrWhiteSpace(passwordHash)) throw new ArgumentException("Password hash is required.", nameof(passwordHash));
        if (passwordHash.Length > 512) throw new ArgumentException("Password hash is too long.", nameof(passwordHash));
        PasswordHash = passwordHash;
        FailedLoginCount = 0;
        LockoutUntilUtc = null;
    }

    public void Disable() => IsActive = false;
    public void SetActive(bool isActive) => IsActive = isActive;

    private static string NormalizeUsername(string value)
    {
        var normalized = Normalize(value, 64, nameof(value)).ToLowerInvariant();
        if (normalized.Any(char.IsWhiteSpace)) throw new ArgumentException("Username cannot contain whitespace.", nameof(value));
        return normalized;
    }
    private static string Normalize(string value, int maxLength, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Value is required.", parameterName);
        var normalized = value.Trim();
        if (normalized.Length > maxLength) throw new ArgumentException("Value is too long.", parameterName);
        return normalized;
    }
}
