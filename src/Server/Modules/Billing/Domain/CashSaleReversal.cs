namespace GameNet.Server.Modules.Billing.Domain;

public sealed class CashSaleReversal
{
    private CashSaleReversal() { }

    private CashSaleReversal(
        Guid id, Guid invoiceId, Guid paymentId, long amountToman,
        Guid reversedByOperatorId, string reason, string correlationId,
        string idempotencyKey, DateTimeOffset reversedAtUtc)
    {
        Id = id;
        InvoiceId = invoiceId;
        PaymentId = paymentId;
        AmountToman = amountToman;
        ReversedByOperatorId = reversedByOperatorId;
        Reason = reason;
        CorrelationId = correlationId;
        IdempotencyKey = idempotencyKey;
        ReversedAtUtc = reversedAtUtc.ToUniversalTime();
    }

    public Guid Id { get; private set; }
    public Guid InvoiceId { get; private set; }
    public Guid PaymentId { get; private set; }
    public long AmountToman { get; private set; }
    public Guid ReversedByOperatorId { get; private set; }
    public string Reason { get; private set; } = null!;
    public string CorrelationId { get; private set; } = null!;
    public string IdempotencyKey { get; private set; } = null!;
    public DateTimeOffset ReversedAtUtc { get; private set; }

    public static CashSaleReversal Create(
        Guid id, Guid invoiceId, Guid paymentId, long amountToman,
        Guid reversedByOperatorId, string reason, string correlationId,
        string idempotencyKey, DateTimeOffset reversedAtUtc)
    {
        if (id == Guid.Empty) throw new ArgumentException("Reversal id is required.", nameof(id));
        if (invoiceId == Guid.Empty) throw new ArgumentException("Invoice id is required.", nameof(invoiceId));
        if (paymentId == Guid.Empty) throw new ArgumentException("Payment id is required.", nameof(paymentId));
        if (reversedByOperatorId == Guid.Empty) throw new ArgumentException("Operator id is required.", nameof(reversedByOperatorId));
        if (amountToman <= 0) throw new ArgumentOutOfRangeException(nameof(amountToman));
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length < 3 || reason.Trim().Length > 256 ||
            reason.Trim().Any(char.IsControl))
            throw new ArgumentException("A reason of 3-256 printable characters is required.", nameof(reason));
        if (string.IsNullOrWhiteSpace(correlationId) || correlationId.Trim().Length > 128 ||
            correlationId.Trim().Any(char.IsControl))
            throw new ArgumentException("A valid correlation id is required.", nameof(correlationId));
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Trim().Length > 200 ||
            idempotencyKey.Trim().Any(char.IsControl))
            throw new ArgumentException("A valid idempotency key is required.", nameof(idempotencyKey));
        return new CashSaleReversal(id, invoiceId, paymentId, amountToman, reversedByOperatorId,
            reason.Trim(), correlationId.Trim(), idempotencyKey.Trim(), reversedAtUtc);
    }
}
