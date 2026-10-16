using GameNet.Server.Modules.Sessions.Domain;
using Xunit;

namespace GameNet.Server.UnitTests.Modules.Sessions;

public sealed class SessionTests
{
    private const long StartedTimestamp = 1_000_000L;
    private const long TimestampFrequency = TimeSpan.TicksPerSecond;
    private const long StartedUptimeMilliseconds = 5_000L;
    private static readonly Guid BootIdentifier = Guid.Parse("04ad13fd-6cb2-4b17-a54d-5e0a48f3e9f0");

    [Fact]
    public void Session_captures_tariff_snapshot_and_utc_start_time()
    {
        var start = new DateTimeOffset(2026, 10, 14, 15, 0, 0, TimeSpan.FromHours(3.5));
        var session = NewSession(start);

        Assert.Equal(SessionStatus.Active, session.Status);
        Assert.Equal("PC-STANDARD", session.TariffCodeSnapshot);
        Assert.Equal("PC Standard", session.TariffNameSnapshot);
        Assert.Equal(60_000L, session.TariffHourlyRateTomanSnapshot);
        Assert.Equal(start.ToUniversalTime(), session.StartedAtUtc);
        Assert.Equal(StartedTimestamp, session.StartedMonotonicTimestamp);
        Assert.Equal(TimestampFrequency, session.StartedMonotonicFrequency);
        Assert.Equal(StartedUptimeMilliseconds, session.StartedSystemUptimeMilliseconds);
        Assert.Equal(BootIdentifier, session.StartedBootIdentifier);
        Assert.Null(session.ElapsedTicks);
        Assert.Equal(1, session.Version);
    }

    [Fact]
    public void Session_moves_active_to_pending_settlement_to_paid_once()
    {
        var start = DateTimeOffset.Parse("2026-10-14T10:00:00Z");
        var session = NewSession(start);
        var endOperator = Guid.NewGuid();
        var payOperator = Guid.NewGuid();

        session.End(endOperator, start.AddMinutes(30), TimeSpan.FromMinutes(30).Ticks);

        Assert.Equal(SessionStatus.PendingSettlement, session.Status);
        Assert.Equal(start.AddMinutes(30), session.EndedAtUtc);
        Assert.Equal(TimeSpan.FromMinutes(30).Ticks, session.ElapsedTicks);
        Assert.Equal(endOperator, session.EndedByOperatorId);
        Assert.Equal(2, session.Version);
        Assert.Throws<InvalidOperationException>(() => session.End(endOperator, start.AddMinutes(31), TimeSpan.FromMinutes(31).Ticks));

        session.MarkPaid(payOperator, start.AddMinutes(31));

        Assert.Equal(SessionStatus.Paid, session.Status);
        Assert.Equal(start.AddMinutes(31), session.PaidAtUtc);
        Assert.Equal(payOperator, session.PaidByOperatorId);
        Assert.Equal(3, session.Version);
        Assert.Throws<InvalidOperationException>(() => session.MarkPaid(payOperator, start.AddMinutes(32)));
    }

    [Fact]
    public void Session_uses_persisted_elapsed_duration_when_utc_clock_moves_backwards()
    {
        var start = DateTimeOffset.Parse("2026-10-14T10:00:00Z");
        var endedAt = start.AddHours(-1);
        var session = NewSession(start);

        session.End(Guid.NewGuid(), endedAt, TimeSpan.FromMinutes(30).Ticks);
        session.MarkPaid(Guid.NewGuid(), endedAt.AddMinutes(-5));

        Assert.Equal(TimeSpan.FromMinutes(30).Ticks, session.ElapsedTicks);
        Assert.Equal(endedAt, session.EndedAtUtc);
        Assert.Equal(endedAt.AddMinutes(-5), session.PaidAtUtc);
        Assert.Equal(SessionStatus.Paid, session.Status);
    }

