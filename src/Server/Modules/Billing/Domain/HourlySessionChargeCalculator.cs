using System.Numerics;

namespace GameNet.Server.Modules.Billing.Domain;

public sealed record SessionChargeBreakdown(long ElapsedTicks, long HourlyRateToman, long AmountToman);

public static class HourlySessionChargeCalculator
{
    /// <summary>
    /// Charges the exact elapsed UTC duration against an hourly Toman rate, rounding any
    /// fractional Toman up to the next integer Toman. Integer arithmetic avoids binary
    /// floating-point money calculations and BigInteger avoids intermediate overflow.
    /// </summary>
    public static SessionChargeBreakdown Calculate(
        long hourlyRateToman,
        DateTimeOffset startedAtUtc,
        DateTimeOffset endedAtUtc)
    {
        if (hourlyRateToman <= 0)
            throw new ArgumentOutOfRangeException(nameof(hourlyRateToman), "Hourly rate must be positive.");

        var startTicks = startedAtUtc.ToUniversalTime().Ticks;
        var endTicks = endedAtUtc.ToUniversalTime().Ticks;
        var elapsedTicks = checked(endTicks - startTicks);
        if (elapsedTicks <= 0)
            throw new ArgumentOutOfRangeException(nameof(endedAtUtc), "Session end must be after session start.");

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
