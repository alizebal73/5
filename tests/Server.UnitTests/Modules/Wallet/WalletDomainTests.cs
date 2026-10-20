using GameNet.Server.Modules.Billing.Domain;
using GameNet.Server.Modules.Wallet.Domain;
using Xunit;

namespace GameNet.Server.UnitTests.Modules.Wallet;

public sealed class WalletDomainTests
{
    [Fact]
    public void CashTopUp_CreditsBalanceAndKeepsChangeSeparate()
    {
        var now = DateTimeOffset.Parse("2026-10-19T12:00:00Z");
        var account = WalletAccount.Open(Guid.NewGuid(), now);
        var balance = account.Credit(105_000, now);

        var receipt = CashReceipt.RecordWalletTopUp(Guid.NewGuid(), account.CustomerId,
            105_000, 110_000, Guid.NewGuid(), now, "wallet-test-1", "correlation-1", "UnitTest", "fingerprint-test-1");
        var entry = WalletLedgerEntry.RecordCashTopUp(Guid.NewGuid(), account.CustomerId, receipt.Id,
            105_000, balance, receipt.ReceivedByOperatorId, now, account.Version);

        Assert.Equal(105_000, account.BalanceToman);
        Assert.Equal(1, account.Version);
        Assert.Equal(105_000, receipt.AmountAppliedToman);
        Assert.Equal(110_000, receipt.AmountReceivedToman);
        Assert.Equal(5_000, receipt.ChangeGivenToman);
        Assert.Equal(105_000, entry.AmountToman);
        Assert.Equal(105_000, entry.BalanceAfterToman);
    }

    [Fact]
    public void WalletAccount_RejectsNonPositiveCreditsWithoutChangingBalance()
    {
        var account = WalletAccount.Open(Guid.NewGuid(), DateTimeOffset.UtcNow);
        Assert.Throws<ArgumentOutOfRangeException>(() => account.Credit(0, DateTimeOffset.UtcNow));
        Assert.Throws<ArgumentOutOfRangeException>(() => account.Credit(-1, DateTimeOffset.UtcNow));
        Assert.Equal(0, account.BalanceToman);
        Assert.Equal(0, account.Version);
    }

    [Fact]
    public void WalletAccount_OverflowDoesNotPartiallyUpdateProjection()
    {
        var account = WalletAccount.Open(Guid.NewGuid(), DateTimeOffset.UtcNow);
        account.Credit(long.MaxValue, DateTimeOffset.UtcNow);
        Assert.Throws<OverflowException>(() => account.Credit(1, DateTimeOffset.UtcNow));
        Assert.Equal(long.MaxValue, account.BalanceToman);
        Assert.Equal(1, account.Version);
    }

    [Fact]
    public void CashReceipt_RejectsCashBelowCreditedValue()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CashReceipt.RecordWalletTopUp(
            Guid.NewGuid(), Guid.NewGuid(), 100, 99, Guid.NewGuid(), DateTimeOffset.UtcNow,
            "wallet-underpaid", "correlation", "UnitTest", "fingerprint-underpaid"));
    }

    [Fact]
    public void PrepaidReservation_moves_all_available_value_and_blocks_top_up_until_settlement()
    {
        var now = DateTimeOffset.Parse("2026-10-20T00:00:00Z");
        var account = WalletAccount.Open(Guid.NewGuid(), now);
        account.Credit(105_000, now);

        var availableAfterReserve = account.ReserveAllAvailable(105_000, now);

        Assert.Equal(0, availableAfterReserve);
        Assert.Equal(0, account.BalanceToman);
        Assert.Equal(105_000, account.ReservedBalanceToman);
        Assert.Equal(2, account.Version);
        Assert.Throws<InvalidOperationException>(() => account.Credit(1, now));
        Assert.Throws<ArgumentOutOfRangeException>(() => account.ReserveAllAvailable(1, now));
    }

    [Fact]
    public void PrepaidSettlement_charges_actual_amount_and_releases_unused_credit()
    {
        var now = DateTimeOffset.Parse("2026-10-20T00:00:00Z");
        var account = WalletAccount.Open(Guid.NewGuid(), now);
        account.Credit(105_000, now);
        account.ReserveAllAvailable(105_000, now);

        var settlement = account.SettlePrepaidReservation(105_000, 33_000, now);

        Assert.Equal(0, settlement.ChargeBalanceAfterToman);
        Assert.Equal(72_000, settlement.ChargeReservedBalanceAfterToman);
        Assert.Equal(72_000, settlement.ReleaseAmountToman);
        Assert.Equal(72_000, settlement.FinalBalanceToman);
        Assert.Equal(0, settlement.FinalReservedBalanceToman);
        Assert.Equal(4, settlement.FinalWalletVersion);
        Assert.Equal(72_000, account.BalanceToman);
        Assert.Equal(0, account.ReservedBalanceToman);
        Assert.Throws<InvalidOperationException>(() => account.SettlePrepaidReservation(105_000, 33_000, now));
    }

    [Fact]
    public void PrepaidSettlement_rejects_charge_above_reserved_credit_without_mutation()
    {
        var now = DateTimeOffset.Parse("2026-10-20T00:00:00Z");
        var account = WalletAccount.Open(Guid.NewGuid(), now);
        account.Credit(10_000, now);
        account.ReserveAllAvailable(10_000, now);

        Assert.Throws<ArgumentOutOfRangeException>(() => account.SettlePrepaidReservation(10_000, 10_001, now));
        Assert.Equal(0, account.BalanceToman);
        Assert.Equal(10_000, account.ReservedBalanceToman);
        Assert.Equal(2, account.Version);
    }

    [Fact]
    public void Ledger_RejectsBalanceAfterBelowCredit()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => WalletLedgerEntry.RecordCashTopUp(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 100, 99, Guid.NewGuid(), DateTimeOffset.UtcNow, 1));
    }
}