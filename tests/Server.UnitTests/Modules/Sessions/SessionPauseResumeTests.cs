using GameNet.Server.Modules.Sessions.Domain;
using GameNet.Shared.Primitives;
using Xunit;

namespace GameNet.Server.UnitTests.Modules.Sessions;

public sealed class SessionPauseResumeTests
{
    private static readonly Guid BootA = Guid.Parse("04ad13fd-6cb2-4b17-a54d-5e0a48f3e9f0");
    private static readonly Guid BootB = Guid.Parse("e9a6da9d-3b2c-47e7-a90c-69cb8f1aab50");

    [Fact]
    public void Pause_resume_and_end_accumulate_only_active_monotonic_segments()
    {
        var start = DateTimeOffset.Parse("2026-10-16T10:00:00Z");
        var operatorId = Guid.NewGuid();
        var session = NewSession(start, BootA);

        session.Pause(operatorId, TimeSpan.FromMinutes(25).Ticks);

        Assert.Equal(SessionStatus.Paused, session.Status);
        Assert.Equal(TimeSpan.FromMinutes(25).Ticks, session.AccumulatedElapsedTicks);
        Assert.Null(session.CurrentSegmentStartedMonotonicTimestamp);
        Assert.Null(session.CurrentSegmentStartedMonotonicFrequency);
        Assert.Null(session.CurrentSegmentStartedSystemUptimeMilliseconds);
        Assert.Null(session.CurrentSegmentStartedBootIdentifier);
        Assert.Equal(2, session.Version);

        // Resume is a fresh timing segment. A different Windows boot is intentionally
        // valid because time spent while paused is excluded rather than reconstructed.
        var resumedAt = new GameClockSnapshot(
            start.AddHours(4), 5_000, TimeSpan.TicksPerSecond, 8_000, BootB);
        session.Resume(operatorId, resumedAt);

        Assert.Equal(SessionStatus.Active, session.Status);
        Assert.Equal(TimeSpan.FromMinutes(25).Ticks, session.AccumulatedElapsedTicks);
        Assert.Equal(5_000L, session.CurrentSegmentStartedMonotonicTimestamp);
        Assert.Equal(TimeSpan.TicksPerSecond, session.CurrentSegmentStartedMonotonicFrequency);
        Assert.Equal(8_000L, session.CurrentSegmentStartedSystemUptimeMilliseconds);
        Assert.Equal(BootB, session.CurrentSegmentStartedBootIdentifier);
        Assert.Equal(3, session.Version);

        // The service supplies accumulated 25 minutes plus the independently measured
        // 15-minute active segment, never the four hours of paused wall-clock time.
        var billableTicks = checked(session.AccumulatedElapsedTicks + TimeSpan.FromMinutes(15).Ticks);
        session.End(operatorId, resumedAt.UtcNow.AddMinutes(15), billableTicks);

        Assert.Equal(SessionStatus.PendingSettlement, session.Status);
        Assert.Equal(TimeSpan.FromMinutes(40).Ticks, session.ElapsedTicks);
        Assert.Equal(TimeSpan.FromMinutes(40).Ticks, session.AccumulatedElapsedTicks);
        Assert.Null(session.CurrentSegmentStartedMonotonicTimestamp);
        Assert.Equal(4, session.Version);
    }

    [Fact]
    public void Ending_while_paused_uses_only_segments_already_accumulated()
    {
        var start = DateTimeOffset.Parse("2026-10-16T10:00:00Z");
        var operatorId = Guid.NewGuid();
        var session = NewSession(start, BootA);

        session.Pause(operatorId, TimeSpan.FromMinutes(17).Ticks);
        var pausedAt = start.AddMinutes(17);
        var alreadyBilled = session.AccumulatedElapsedTicks;

        // The caller can end a paused Session without consulting a possibly changed
        // monotonic clock. The long interval after pause must not enter elapsedTicks.
        session.End(operatorId, pausedAt.AddHours(6), alreadyBilled);

        Assert.Equal(SessionStatus.PendingSettlement, session.Status);
        Assert.Equal(TimeSpan.FromMinutes(17).Ticks, session.ElapsedTicks);
        Assert.Equal(TimeSpan.FromMinutes(17).Ticks, session.AccumulatedElapsedTicks);
        Assert.Equal(pausedAt.AddHours(6), session.EndedAtUtc);
        Assert.Null(session.CurrentSegmentStartedMonotonicTimestamp);
    }

    [Fact]
    public void Wrong_state_pause_resume_and_underreported_end_are_rejected()
    {
        var start = DateTimeOffset.Parse("2026-10-16T10:00:00Z");
        var operatorId = Guid.NewGuid();
        var session = NewSession(start, BootA);

        Assert.Throws<InvalidOperationException>(() => session.Resume(operatorId,
            new GameClockSnapshot(start, 10_000, TimeSpan.TicksPerSecond, 6_000, BootA)));

        session.Pause(operatorId, TimeSpan.FromMinutes(10).Ticks);
        Assert.Throws<InvalidOperationException>(() => session.Pause(operatorId, TimeSpan.FromMinutes(1).Ticks));
        Assert.Throws<ArgumentOutOfRangeException>(() => session.End(
            operatorId, start.AddMinutes(20), TimeSpan.FromMinutes(9).Ticks));

        session.Resume(operatorId,
            new GameClockSnapshot(start.AddHours(1), 20_000, TimeSpan.TicksPerSecond, 6_000, BootB));
        Assert.Throws<InvalidOperationException>(() => session.Resume(operatorId,
            new GameClockSnapshot(start.AddHours(1), 30_000, TimeSpan.TicksPerSecond, 7_000, BootB)));
    }

    private static Session NewSession(DateTimeOffset start, Guid bootId) => Session.Start(
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
        "PC-STANDARD", "PC Standard", 60_000L, Guid.NewGuid(), start,
        1_000, TimeSpan.TicksPerSecond, 5_000, bootId);
}
