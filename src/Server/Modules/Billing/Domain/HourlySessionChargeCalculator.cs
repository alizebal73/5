using System.Numerics;

namespace GameNet.Server.Modules.Billing.Domain;

public sealed record SessionChargeBreakdown(long ElapsedTicks, long HourlyRateToman, long AmountToman);

public static class HourlySessionChargeCalculator
{
    /// <summary>
    /// Charges monotonic elapsed duration against the immutable hourly Toman rate,
    /// rounding any fractional Toman upward. UTC event timestamps are never inputs here.
    /// </summary>
    public static SessionChargeBreakdown Calculate(long hourlyRateToman, long elapsedTicks)
    {
        if (hourlyRateToman <= 0)
            throw new ArgumentOutOfRangeException(nameof(hourlyRateToman), "Hourly rate must be positive.");
        if (elapsedTicks <= 0)
            throw new ArgumentOutOfRangeException(nameof(elapsedTicks), "Session elapsed duration must be positive.");

        var numerator = new BigInteger(hourlyRateToman) * elapsedTicks;
        var denominator = new BigInteger(TimeSpan.TicksPerHour);
        var amount = BigInteger.DivRem(numerator, denominator, out var remainder);
        if (remainder > BigInteger.Zero)
            amount += BigInteger.One;

        if (amount > long.MaxValue)
            throw new OverflowException("SESSION_CHARGE_EXCEEDS_TOMAN_RANGE");

        return new SessionChargeBreakdown(elapsedTicks, hourlyRateToman, (long)amount);
    }
}
