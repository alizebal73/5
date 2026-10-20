namespace GameNet.Server.Modules.Customers.Domain;

public sealed class CustomerLoginSession
{
    private CustomerLoginSession() { }
    private CustomerLoginSession(string jti, Guid customerId, DateTimeOffset createdAtUtc, DateTimeOffset expiresAtUtc)
    {
        if (string.IsNullOrWhiteSpace(jti) || jti.Length != 32 || jti.Any(ch => !char.IsAsciiHexDigit(ch)))
            throw new ArgumentException("Customer login token id must be 32 hexadecimal characters.", nameof(jti));
        if (customerId == Guid.Empty) throw new ArgumentException("Customer id is required.", nameof(customerId));
        if (expiresAtUtc <= createdAtUtc) throw new ArgumentOutOfRangeException(nameof(expiresAtUtc));
        Jti = jti.ToLowerInvariant();
        CustomerId = customerId;
        CreatedAtUtc = createdAtUtc.ToUniversalTime();
        ExpiresAtUtc = expiresAtUtc.ToUniversalTime();
    }
    public string Jti { get; private set; } = null!;
    public Guid CustomerId { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset ExpiresAtUtc { get; private set; }
    public DateTimeOffset? RevokedAtUtc { get; private set; }
    public static CustomerLoginSession Create(string jti, Guid customerId, DateTimeOffset createdAtUtc, DateTimeOffset expiresAtUtc) =>
        new(jti, customerId, createdAtUtc, expiresAtUtc);
    public bool IsUsable(DateTimeOffset nowUtc) => RevokedAtUtc is null && ExpiresAtUtc > nowUtc.ToUniversalTime();
    public void Revoke(DateTimeOffset nowUtc) => RevokedAtUtc ??= nowUtc.ToUniversalTime();
}
