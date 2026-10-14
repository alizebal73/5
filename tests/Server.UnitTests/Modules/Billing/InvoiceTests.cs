using GameNet.Server.Modules.Billing.Domain;
using Xunit;

namespace GameNet.Server.UnitTests.Modules.Billing;

public sealed class InvoiceTests
{
    [Fact]
    public void Session_end_issues_pending_invoice_and_cash_payment_marks_it_paid()
    {
        var issuedAt = DateTimeOffset.Parse("2026-10-14T10:30:00Z");
        var invoice = Invoice.CreateForSession(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "PC-STANDARD", "PC Standard", 60_000L, TimeSpan.FromMinutes(30).Ticks,
            30_000L, Guid.NewGuid(), issuedAt);

        Assert.Equal(InvoiceStatus.PendingPayment, invoice.Status);
        Assert.Null(invoice.PaidAtUtc);
        Assert.Null(invoice.PaidByOperatorId);

        var paidBy = Guid.NewGuid();
        invoice.MarkPaid(paidBy, issuedAt.AddSeconds(5));

        Assert.Equal(InvoiceStatus.Paid, invoice.Status);
        Assert.Equal(paidBy, invoice.PaidByOperatorId);
        Assert.Equal(issuedAt.AddSeconds(5), invoice.PaidAtUtc);
        Assert.Throws<InvalidOperationException>(() => invoice.MarkPaid(Guid.NewGuid(), issuedAt.AddSeconds(6)));
    }

    [Fact]
    public void Invoice_rejects_non_positive_amount_and_payment_before_issue()
    {
        var issuedAt = DateTimeOffset.Parse("2026-10-14T10:30:00Z");
        Assert.Throws<ArgumentOutOfRangeException>(() => Invoice.CreateForSession(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "PC-1", "PC", 60_000L, TimeSpan.FromMinutes(30).Ticks, 0L, Guid.NewGuid(), issuedAt));

        var invoice = Invoice.CreateForSession(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "PC-1", "PC", 60_000L, TimeSpan.FromMinutes(30).Ticks, 30_000L, Guid.NewGuid(), issuedAt);
        Assert.Throws<ArgumentOutOfRangeException>(() => invoice.MarkPaid(Guid.NewGuid(), issuedAt.AddTicks(-1)));
    }
}
