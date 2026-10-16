namespace GameNet.Server.Modules.Billing.Domain;

public sealed class CashTenderCorrection
{
    private CashTenderCorrection() { }

    private CashTenderCorrection(
        Guid id,
        Guid invoiceId,
        Guid paymentId,
        int version,
        long previousAmountReceivedToman,
        long previousChangeGivenToman,
        long correctedAmountReceivedToman,
        long correctedChangeGivenToman,
        Guid correctedByOperatorId,
        string reason,
        string correlationId,
        string idempotencyKey,
        DateTimeOffset correctedAtUtc)
    {
        Id = id;
        InvoiceId = invoiceId;
        PaymentId = paymentId;
        Version = version;
        PreviousAmountReceivedToman = previousAmountReceivedToman;
        PreviousChangeGivenToman = previousChangeGivenToman;
        CorrectedAmountReceivedToman = correctedAmountReceivedToman;
        CorrectedChangeGivenToman = correctedChangeGivenToman;
        CorrectedByOperatorId = correctedByOperatorId;
        Reason = reason;
        CorrelationId = correlationId;
        IdempotencyKey = idempotencyKey;
        CorrectedAtUtc = correctedAtUtc.ToUniversalTime();
    }

    public Guid Id { get; private set; }
    public Guid InvoiceId { get; private set; }
    public Guid PaymentId { get; private set; }
    public int Version { get; private set; }
    public long PreviousAmountReceivedToman { get; private set; }
    public long PreviousChangeGivenToman { get; private set; }
    public long CorrectedAmountReceivedToman { get; private set; }
    public long CorrectedChangeGivenToman { get; private set; }
    public Guid CorrectedByOperatorId { get; private set; }
    public string Reason { get; private set; } = null!;
    public string CorrelationId { get; private set; } = null!;
    public string IdempotencyKey { get; private set; } = null!;
    public DateTimeOffset CorrectedAtUtc { get; private set; }

    public static CashTenderCorrection Create(
        Guid id,
        Guid invoiceId,
        Guid paymentId,
        int version,
        long previousAmountReceivedToman,
        long previousChangeGivenToman,
        long amountAppliedToman,
        long correctedAmountReceivedToman,
        Guid correctedByOperatorId,
        string reason,
        string correlationId,
        string idempotencyKey,
        DateTimeOffset correctedAtUtc)
    {
        if (id == Guid.Empty) throw new ArgumentException("Correction id is required.", nameof(id));
        if (invoiceId == Guid.Empty) throw new ArgumentException("Invoice id is required.", nameof(invoiceId));
        if (paymentId == Guid.Empty) throw new ArgumentException("Payment id is required.", nameof(paymentId));
        if (correctedByOperatorId == Guid.Empty) throw new ArgumentException("Operator id is required.", nameof(correctedByOperatorId));
        if (version <= 0) throw new ArgumentOutOfRangeException(nameof(version));
        if (amountAppliedToman <= 0) throw new ArgumentOutOfRangeException(nameof(amountAppliedToman));
        if (previousAmountReceivedToman < amountAppliedToman ||
            previousChangeGivenToman != checked(previousAmountReceivedToman - amountAppliedToman))
            throw new ArgumentException("Previous cash tender facts do not reconcile.", nameof(previousAmountReceivedToman));
        if (correctedAmountReceivedToman < amountAppliedToman)
            throw new ArgumentOutOfRangeException(nameof(correctedAmountReceivedToman), "Corrected cash received cannot be less than the invoice amount.");
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length < 3 || reason.Trim().Length > 256 ||
            reason.Trim().Any(char.IsControl))
            throw new ArgumentException("A reason of 3-256 printable characters is required.", nameof(reason));
        if (string.IsNullOrWhiteSpace(correlationId) || correlationId.Trim().Length > 128 ||
            correlationId.Trim().Any(char.IsControl))
            throw new ArgumentException("A valid correlation id is required.", nameof(correlationId));
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Trim().Length > 200 ||
            idempotencyKey.Trim().Any(char.IsControl))
            throw new ArgumentException("A valid idempotency key is required.", nameof(idempotencyKey));

        var correctedChange = checked(correctedAmountReceivedToman - amountAppliedToman);
        return new CashTenderCorrection(
            id, invoiceId, paymentId, version,
            previousAmountReceivedToman, previousChangeGivenToman,
            correctedAmountReceivedToman, correctedChange, correctedByOperatorId,
            reason.Trim(), correlationId.Trim(), idempotencyKey.Trim(), correctedAtUtc);
    }
}