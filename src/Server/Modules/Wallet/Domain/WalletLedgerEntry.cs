namespace GameNet.Server.Modules.Wallet.Domain;

public enum WalletLedgerEntryType { CashTopUp = 1 }

/// <summary>Immutable audit trail of a purchased-value credit.</summary>
public sealed class WalletLedgerEntry
{
    private WalletLedgerEntry() { }
    private WalletLedgerEntry(Guid id, Guid customerId, Guid cashReceiptId, WalletLedgerEntryType entryType,
        long amountToman, long balanceAfterToman, Guid actorId, DateTimeOffset occurredAtUtc, int walletVersionAfter)
    {
        Id=id; CustomerId=customerId; CashReceiptId=cashReceiptId; EntryType=entryType;
        AmountToman=amountToman; BalanceAfterToman=balanceAfterToman; ActorId=actorId;
        OccurredAtUtc=occurredAtUtc.ToUniversalTime(); WalletVersionAfter=walletVersionAfter;
    }
    public Guid Id { get; private set; }
    public Guid CustomerId { get; private set; }
    public Guid CashReceiptId { get; private set; }
    public WalletLedgerEntryType EntryType { get; private set; }
    public long AmountToman { get; private set; }
    public long BalanceAfterToman { get; private set; }
    public Guid ActorId { get; private set; }
    public DateTimeOffset OccurredAtUtc { get; private set; }
    public int WalletVersionAfter { get; private set; }
    public static WalletLedgerEntry RecordCashTopUp(Guid id, Guid customerId, Guid cashReceiptId,
        long amountToman, long balanceAfterToman, Guid actorId, DateTimeOffset occurredAtUtc, int walletVersionAfter)
    {
        if (id == Guid.Empty) throw new ArgumentException("Ledger entry id is required.", nameof(id));
        if (customerId == Guid.Empty) throw new ArgumentException("Customer id is required.", nameof(customerId));
        if (cashReceiptId == Guid.Empty) throw new ArgumentException("Cash receipt id is required.", nameof(cashReceiptId));
        if (actorId == Guid.Empty) throw new ArgumentException("Actor id is required.", nameof(actorId));
        if (amountToman <= 0) throw new ArgumentOutOfRangeException(nameof(amountToman));
        if (balanceAfterToman < amountToman) throw new ArgumentOutOfRangeException(nameof(balanceAfterToman));
        if (walletVersionAfter < 1) throw new ArgumentOutOfRangeException(nameof(walletVersionAfter));
        return new WalletLedgerEntry(id, customerId, cashReceiptId, WalletLedgerEntryType.CashTopUp,
            amountToman, balanceAfterToman, actorId, occurredAtUtc, walletVersionAfter);
    }
}