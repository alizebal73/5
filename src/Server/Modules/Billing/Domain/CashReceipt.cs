namespace GameNet.Server.Modules.Billing.Domain;

public enum CashReceiptPurpose
{
    WalletTopUp = 1
}

/// <summary>Immutable Billing-owned record of physical cash received for a non-invoice operation.</summary>
public sealed class CashReceipt
{
    private CashReceipt() { }
    private CashReceipt(Guid id, Guid customerId, CashReceiptPurpose purpose, long amountAppliedToman,
        long amountReceivedToman, Guid receivedByOperatorId, DateTimeOffset receivedAtUtc,
        string idempotencyKey, string correlationId, string source)
    {
        Id = id; CustomerId = customerId; Purpose = purpose;
        AmountAppliedToman = amountAppliedToman; AmountReceivedToman = amountReceivedToman;
        ChangeGivenToman = checked(amountReceivedToman - amountAppliedToman);
        ReceivedByOperatorId = receivedByOperatorId; ReceivedAtUtc = receivedAtUtc.ToUniversalTime();
        IdempotencyKey = idempotencyKey; CorrelationId = correlationId; Source = source;
    }
    public Guid Id { get; private set; }
    public Guid CustomerId { get; private set; }
    public CashReceiptPurpose Purpose { get; private set; }
    public long AmountAppliedToman { get; private set; }
    public long AmountReceivedToman { get; private set; }
    public long ChangeGivenToman { get; private set; }
    public Guid ReceivedByOperatorId { get; private set; }
    public DateTimeOffset ReceivedAtUtc { get; private set; }
    public string IdempotencyKey { get; private set; } = null!;
    public string CorrelationId { get; private set; } = null!;
    public string Source { get; private set; } = null!;

    public static CashReceipt RecordWalletTopUp(Guid id, Guid customerId, long amountAppliedToman,
        long amountReceivedToman, Guid receivedByOperatorId, DateTimeOffset receivedAtUtc,
        string idempotencyKey, string correlationId, string source)
    {
        if (id == Guid.Empty) throw new ArgumentException("Cash receipt id is required.", nameof(id));
        if (customerId == Guid.Empty) throw new ArgumentException("Customer id is required.", nameof(customerId));
        if (receivedByOperatorId == Guid.Empty) throw new ArgumentException("Operator id is required.", nameof(receivedByOperatorId));
        if (amountAppliedToman <= 0) throw new ArgumentOutOfRangeException(nameof(amountAppliedToman));
        if (amountReceivedToman < amountAppliedToman)
            throw new ArgumentOutOfRangeException(nameof(amountReceivedToman), "Received cash cannot be less than the credited amount.");
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Trim().Length > 200)
            throw new ArgumentException("A valid idempotency key is required.", nameof(idempotencyKey));
        if (string.IsNullOrWhiteSpace(correlationId) || correlationId.Length > 128)
            throw new ArgumentException("A valid correlation id is required.", nameof(correlationId));
        if (string.IsNullOrWhiteSpace(source) || source.Length > 64)
            throw new ArgumentException("A valid source is required.", nameof(source));
        return new CashReceipt(id, customerId, CashReceiptPurpose.WalletTopUp, amountAppliedToman,
            amountReceivedToman, receivedByOperatorId, receivedAtUtc, idempotencyKey.Trim(), correlationId, source);
    }
}