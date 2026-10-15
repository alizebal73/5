using GameNet.Shared.Primitives;
using Xunit;

namespace GameNet.Server.UnitTests;

public sealed class MonotonicElapsedTimeTests
{
    private static readonly Guid Boot = Guid.Parse("04ad13fd-6cb2-4b17-a54d-5e0a48f3e9f0");

    [Theory]
    [InlineData(1)]
    [InlineData(-1)]
    public void Utc_wall_clock_jumps_do_not_change_monotonic_duration(int utcJumpHours)
    {
        var frequency = TimeSpan.TicksPerSecond;
        var startedTimestamp = 5_000_000L;
        var startedUptime = 100_000L;
        var elapsed = TimeSpan.FromMinutes(30);
        var ending = new GameClockSnapshot(
            DateTimeOffset.Parse("2026-10-15T12:00:00Z").AddHours(utcJumpHours),
            startedTimestamp + elapsed.Ticks,
            frequency,
            startedUptime + (long)elapsed.TotalMilliseconds,
            Boot);

        var ok = MonotonicElapsedTime.TryCalculateElapsedTicks(
            startedTimestamp, frequency, startedUptime, Boot, ending, out var elapsedTicks);

        Assert.True(ok);
        Assert.Equal(elapsed.Ticks, elapsedTicks);
    }

    [Fact]
    public void Boot_identity_change_or_missing_identity_fails_closed()
    {
        var ending = new GameClockSnapshot(
            DateTimeOffset.UtcNow,
            10_000L + TimeSpan.FromMinutes(5).Ticks,
            TimeSpan.TicksPerSecond,
            10_000L + (long)TimeSpan.FromMinutes(5).TotalMilliseconds,
            Guid.NewGuid());

        Assert.False(MonotonicElapsedTime.TryCalculateElapsedTicks(
            10_000L, TimeSpan.TicksPerSecond, 10_000L, Boot, ending, out _));

        Assert.False(MonotonicElapsedTime.TryCalculateElapsedTicks(
            10_000L, TimeSpan.TicksPerSecond, 10_000L, Guid.Empty,
            ending with { BootIdentifier = Boot }, out _));
    }

    [Fact]
    public void Frequency_counter_or_uptime_discontinuity_fails_closed()
    {
        Assert.False(MonotonicElapsedTime.TryCalculateElapsedTicks(
            10_000L, TimeSpan.TicksPerSecond, 10_000L, Boot,
            new GameClockSnapshot(DateTimeOffset.UtcNow, 10_001L, 2 * TimeSpan.TicksPerSecond,
                10_001L, Boot), out _));

        Assert.False(MonotonicElapsedTime.TryCalculateElapsedTicks(
            10_000L, TimeSpan.TicksPerSecond, 10_000L, Boot,
            new GameClockSnapshot(DateTimeOffset.UtcNow, 10_000L,
                TimeSpan.TicksPerSecond, 10_000L, Boot), out _));

        Assert.False(MonotonicElapsedTime.TryCalculateElapsedTicks(
            10_000L, TimeSpan.TicksPerSecond, 100_000L, Boot,
            new GameClockSnapshot(DateTimeOffset.UtcNow, 10_000L + TimeSpan.TicksPerHour,
                TimeSpan.TicksPerSecond, 100_000L + 60_000, Boot), out _));
    }
}
