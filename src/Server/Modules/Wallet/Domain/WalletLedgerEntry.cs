namespace GameNet.Server.Modules.Wallet.Domain;

public enum WalletLedgerEntryType
{
    CashTopUp = 1,
    SessionReserve = 2,
    SessionCharge = 3,
    SessionRelease = 4
}

/// <summary>Immutable trail of purchased-value credits and prepaid Session funding movements.</summary>
public sealed class WalletLedgerEntry
{
    private WalletLedgerEntry() { }

    private WalletLedgerEntry(Guid id, Guid customerId, Guid? cashReceiptId, Guid? sessionId,
        WalletLedgerEntryType entryType, long amountToman, long balanceAfterToman,
        long reservedBalanceAfterToman, Guid actorId, string actorType,
        DateTimeOffset occurredAtUtc, int walletVersionAfter)
    {
        Id = id;
        CustomerId = customerId;
        CashReceiptId = cashReceiptId;
        SessionId = sessionId;
        EntryType = entryType;
        AmountToman = amountToman;
        BalanceAfterToman = balanceAfterToman;
        ReservedBalanceAfterToman = reservedBalanceAfterToman;
        ActorId = actorId;
        ActorType = NormalizeActorType(actorType);
        OccurredAtUtc = occurredAtUtc.ToUniversalTime();
        WalletVersionAfter = walletVersionAfter;
    }

    public Guid Id { get; private set; }
    public Guid CustomerId { get; private set; }
    public Guid? CashReceiptId { get; private set; }
    public Guid? SessionId { get; private set; }
    public WalletLedgerEntryType EntryType { get; private set; }
    public long AmountToman { get; private set; }
    public long BalanceAfterToman { get; private set; }
    public long ReservedBalanceAfterToman { get; private set; }
    public Guid ActorId { get; private set; }
    public string ActorType { get; private set; } = "Operator";
    public DateTimeOffset OccurredAtUtc { get; private set; }
    public int WalletVersionAfter { get; private set; }

    public static WalletLedgerEntry RecordCashTopUp(Guid id, Guid customerId, Guid cashReceiptId,
        long amountToman, long balanceAfterToman, Guid actorId, DateTimeOffset occurredAtUtc,
        int walletVersionAfter, long reservedBalanceAfterToman = 0)
    {
        ValidateCommon(id, customerId, amountToman, balanceAfterToman, reservedBalanceAfterToman,
            actorId, "Operator", walletVersionAfter);
        if (cashReceiptId == Guid.Empty) throw new ArgumentException("Cash receipt id is required.", nameof(cashReceiptId));
        if (balanceAfterToman < amountToman) throw new ArgumentOutOfRangeException(nameof(balanceAfterToman));
        return new WalletLedgerEntry(id, customerId, cashReceiptId, null, WalletLedgerEntryType.CashTopUp,
            amountToman, balanceAfterToman, reservedBalanceAfterToman, actorId, "Operator", occurredAtUtc, walletVersionAfter);
    }

    public static WalletLedgerEntry RecordSessionReserve(Guid id, Guid customerId, Guid sessionId,
        long amountToman, long balanceAfterToman, long reservedBalanceAfterToman,
        Guid actorId, string actorType, DateTimeOffset occurredAtUtc, int walletVersionAfter)
    {
        ValidateSession(id, customerId, sessionId, amountToman, balanceAfterToman, reservedBalanceAfterToman,
            actorId, actorType, walletVersionAfter);
        return new WalletLedgerEntry(id, customerId, null, sessionId, WalletLedgerEntryType.SessionReserve,
            amountToman, balanceAfterToman, reservedBalanceAfterToman, actorId, actorType, occurredAtUtc, walletVersionAfter);
    }

    public static WalletLedgerEntry RecordSessionCharge(Guid id, Guid customerId, Guid sessionId,
        long amountToman, long balanceAfterToman, long reservedBalanceAfterToman,
        Guid actorId, string actorType, DateTimeOffset occurredAtUtc, int walletVersionAfter)
    {
        ValidateSession(id, customerId, sessionId, amountToman, balanceAfterToman, reservedBalanceAfterToman,
            actorId, actorType, walletVersionAfter);
        return new WalletLedgerEntry(id, customerId, null, sessionId, WalletLedgerEntryType.SessionCharge,
            amountToman, balanceAfterToman, reservedBalanceAfterToman, actorId, actorType, occurredAtUtc, walletVersionAfter);
    }

    public static WalletLedgerEntry RecordSessionRelease(Guid id, Guid customerId, Guid sessionId,
        long amountToman, long balanceAfterToman, long reservedBalanceAfterToman,
        Guid actorId, string actorType, DateTimeOffset occurredAtUtc, int walletVersionAfter)
    {
        ValidateSession(id, customerId, sessionId, amountToman, balanceAfterToman, reservedBalanceAfterToman,
            actorId, actorType, walletVersionAfter);
        return new WalletLedgerEntry(id, customerId, null, sessionId, WalletLedgerEntryType.SessionRelease,
            amountToman, balanceAfterToman, reservedBalanceAfterToman, actorId, actorType, occurredAtUtc, walletVersionAfter);
    }

    private static void ValidateSession(Guid id, Guid customerId, Guid sessionId, long amountToman,
        long balanceAfterToman, long reservedBalanceAfterToman, Guid actorId, string actorType, int walletVersionAfter)
    {
        if (sessionId == Guid.Empty) throw new ArgumentException("Session id is required.", nameof(sessionId));
        ValidateCommon(id, customerId, amountToman, balanceAfterToman, reservedBalanceAfterToman,
            actorId, actorType, walletVersionAfter);
    }

    private static void ValidateCommon(Guid id, Guid customerId, long amountToman,
        long balanceAfterToman, long reservedBalanceAfterToman, Guid actorId, string actorType, int walletVersionAfter)
    {
        if (id == Guid.Empty) throw new ArgumentException("Ledger entry id is required.", nameof(id));
        if (customerId == Guid.Empty) throw new ArgumentException("Customer id is required.", nameof(customerId));
        if (amountToman <= 0) throw new ArgumentOutOfRangeException(nameof(amountToman));
        if (balanceAfterToman < 0) throw new ArgumentOutOfRangeException(nameof(balanceAfterToman));
        if (reservedBalanceAfterToman < 0) throw new ArgumentOutOfRangeException(nameof(reservedBalanceAfterToman));
        if (actorType is not ("Operator" or "Customer" or "System"))
            throw new ArgumentException("Wallet ledger actor type is invalid.", nameof(actorType));
        if (actorId == Guid.Empty && actorType != "System")
            throw new ArgumentException("Wallet ledger actor id is required except for System records.", nameof(actorId));
        if (walletVersionAfter < 1) throw new ArgumentOutOfRangeException(nameof(walletVersionAfter));
    }

    private static string NormalizeActorType(string value) =>
        value is "Operator" or "Customer" or "System"
            ? value
            : throw new ArgumentException("Wallet ledger actor type is invalid.", nameof(value));
}
