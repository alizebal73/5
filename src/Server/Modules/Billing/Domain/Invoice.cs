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
        DateTimeOffset issuedAtUtc,
        bool issuedBySystem)
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
        IssuedBySystem = issuedBySystem;
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
    public bool IssuedBySystem { get; private set; }
    public DateTimeOffset IssuedAtUtc { get; private set; }
    public Guid? PaidByOperatorId { get; private set; }
    public Guid? PaidByCustomerId { get; private set; }
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
        DateTimeOffset issuedAtUtc,
        bool issuedBySystem = false)
    {
        if (id == Guid.Empty) throw new ArgumentException("Invoice id is required.", nameof(id));
        if (sessionId == Guid.Empty) throw new ArgumentException("Session id is required.", nameof(sessionId));
        if (stationId == Guid.Empty) throw new ArgumentException("Station id is required.", nameof(stationId));
        if (customerId == Guid.Empty) throw new ArgumentException("Customer id is required.", nameof(customerId));
        if (tariffId == Guid.Empty) throw new ArgumentException("Tariff id is required.", nameof(tariffId));
        if (issuedBySystem ? issuedByOperatorId != Guid.Empty : issuedByOperatorId == Guid.Empty)
            throw new ArgumentException("Invoice issuer must be either an Operator or the System.", nameof(issuedByOperatorId));
        if (tariffHourlyRateTomanSnapshot <= 0) throw new ArgumentOutOfRangeException(nameof(tariffHourlyRateTomanSnapshot));
        if (elapsedTicks <= 0) throw new ArgumentOutOfRangeException(nameof(elapsedTicks));
        if (amountToman <= 0) throw new ArgumentOutOfRangeException(nameof(amountToman));

        return new Invoice(id, sessionId, stationId, customerId, tariffId, tariffCodeSnapshot,
            tariffNameSnapshot, tariffHourlyRateTomanSnapshot, elapsedTicks, amountToman, issuedByOperatorId, issuedAtUtc);
    }

    public void MarkPaid(Guid paidByOperatorId, DateTimeOffset paidAtUtc, Guid? paidByCustomerId = null)
    {
        if (Status != InvoiceStatus.PendingPayment)
            throw new InvalidOperationException("INVOICE_NOT_PENDING_PAYMENT");
        if (paidByCustomerId is Guid customerPayerId)
        {
            if (customerPayerId == Guid.Empty || paidByOperatorId != Guid.Empty)
                throw new ArgumentException("A Wallet-funded invoice must identify the Customer payer and no Operator payer.");
        }
        else if (paidByOperatorId == Guid.Empty)
        {
            throw new ArgumentException("Operator id is required unless the invoice is paid from Customer Wallet credit.", nameof(paidByOperatorId));
        }

        PaidByOperatorId = paidByOperatorId;
        PaidByCustomerId = paidByCustomerId;
        PaidAtUtc = paidAtUtc.ToUniversalTime();
        Status = InvoiceStatus.Paid;
    }

    public void MarkReversed()
    {
        if (Status != InvoiceStatus.Paid)
            throw new InvalidOperationException("INVOICE_NOT_PAID_FOR_REVERSAL");
        if (PaidAtUtc is null || PaidByOperatorId is null || PaidByOperatorId == Guid.Empty || PaidByCustomerId is not null)
            throw new InvalidOperationException("INVOICE_PAID_FACTS_MISSING");

        // Only the lifecycle projection changes; original paid facts and invoice amount remain immutable.
        Status = InvoiceStatus.Reversed;
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
