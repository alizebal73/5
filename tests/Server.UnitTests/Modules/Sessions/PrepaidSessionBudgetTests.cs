using GameNet.Server.Modules.Billing.Domain;
using GameNet.Server.Modules.Sessions.Domain;
using Xunit;

namespace GameNet.Server.UnitTests.Modules.Sessions;

public sealed class PrepaidSessionBudgetTests
{
    [Theory]
    [InlineData(105_000L, 105_000L)]
    [InlineData(51_000L, 100_000L)]
    [InlineData(1_001L, 33_333L)]
    [InlineData(9_876_543L, 177_777L)]
    public void Budget_never_allows_calculated_charge_to_exceed_reservation(
        long reservedToman, long hourlyRateToman)
    {
        var maximumTicks = PrepaidSessionBudget.CalculateMaximumElapsedTicks(reservedToman, hourlyRateToman);

        Assert.True(maximumTicks > 0);
        Assert.InRange(
            HourlySessionChargeCalculator.Calculate(hourlyRateToman, maximumTicks).AmountToman,
            1,
            reservedToman);
        Assert.True(
            HourlySessionChargeCalculator.Calculate(hourlyRateToman, maximumTicks + 1).AmountToman > reservedToman);
    }

    [Fact]
    public void Exact_hourly_budget_returns_exact_duration()
    {
        Assert.Equal(
            TimeSpan.TicksPerHour,
            PrepaidSessionBudget.CalculateMaximumElapsedTicks(60_000, 60_000));
    }

    [Fact]
    public void Invalid_credit_or_rate_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PrepaidSessionBudget.CalculateMaximumElapsedTicks(0, 60_000));
        Assert.Throws<ArgumentOutOfRangeException>(() => PrepaidSessionBudget.CalculateMaximumElapsedTicks(1, 0));
    }

    [Fact]
    public void Budget_shorter_than_one_tick_returns_zero_for_start_use_case_to_reject()
    {
        Assert.Equal(0, PrepaidSessionBudget.CalculateMaximumElapsedTicks(1, long.MaxValue));
    }
}
