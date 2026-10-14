using GameNet.Server.Modules.Billing.Domain;
using Xunit;

namespace GameNet.Server.UnitTests.Modules.Billing;

public sealed class PaymentTests
{
    [Theory]
    [InlineData(30_000L, 30_000L, 0L)]
    [InlineData(30_000L, 50_000L, 20_000L)]
    public void Cash_payment_records_amount_applied_received_and_change(long due, long received, long change)
    {
        var payment = Payment.CreateCash(
            Guid.NewGuid(), Guid.NewGuid(), due, received, Guid.NewGuid(),
            DateTimeOffset.Parse("2026-10-14T10:30:00Z"));

        Assert.Equal(PaymentMethod.Cash, payment.Method);
        Assert.Equal(due, payment.AmountAppliedToman);
        Assert.Equal(received, payment.AmountReceivedToman);
        Assert.Equal(change, payment.ChangeGivenToman);
    }

    [Fact]
    public void Cash_payment_rejects_insufficient_amount_and_checked_change_overflow()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Payment.CreateCash(
            Guid.NewGuid(), Guid.NewGuid(), 30_000L, 29_999L, Guid.NewGuid(), DateTimeOffset.UtcNow));

        Assert.Throws<OverflowException>(() => Payment.CreateCash(
            Guid.NewGuid(), Guid.NewGuid(), 1L, long.MaxValue, Guid.NewGuid(), DateTimeOffset.UtcNow));
    }
}
