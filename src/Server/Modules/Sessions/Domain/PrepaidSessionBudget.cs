using System.Numerics;

namespace GameNet.Server.Modules.Sessions.Domain;

/// <summary>
/// Computes the latest monotonic elapsed time whose upward-rounded Toman charge remains
/// within a reserved Wallet amount. The display timer is informational; Server expiry is authoritative.
/// </summary>
public static class PrepaidSessionBudget
{
    public static long CalculateMaximumElapsedTicks(long reservedAmountToman, long hourlyRateToman)
    {
        if (reservedAmountToman <= 0)
            throw new ArgumentOutOfRangeException(nameof(reservedAmountToman));
        if (hourlyRateToman <= 0)
            throw new ArgumentOutOfRangeException(nameof(hourlyRateToman));

        var ticks = new BigInteger(reservedAmountToman) * TimeSpan.TicksPerHour / hourlyRateToman;
        if (ticks <= BigInteger.Zero)
            return 0;
        return ticks > long.MaxValue ? long.MaxValue : (long)ticks;
    }
}