    [Fact]
    public void Session_rejects_invalid_elapsed_time_and_invalid_state_transitions()
    {
        var start = DateTimeOffset.Parse("2026-10-14T10:00:00Z");
        var session = NewSession(start);

        Assert.Throws<ArgumentOutOfRangeException>(() => session.End(Guid.NewGuid(), start.AddMinutes(1), 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => session.End(Guid.NewGuid(), start.AddMinutes(1), -1));
        Assert.Throws<InvalidOperationException>(() => session.MarkPaid(Guid.NewGuid(), start.AddMinutes(1)));

        session.End(Guid.NewGuid(), start.AddMinutes(1), TimeSpan.FromMinutes(1).Ticks);
        Assert.Throws<ArgumentException>(() => session.MarkPaid(Guid.Empty, start.AddMinutes(1)));
    }

    [Fact]
    public void Session_transfer_preserves_tariff_and_active_monotonic_timing_baseline()
    {
        var session = NewSession(DateTimeOffset.Parse("2026-10-14T10:00:00Z"));
        var targetStationId = Guid.NewGuid();
        var operatorId = Guid.NewGuid();
        var version = session.Version;
        var customerId = session.CustomerId;
        var tariffId = session.TariffId;
        var rate = session.TariffHourlyRateTomanSnapshot;
        var startUtc = session.StartedAtUtc;
        var segmentTimestamp = session.CurrentSegmentStartedMonotonicTimestamp;
        var segmentFrequency = session.CurrentSegmentStartedMonotonicFrequency;
        var segmentUptime = session.CurrentSegmentStartedSystemUptimeMilliseconds;
        var segmentBoot = session.CurrentSegmentStartedBootIdentifier;

        session.TransferTo(targetStationId, operatorId);

        Assert.Equal(targetStationId, session.StationId);
        Assert.Equal(SessionStatus.Active, session.Status);
        Assert.Equal(version + 1, session.Version);
        Assert.Equal(customerId, session.CustomerId);
        Assert.Equal(tariffId, session.TariffId);
        Assert.Equal(rate, session.TariffHourlyRateTomanSnapshot);
        Assert.Equal(startUtc, session.StartedAtUtc);
        Assert.Equal(0L, session.AccumulatedElapsedTicks);
        Assert.Equal(segmentTimestamp, session.CurrentSegmentStartedMonotonicTimestamp);
        Assert.Equal(segmentFrequency, session.CurrentSegmentStartedMonotonicFrequency);
        Assert.Equal(segmentUptime, session.CurrentSegmentStartedSystemUptimeMilliseconds);
        Assert.Equal(segmentBoot, session.CurrentSegmentStartedBootIdentifier);
    }

    [Fact]
    public void Paused_session_transfer_keeps_accumulated_time_and_paused_state()
    {
        var session = NewSession(DateTimeOffset.Parse("2026-10-14T10:00:00Z"));
        session.Pause(Guid.NewGuid(), TimeSpan.FromMinutes(7).Ticks);
        var targetStationId = Guid.NewGuid();
        var operatorId = Guid.NewGuid();
        var accumulated = session.AccumulatedElapsedTicks;
        var version = session.Version;

        session.TransferTo(targetStationId, operatorId);

        Assert.Equal(targetStationId, session.StationId);
        Assert.Equal(SessionStatus.Paused, session.Status);
        Assert.Equal(version + 1, session.Version);
        Assert.Equal(accumulated, session.AccumulatedElapsedTicks);
        Assert.Null(session.CurrentSegmentStartedMonotonicTimestamp);
        Assert.Null(session.CurrentSegmentStartedMonotonicFrequency);
        Assert.Null(session.CurrentSegmentStartedSystemUptimeMilliseconds);
        Assert.Null(session.CurrentSegmentStartedBootIdentifier);
    }

    [Fact]
    public void Session_transfer_rejects_same_or_empty_target_and_settled_sessions()
    {
        var session = NewSession(DateTimeOffset.Parse("2026-10-14T10:00:00Z"));
        Assert.Throws<InvalidOperationException>(() => session.TransferTo(session.StationId, Guid.NewGuid()));
        Assert.Throws<ArgumentException>(() => session.TransferTo(Guid.Empty, Guid.NewGuid()));
        Assert.Throws<ArgumentException>(() => session.TransferTo(Guid.NewGuid(), Guid.Empty));

        session.End(Guid.NewGuid(), DateTimeOffset.Parse("2026-10-14T10:30:00Z"), TimeSpan.FromMinutes(30).Ticks);
        Assert.Throws<InvalidOperationException>(() => session.TransferTo(Guid.NewGuid(), Guid.NewGuid()));
        session.MarkPaid(Guid.NewGuid(), DateTimeOffset.Parse("2026-10-14T10:31:00Z"));
        Assert.Throws<InvalidOperationException>(() => session.TransferTo(Guid.NewGuid(), Guid.NewGuid()));
    }

    private static Session NewSession(DateTimeOffset start) => Session.Start(
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
        "PC-STANDARD", "PC Standard", 60_000L, Guid.NewGuid(), start,
        StartedTimestamp, TimestampFrequency, StartedUptimeMilliseconds, BootIdentifier);
}
