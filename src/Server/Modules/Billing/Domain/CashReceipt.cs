namespace GameNet.Server.Modules.Billing.Domain;

public enum CashReceiptPurpose
{
    WalletTopUp = 1
}

/// <summary>Billing-owned actual cash facts for non-invoice sales such as Wallet top-ups.</summary>
public sealed class CashReceipt
{
    private CashReceipt() { }
    private CashReceipt(Guid id, Guid customerId, CashReceiptPurpose purpose, Guid referenceId,
        long amountAppliedToman, long amountReceivedToman, long changeGivenToman, PaymentMethod method,
        Guid receivedByOperatorId, DateTimeOffset receivedAtUtc)
    {
        Id = id;
        CustomerId = customerId;
        Purpose = purpose;
        ReferenceId = referenceId;
        AmountAppliedToman = amountAppliedToman;
        AmountReceivedToman = amountReceivedToman;
        ChangeGivenToman = changeGivenToman;
        Method = method;
        ReceivedByOperatorId = receivedByOperatorId;
        ReceivedAtUtc = receivedAtUtc.ToUniversalTime();
    }

    public Guid Id { get; private set; }
    public Guid CustomerId { get; private set; }
    public CashReceiptPurpose Purpose { get; private set; }
    public Guid ReferenceId { get; private set; }
    public long AmountAppliedToman { get; private set; }
    public long AmountReceivedToman { get; private set; }
    public long ChangeGivenToman { get; private set; }
    public PaymentMethod Method { get; private set; }
    public Guid ReceivedByOperatorId { get; private set; }
    public DateTimeOffset ReceivedAtUtc { get; private set; }

    public static CashReceipt CreateForWalletTopUp(Guid id, Guid customerId, Guid walletLedgerEntryId,
        long amountToCreditToman, long amountReceivedToman, Guid receivedByOperatorId, DateTimeOffset receivedAtUtc)
    {
        if (id == Guid.Empty) throw new ArgumentException("Cash receipt id is required.", nameof(id));
        if (customerId == Guid.Empty) throw new ArgumentException("Customer id is required.", nameof(customerId));
        if (walletLedgerEntryId == Guid.Empty) throw new ArgumentException("Wallet ledger reference is required.", nameof(walletLedgerEntryId));
        if (receivedByOperatorId == Guid.Empty) throw new ArgumentException("Operator id is required.", nameof(receivedByOperatorId));
        if (amountToCreditToman <= 0) throw new ArgumentOutOfRangeException(nameof(amountToCreditToman));
        if (amountReceivedToman < amountToCreditToman)
            throw new ArgumentOutOfRangeException(nameof(amountReceivedToman), "Received cash cannot be less than the credited amount.");
        var change = checked(amountReceivedToman - amountToCreditToman);
        return new CashReceipt(id, customerId, CashReceiptPurpose.WalletTopUp, walletLedgerEntryId,
            amountToCreditToman, amountReceivedToman, change, PaymentMethod.Cash, receivedByOperatorId, receivedAtUtc);
    }
}
