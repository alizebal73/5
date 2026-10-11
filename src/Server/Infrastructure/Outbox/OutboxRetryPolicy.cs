namespace GameNet.Server.Infrastructure.Outbox;

public static class OutboxRetryPolicy
{
    /// <summary>
    /// Returns capped exponential backoff with 20% downward jitter to avoid synchronized retries.
    /// The jitter sample is injected so policy tests remain deterministic.
    /// </summary>
    public static TimeSpan CalculateDelay(
        int attemptCount,
        TimeSpan initialDelay,
        TimeSpan maximumDelay,
        double jitterSample)
    {
        if (attemptCount < 1)
            throw new ArgumentOutOfRangeException(nameof(attemptCount));
        if (initialDelay <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(initialDelay));
        if (maximumDelay < initialDelay)
            throw new ArgumentOutOfRangeException(nameof(maximumDelay));
        if (double.IsNaN(jitterSample) || jitterSample is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(jitterSample));

        var exponent = Math.Min(attemptCount - 1, 30);
        var multiplier = 1L << exponent;
        var maxTicks = maximumDelay.Ticks;
        var ceilingTicks = initialDelay.Ticks > maxTicks / multiplier
            ? maxTicks
            : Math.Min(maxTicks, initialDelay.Ticks * multiplier);
        var lowerTicks = Math.Max(1L, ceilingTicks * 4 / 5);
        var jitterTicks = (long)((ceilingTicks - lowerTicks) * jitterSample);
        return TimeSpan.FromTicks(lowerTicks + jitterTicks);
    }
}
