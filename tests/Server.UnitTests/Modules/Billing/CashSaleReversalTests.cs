using GameNet.Server.Modules.Billing.Domain;
using Xunit;

namespace GameNet.Server.UnitTests.Modules.Billing;

public sealed class CashSaleReversalTests
{
    [Fact]
    public void Cash_sale_reversal_requires_reason_and_positive_amount()
    {
        Assert.Throws<ArgumentException>(() => CashSaleReversal.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 30_000L, Guid.NewGuid(),
            " ", "test-correlation", "test-reversal", DateTimeOffset.UtcNow));
        Assert.Throws<ArgumentOutOfRangeException>(() => CashSaleReversal.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 0L, Guid.NewGuid(),
            "Customer refund", "test-correlation", "test-reversal", DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Reversal_ledger_records_positive_amount_magnitude_with_explicit_type()
    {
        var invoiceId = Guid.NewGuid();
        var paymentId = Guid.NewGuid();
        var entry = BillingLedgerEntry.RecordCashSaleReversal(
            Guid.NewGuid(), invoiceId, paymentId, 30_000L, Guid.NewGuid(),
            DateTimeOffset.Parse("2026-10-16T10:00:00Z"));

        Assert.Equal(BillingLedgerEntryType.CashSaleReversal, entry.EntryType);
        Assert.Equal(30_000L, entry.AmountToman);
        Assert.Equal(-30_000L, entry.GetSignedAmountToman());
        Assert.Equal(invoiceId, entry.InvoiceId);
        Assert.Equal(paymentId, entry.PaymentId);
    }

    [Fact]
    public void Invoice_reversal_preserves_original_paid_facts_and_cannot_repeat()
    {
        var issuedAt = DateTimeOffset.Parse("2026-10-16T10:00:00Z");
        var paidAt = issuedAt.AddMinutes(10);
        var paidBy = Guid.NewGuid();
        var invoice = Invoice.CreateForSession(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "PC-1", "PC", 60_000L, TimeSpan.FromMinutes(30).Ticks, 30_000L, Guid.NewGuid(), issuedAt);
        invoice.MarkPaid(paidBy, paidAt);
        invoice.MarkReversed();

        Assert.Equal(InvoiceStatus.Reversed, invoice.Status);
        Assert.Equal(paidBy, invoice.PaidByOperatorId);
        Assert.Equal(paidAt, invoice.PaidAtUtc);
        Assert.Throws<InvalidOperationException>(() => invoice.MarkReversed());
    }
}
