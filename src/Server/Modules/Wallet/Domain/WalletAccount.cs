namespace GameNet.Server.Modules.Wallet.Domain;

/// <summary>
/// Mutable balance projection only. Immutable WalletLedgerEntry rows remain the accounting source of truth.
/// </summary>
public sealed class WalletAccount
{
    private WalletAccount() { }
    private WalletAccount(Guid customerId, DateTimeOffset updatedAtUtc)
    {
        CustomerId = customerId;
        UpdatedAtUtc = updatedAtUtc.ToUniversalTime();
    }

    public Guid CustomerId { get; private set; }
    public long PurchasedBalanceToman { get; private set; }
    public int Version { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public static WalletAccount Open(Guid customerId, DateTimeOffset nowUtc)
    {
        if (customerId == Guid.Empty) throw new ArgumentException("Customer id is required.", nameof(customerId));
        return new WalletAccount(customerId, nowUtc);
    }

    public void CreditPurchasedValue(long amountToman, DateTimeOffset nowUtc)
    {
        if (amountToman <= 0) throw new ArgumentOutOfRangeException(nameof(amountToman));
        PurchasedBalanceToman = checked(PurchasedBalanceToman + amountToman);
        Version = checked(Version + 1);
        UpdatedAtUtc = nowUtc.ToUniversalTime();
    }
}
