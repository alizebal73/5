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
    public void Wallet_paid_invoice_records_customer_payer_not_operator()
    {
        var issuedAt = DateTimeOffset.Parse("2026-10-14T10:30:00Z");
        var customerId = Guid.NewGuid();
        var invoice = Invoice.CreateForSession(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), customerId, Guid.NewGuid(),
            "PC-STANDARD", "PC Standard", 60_000L, TimeSpan.FromMinutes(30).Ticks,
            30_000L, Guid.NewGuid(), issuedAt);

        invoice.MarkPaid(Guid.Empty, issuedAt.AddSeconds(5), customerId);

        Assert.Equal(Guid.Empty, invoice.PaidByOperatorId);
        Assert.Equal(customerId, invoice.PaidByCustomerId);
        Assert.Equal(InvoiceStatus.Paid, invoice.Status);
    }

    [Fact]
    public void Automatic_invoice_uses_system_issuer_without_impersonating_an_operator()
    {
        var issuedAt = DateTimeOffset.Parse("2026-10-14T10:30:00Z");
        var invoice = Invoice.CreateForSession(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "PC-STANDARD", "PC Standard", 60_000L, TimeSpan.FromMinutes(30).Ticks,
            30_000L, Guid.Empty, issuedAt, issuedBySystem: true);

        Assert.True(invoice.IssuedBySystem);
        Assert.Equal(Guid.Empty, invoice.IssuedByOperatorId);
    }

    [Fact]
    public void Invoice_rejects_non_positive_amount()
    {
        var issuedAt = DateTimeOffset.Parse("2026-10-14T10:30:00Z");
        Assert.Throws<ArgumentOutOfRangeException>(() => Invoice.CreateForSession(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "PC-1", "PC", 60_000L, TimeSpan.FromMinutes(30).Ticks, 0L, Guid.NewGuid(), issuedAt));
    }

    [Fact]
    public void Invoice_state_transition_allows_paid_utc_before_issue_when_wall_clock_moves_backwards()
    {
        var issuedAt = DateTimeOffset.Parse("2026-10-14T10:30:00Z");
        var paidAt = issuedAt.AddTicks(-1);
        var invoice = Invoice.CreateForSession(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "PC-1", "PC", 60_000L, TimeSpan.FromMinutes(30).Ticks, 30_000L, Guid.NewGuid(), issuedAt);

        invoice.MarkPaid(Guid.NewGuid(), paidAt);

        Assert.Equal(InvoiceStatus.Paid, invoice.Status);
        Assert.Equal(paidAt, invoice.PaidAtUtc);
    }
}
