namespace GameNet.Server.Modules.Billing.Domain;

public sealed class BillingLedgerEntry
{
    private BillingLedgerEntry() { }

    private BillingLedgerEntry(
        Guid id,
        Guid invoiceId,
        Guid paymentId,
        BillingLedgerEntryType entryType,
        long amountToman,
        Guid actorId,
        DateTimeOffset occurredAtUtc)
    {
        Id = id;
        InvoiceId = invoiceId;
        PaymentId = paymentId;
        EntryType = entryType;
        AmountToman = amountToman;
        ActorId = actorId;
        OccurredAtUtc = occurredAtUtc.ToUniversalTime();
    }

    public Guid Id { get; private set; }
    public Guid InvoiceId { get; private set; }
    public Guid PaymentId { get; private set; }
    public BillingLedgerEntryType EntryType { get; private set; }
    public long AmountToman { get; private set; }
    public Guid ActorId { get; private set; }
    public DateTimeOffset OccurredAtUtc { get; private set; }

    public static BillingLedgerEntry RecordCashSale(
        Guid id,
        Guid invoiceId,
        Guid paymentId,
        long amountToman,
        Guid actorId,
        DateTimeOffset occurredAtUtc)
    {
        if (id == Guid.Empty) throw new ArgumentException("Ledger entry id is required.", nameof(id));
        if (invoiceId == Guid.Empty) throw new ArgumentException("Invoice id is required.", nameof(invoiceId));
        if (paymentId == Guid.Empty) throw new ArgumentException("Payment id is required.", nameof(paymentId));
        if (actorId == Guid.Empty) throw new ArgumentException("Actor id is required.", nameof(actorId));
        if (amountToman <= 0) throw new ArgumentOutOfRangeException(nameof(amountToman));

        return new BillingLedgerEntry(id, invoiceId, paymentId, BillingLedgerEntryType.CashSale,
            amountToman, actorId, occurredAtUtc);
    }

    /// <summary>
    /// Signed contribution used by future cash-ledger projections. Stored ledger amounts remain positive magnitudes.
    /// </summary>
    public long GetSignedAmountToman() => EntryType switch
    {
        BillingLedgerEntryType.CashSale => AmountToman,
        BillingLedgerEntryType.CashSaleReversal => checked(-AmountToman),
        _ => throw new InvalidOperationException("Unsupported billing ledger entry type.")
    };

    public static BillingLedgerEntry RecordCashSaleReversal(
        Guid id,
        Guid invoiceId,
        Guid paymentId,
        long amountToman,
        Guid actorId,
        DateTimeOffset occurredAtUtc)
    {
        if (id == Guid.Empty) throw new ArgumentException("Ledger entry id is required.", nameof(id));
        if (invoiceId == Guid.Empty) throw new ArgumentException("Invoice id is required.", nameof(invoiceId));
        if (paymentId == Guid.Empty) throw new ArgumentException("Payment id is required.", nameof(paymentId));
        if (actorId == Guid.Empty) throw new ArgumentException("Actor id is required.", nameof(actorId));
        if (amountToman <= 0) throw new ArgumentOutOfRangeException(nameof(amountToman));

        return new BillingLedgerEntry(id, invoiceId, paymentId, BillingLedgerEntryType.CashSaleReversal,
            amountToman, actorId, occurredAtUtc);
    }
}
