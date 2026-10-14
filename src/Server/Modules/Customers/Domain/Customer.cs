namespace GameNet.Server.Modules.Customers.Domain;

public sealed class Customer
{
    private Customer() { }

    private Customer(Guid id, string code, string? displayName, CustomerKind kind, DateTimeOffset createdAtUtc)
    {
        Id = id;
        Code = NormalizeCode(code);
        DisplayName = NormalizeDisplayName(displayName, kind);
        Kind = kind;
        IsActive = true;
        CreatedAtUtc = createdAtUtc.ToUniversalTime();
        Version = 1;
    }

    public Guid Id { get; private set; }
    public string Code { get; private set; } = null!;
    public string? DisplayName { get; private set; }
    public CustomerKind Kind { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public int Version { get; private set; }

    public static Customer CreateWalkIn(Guid id, DateTimeOffset createdAtUtc)
    {
        if (id == Guid.Empty) throw new ArgumentException("Customer id is required.", nameof(id));
        var code = $"W-{id:N}".ToUpperInvariant();
        return new Customer(id, code, null, CustomerKind.WalkIn, createdAtUtc);
    }

    public static Customer CreateRegistered(Guid id, string code, string displayName, DateTimeOffset createdAtUtc)
    {
        if (id == Guid.Empty) throw new ArgumentException("Customer id is required.", nameof(id));
        return new Customer(id, code, displayName, CustomerKind.Registered, createdAtUtc);
    }

    public void Rename(string displayName)
    {
        if (Kind != CustomerKind.Registered)
            throw new InvalidOperationException("CUSTOMER_WALKIN_PROFILE_IS_NOT_EDITABLE");

        var normalized = NormalizeDisplayName(displayName, Kind);
        if (DisplayName == normalized) return;
        DisplayName = normalized;
        Version = checked(Version + 1);
    }

    public void Deactivate()
    {
        if (!IsActive) return;
        IsActive = false;
        Version = checked(Version + 1);
    }

    private static string NormalizeCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code)) throw new ArgumentException("Customer code is required.", nameof(code));
        var value = code.Trim().ToUpperInvariant();
        if (value.Length is < 2 or > 40 ||
            value.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_')) ||
            !char.IsAsciiLetterOrDigit(value[0]) || !char.IsAsciiLetterOrDigit(value[^1]))
            throw new ArgumentException("Customer code must be 2-40 ASCII letters, digits, dashes or underscores.", nameof(code));
        return value;
    }

    private static string? NormalizeDisplayName(string? displayName, CustomerKind kind)
    {
        if (kind == CustomerKind.WalkIn && string.IsNullOrWhiteSpace(displayName))
            return null;

        if (string.IsNullOrWhiteSpace(displayName))
            throw new ArgumentException("Registered customer name is required.", nameof(displayName));

        var value = displayName.Trim();
        if (value.Length > 120 || value.Any(char.IsControl))
            throw new ArgumentException("Customer name must be at most 120 characters.", nameof(displayName));
        return value;
    }
}
