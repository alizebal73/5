namespace GameNet.Server.Modules.Billing.Domain;

public sealed class Invoice
{
    private Invoice() { }

    private Invoice(
        Guid id,
        Guid sessionId,
        Guid stationId,
        Guid customerId,
        Guid tariffId,
        string tariffCodeSnapshot,
        string tariffNameSnapshot,
        long tariffHourlyRateTomanSnapshot,
        long elapsedTicks,
        long amountToman,
        Guid issuedByOperatorId,
        DateTimeOffset issuedAtUtc)
    {
        Id = id;
        SessionId = sessionId;
        StationId = stationId;
        CustomerId = customerId;
        TariffId = tariffId;
        TariffCodeSnapshot = NormalizeSnapshot(tariffCodeSnapshot, nameof(tariffCodeSnapshot));
        TariffNameSnapshot = NormalizeSnapshot(tariffNameSnapshot, nameof(tariffNameSnapshot));
        TariffHourlyRateTomanSnapshot = tariffHourlyRateTomanSnapshot;
        ElapsedTicks = elapsedTicks;
        AmountToman = amountToman;
        IssuedByOperatorId = issuedByOperatorId;
        IssuedAtUtc = issuedAtUtc.ToUniversalTime();
        Status = InvoiceStatus.PendingPayment;
    }

    public Guid Id { get; private set; }
    public Guid SessionId { get; private set; }
    public Guid StationId { get; private set; }
    public Guid CustomerId { get; private set; }
    public Guid TariffId { get; private set; }
    public string TariffCodeSnapshot { get; private set; } = null!;
    public string TariffNameSnapshot { get; private set; } = null!;
    public long TariffHourlyRateTomanSnapshot { get; private set; }
    public long ElapsedTicks { get; private set; }
    public long AmountToman { get; private set; }
    public Guid IssuedByOperatorId { get; private set; }
    public DateTimeOffset IssuedAtUtc { get; private set; }
    public Guid? PaidByOperatorId { get; private set; }
    public DateTimeOffset? PaidAtUtc { get; private set; }
    public InvoiceStatus Status { get; private set; }

    public static Invoice CreateForSession(
        Guid id,
        Guid sessionId,
        Guid stationId,
        Guid customerId,
        Guid tariffId,
        string tariffCodeSnapshot,
        string tariffNameSnapshot,
        long tariffHourlyRateTomanSnapshot,
        long elapsedTicks,
        long amountToman,
        Guid issuedByOperatorId,
        DateTimeOffset issuedAtUtc)
    {
        if (id == Guid.Empty) throw new ArgumentException("Invoice id is required.", nameof(id));
        if (sessionId == Guid.Empty) throw new ArgumentException("Session id is required.", nameof(sessionId));
        if (stationId == Guid.Empty) throw new ArgumentException("Station id is required.", nameof(stationId));
        if (customerId == Guid.Empty) throw new ArgumentException("Customer id is required.", nameof(customerId));
        if (tariffId == Guid.Empty) throw new ArgumentException("Tariff id is required.", nameof(tariffId));
        if (issuedByOperatorId == Guid.Empty) throw new ArgumentException("Operator id is required.", nameof(issuedByOperatorId));
        if (tariffHourlyRateTomanSnapshot <= 0) throw new ArgumentOutOfRangeException(nameof(tariffHourlyRateTomanSnapshot));
        if (elapsedTicks <= 0) throw new ArgumentOutOfRangeException(nameof(elapsedTicks));
        if (amountToman <= 0) throw new ArgumentOutOfRangeException(nameof(amountToman));

        return new Invoice(id, sessionId, stationId, customerId, tariffId, tariffCodeSnapshot,
            tariffNameSnapshot, tariffHourlyRateTomanSnapshot, elapsedTicks, amountToman, issuedByOperatorId, issuedAtUtc);
    }

    public void MarkPaid(Guid paidByOperatorId, DateTimeOffset paidAtUtc)
    {
        if (Status != InvoiceStatus.PendingPayment)
            throw new InvalidOperationException("INVOICE_NOT_PENDING_PAYMENT");
        if (paidByOperatorId == Guid.Empty)
            throw new ArgumentException("Operator id is required.", nameof(paidByOperatorId));

        var paid = paidAtUtc.ToUniversalTime();
        if (paid < IssuedAtUtc)
            throw new ArgumentOutOfRangeException(nameof(paidAtUtc), "Payment cannot precede invoice issuance.");

        PaidByOperatorId = paidByOperatorId;
        PaidAtUtc = paid;
        Status = InvoiceStatus.Paid;
    }

    private static string NormalizeSnapshot(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Tariff snapshot is required.", parameterName);
        var normalized = value.Trim();
        if (normalized.Length > 120 || normalized.Any(char.IsControl))
            throw new ArgumentException("Tariff snapshot is too long or contains control characters.", parameterName);
        return normalized;
    }
}
