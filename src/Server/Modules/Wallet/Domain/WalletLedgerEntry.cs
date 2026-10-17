namespace GameNet.Server.Modules.Wallet.Domain;

public enum WalletLedgerEntryType
{
    CashTopUp = 1
}

/// <summary>Immutable monetary movement. Amount is a positive magnitude; EntryType defines direction.</summary>
public sealed class WalletLedgerEntry
{
    private WalletLedgerEntry() { }
    private WalletLedgerEntry(Guid id, Guid customerId, Guid cashReceiptId,
        WalletLedgerEntryType entryType, long amountToman, Guid actorId, DateTimeOffset occurredAtUtc)
    {
        Id = id;
        CustomerId = customerId;
        CashReceiptId = cashReceiptId;
        EntryType = entryType;
        AmountToman = amountToman;
        ActorId = actorId;
        OccurredAtUtc = occurredAtUtc.ToUniversalTime();
    }

    public Guid Id { get; private set; }
    public Guid CustomerId { get; private set; }
    public Guid CashReceiptId { get; private set; }
    public WalletLedgerEntryType EntryType { get; private set; }
    public long AmountToman { get; private set; }
    public Guid ActorId { get; private set; }
    public DateTimeOffset OccurredAtUtc { get; private set; }

    public static WalletLedgerEntry RecordCashTopUp(Guid id, Guid customerId, Guid cashReceiptId,
        long amountToman, Guid actorId, DateTimeOffset occurredAtUtc)
    {
        if (id == Guid.Empty) throw new ArgumentException("Ledger entry id is required.", nameof(id));
        if (customerId == Guid.Empty) throw new ArgumentException("Customer id is required.", nameof(customerId));
        if (cashReceiptId == Guid.Empty) throw new ArgumentException("Cash receipt id is required.", nameof(cashReceiptId));
        if (actorId == Guid.Empty) throw new ArgumentException("Operator id is required.", nameof(actorId));
        if (amountToman <= 0) throw new ArgumentOutOfRangeException(nameof(amountToman));
        return new WalletLedgerEntry(id, customerId, cashReceiptId,
            WalletLedgerEntryType.CashTopUp, amountToman, actorId, occurredAtUtc);
    }

    public long GetSignedAmountToman() => EntryType switch
    {
        WalletLedgerEntryType.CashTopUp => AmountToman,
        _ => throw new InvalidOperationException("Unsupported Wallet ledger entry type.")
    };
}
