namespace GameNet.Server.Modules.Billing.Domain;

public sealed class Payment
{
    private Payment() { }

    private Payment(
        Guid id,
        Guid invoiceId,
        long amountAppliedToman,
        long amountReceivedToman,
        long changeGivenToman,
        Guid receivedByOperatorId,
        DateTimeOffset receivedAtUtc)
    {
        Id = id;
        InvoiceId = invoiceId;
        Method = PaymentMethod.Cash;
        AmountAppliedToman = amountAppliedToman;
        AmountReceivedToman = amountReceivedToman;
        ChangeGivenToman = changeGivenToman;
        ReceivedByOperatorId = receivedByOperatorId;
        ReceivedAtUtc = receivedAtUtc.ToUniversalTime();
    }

    public Guid Id { get; private set; }
    public Guid InvoiceId { get; private set; }
    public PaymentMethod Method { get; private set; }
    public long AmountAppliedToman { get; private set; }
    public long AmountReceivedToman { get; private set; }
    public long ChangeGivenToman { get; private set; }
    public Guid ReceivedByOperatorId { get; private set; }
    public DateTimeOffset ReceivedAtUtc { get; private set; }

    public static Payment CreateCash(
        Guid id,
        Guid invoiceId,
        long amountDueToman,
        long amountReceivedToman,
        Guid receivedByOperatorId,
        DateTimeOffset receivedAtUtc)
    {
        if (id == Guid.Empty) throw new ArgumentException("Payment id is required.", nameof(id));
        if (invoiceId == Guid.Empty) throw new ArgumentException("Invoice id is required.", nameof(invoiceId));
        if (receivedByOperatorId == Guid.Empty) throw new ArgumentException("Operator id is required.", nameof(receivedByOperatorId));
        if (amountDueToman <= 0) throw new ArgumentOutOfRangeException(nameof(amountDueToman));
        if (amountReceivedToman < amountDueToman)
            throw new ArgumentOutOfRangeException(nameof(amountReceivedToman), "Received cash cannot be less than the invoice amount.");

        var change = checked(amountReceivedToman - amountDueToman);
        return new Payment(id, invoiceId, amountDueToman, amountReceivedToman, change, receivedByOperatorId, receivedAtUtc);
    }
}
