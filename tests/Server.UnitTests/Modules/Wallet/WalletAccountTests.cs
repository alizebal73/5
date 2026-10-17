using GameNet.Server.Modules.Wallet.Domain;
using Xunit;

namespace GameNet.Server.UnitTests.Modules.Wallet;

public sealed class WalletAccountTests
{
    [Fact]
    public void Purchased_value_credit_updates_projection_with_checked_whole_toman_arithmetic()
    {
        var customerId = Guid.NewGuid();
        var now = DateTimeOffset.Parse("2026-10-17T08:00:00Z");
        var account = WalletAccount.Open(customerId, now);

        Assert.Equal(0L, account.PurchasedBalanceToman);
        Assert.Equal(0, account.Version);

        account.CreditPurchasedValue(105_000L, now.AddMinutes(1));

        Assert.Equal(105_000L, account.PurchasedBalanceToman);
        Assert.Equal(1, account.Version);
        Assert.Equal(now.AddMinutes(1), account.UpdatedAtUtc);
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    public void Purchased_value_credit_rejects_nonpositive_amounts(long amount)
    {
        var account = WalletAccount.Open(Guid.NewGuid(), DateTimeOffset.UtcNow);
        Assert.Throws<ArgumentOutOfRangeException>(() => account.CreditPurchasedValue(amount, DateTimeOffset.UtcNow));
        Assert.Equal(0L, account.PurchasedBalanceToman);
        Assert.Equal(0, account.Version);
    }

    [Fact]
    public void Purchased_value_overflow_does_not_partially_change_the_projection()
    {
        var account = WalletAccount.Open(Guid.NewGuid(), DateTimeOffset.UtcNow);
        account.CreditPurchasedValue(long.MaxValue, DateTimeOffset.UtcNow);
        var version = account.Version;
        var updatedAt = account.UpdatedAtUtc;

        Assert.Throws<OverflowException>(() => account.CreditPurchasedValue(1, updatedAt.AddMinutes(1)));

        Assert.Equal(long.MaxValue, account.PurchasedBalanceToman);
        Assert.Equal(version, account.Version);
        Assert.Equal(updatedAt, account.UpdatedAtUtc);
    }
}
