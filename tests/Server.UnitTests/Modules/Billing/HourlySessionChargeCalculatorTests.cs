using GameNet.Server.Modules.Billing.Domain;
using Xunit;

namespace GameNet.Server.UnitTests.Modules.Billing;

public sealed class HourlySessionChargeCalculatorTests
{
    [Theory]
    [InlineData(60_000L, 60, 60_000L)]
    [InlineData(60_000L, 30, 30_000L)]
    [InlineData(60_000L, 15, 15_000L)]
    [InlineData(72_000L, 30, 36_000L)]
    public void Exact_hourly_fraction_produces_expected_integer_toman_amount(long rate, int minutes, long expected)
    {
        var result = HourlySessionChargeCalculator.Calculate(rate, TimeSpan.FromMinutes(minutes).Ticks);

        Assert.Equal(TimeSpan.FromMinutes(minutes).Ticks, result.ElapsedTicks);
        Assert.Equal(rate, result.HourlyRateToman);
        Assert.Equal(expected, result.AmountToman);
    }

    [Fact]
    public void Fractional_toman_is_rounded_up_without_binary_floating_point()
    {
        var result = HourlySessionChargeCalculator.Calculate(60_000L, TimeSpan.TicksPerHour + 1);

        Assert.Equal(60_001L, result.AmountToman);
    }

    [Fact]
    public void Invalid_rate_duration_and_overflow_are_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => HourlySessionChargeCalculator.Calculate(0, TimeSpan.TicksPerHour));
        Assert.Throws<ArgumentOutOfRangeException>(() => HourlySessionChargeCalculator.Calculate(60_000, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => HourlySessionChargeCalculator.Calculate(60_000, -1));
        Assert.Throws<OverflowException>(() => HourlySessionChargeCalculator.Calculate(long.MaxValue, TimeSpan.FromDays(365).Ticks));
    }
}
