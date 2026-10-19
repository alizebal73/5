namespace GameNet.Server.Modules.Wallet.Domain;

/// <summary>Mutable balance projection. Immutable money movements live in WalletLedgerEntry.</summary>
public sealed class WalletAccount
{
    private WalletAccount() { }
    private WalletAccount(Guid customerId, DateTimeOffset createdAtUtc)
    {
        if (customerId == Guid.Empty) throw new ArgumentException("Customer id is required.", nameof(customerId));
        CustomerId = customerId; BalanceToman = 0; Version = 0; UpdatedAtUtc = createdAtUtc.ToUniversalTime();
    }
    public Guid CustomerId { get; private set; }
    public long BalanceToman { get; private set; }
    public int Version { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }
    public static WalletAccount Open(Guid customerId, DateTimeOffset nowUtc) => new(customerId, nowUtc);
    public long Credit(long amountToman, DateTimeOffset nowUtc)
    {
        if (amountToman <= 0) throw new ArgumentOutOfRangeException(nameof(amountToman));
        var newBalance = checked(BalanceToman + amountToman);
        var newVersion = checked(Version + 1);
        BalanceToman = newBalance; Version = newVersion; UpdatedAtUtc = nowUtc.ToUniversalTime();
        return newBalance;
    }
}