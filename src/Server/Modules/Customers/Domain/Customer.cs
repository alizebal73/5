namespace GameNet.Server.Modules.Customers.Domain;

public sealed class Customer
{
    private Customer() { }

    private Customer(
        Guid id,
        string code,
        string displayName,
        string? phone,
        string? pinHash,
        DateTimeOffset createdAtUtc)
    {
        Id = id;
        Code = NormalizeCode(code);
        DisplayName = Normalize(displayName, 120, nameof(displayName));
        Phone = NormalizeOptional(phone, 32);
        PinHash = pinHash;
        IsActive = true;
        CreatedAtUtc = createdAtUtc;
        Version = 1;
    }

    public Guid Id { get; private set; }
    public string Code { get; private set; } = null!;
    public string DisplayName { get; private set; } = null!;
    public string? Phone { get; private set; }
    public string? PinHash { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? UpdatedAtUtc { get; private set; }
    public int Version { get; private set; }

    public static Customer Create(
        Guid id,
        string code,
        string displayName,
        string? phone,
        string? pinHash,
        DateTimeOffset createdAtUtc)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("Customer id is required.", nameof(id));

        return new Customer(
            id,
            code,
            displayName,
            phone,
            pinHash,
            createdAtUtc);
    }

    public void UpdateProfile(
        string displayName,
        string? phone,
        DateTimeOffset updatedAtUtc)
    {
        DisplayName = Normalize(displayName, 120, nameof(displayName));
        Phone = NormalizeOptional(phone, 32);
        UpdatedAtUtc = updatedAtUtc;
        Version++;
    }

    public void ChangePin(string? pinHash, DateTimeOffset updatedAtUtc)
    {
        PinHash = pinHash;
        UpdatedAtUtc = updatedAtUtc;
        Version++;
    }

    public void Disable(DateTimeOffset updatedAtUtc)
    {
        IsActive = false;
        UpdatedAtUtc = updatedAtUtc;
        Version++;
    }

    private static string NormalizeCode(string value)
    {
        var normalized = Normalize(value, 64, nameof(value)).ToUpperInvariant();
        if (normalized.Any(char.IsWhiteSpace))
            throw new ArgumentException("Customer code cannot contain whitespace.", nameof(value));
        return normalized;
    }

    private static string Normalize(string value, int maxLength, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Value is required.", parameterName);

        var normalized = value.Trim();
        if (normalized.Length > maxLength)
            throw new ArgumentException("Value is too long.", parameterName);

        return normalized;
    }

    private static string? NormalizeOptional(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var normalized = value.Trim();
        if (normalized.Length > maxLength)
            throw new ArgumentException("Value is too long.", nameof(value));

        return normalized;
    }
}
