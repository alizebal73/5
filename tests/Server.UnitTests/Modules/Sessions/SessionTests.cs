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
    public void Customer_started_prepaid_session_never_uses_customer_id_as_operator()
    {
        var start = DateTimeOffset.Parse("2026-10-14T10:00:00Z");
        var customerId = Guid.NewGuid();
        var session = Session.Start(
            Guid.NewGuid(), Guid.NewGuid(), customerId, Guid.NewGuid(),
            "PC-STANDARD", "PC Standard", 60_000L, Guid.Empty, start,
            StartedTimestamp, TimestampFrequency, StartedUptimeMilliseconds, BootIdentifier, customerId);

        Assert.Equal(Guid.Empty, session.StartedByOperatorId);
        Assert.Equal(customerId, session.StartedByCustomerId);

        session.End(Guid.Empty, start.AddMinutes(60), TimeSpan.FromHours(1).Ticks, endedBySystem: true);
        Assert.True(session.EndedBySystem);
        Assert.Equal(Guid.Empty, session.EndedByOperatorId);

        session.MarkPaid(Guid.Empty, start.AddMinutes(60), customerId);
        Assert.Equal(customerId, session.PaidByCustomerId);
        Assert.Equal(Guid.Empty, session.PaidByOperatorId);
    }

    [Fact]
    public void Session_rejects_mixed_or_missing_actor_identity()
    {
        var start = DateTimeOffset.Parse("2026-10-14T10:00:00Z");
        var args = new object[] { };
        Assert.Throws<ArgumentException>(() => Session.Start(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "PC-STANDARD", "PC Standard", 60_000L, Guid.Empty, start,
            StartedTimestamp, TimestampFrequency, StartedUptimeMilliseconds, BootIdentifier));
        Assert.Throws<ArgumentException>(() => Session.Start(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "PC-STANDARD", "PC Standard", 60_000L, Guid.NewGuid(), start,
            StartedTimestamp, TimestampFrequency, StartedUptimeMilliseconds, BootIdentifier, Guid.NewGuid()));

        var session = NewSession(start);
        Assert.Throws<ArgumentException>(() => session.End(Guid.NewGuid(), start.AddMinutes(1),
            TimeSpan.FromMinutes(1).Ticks, endedBySystem: true));
        Assert.Throws<ArgumentException>(() => session.End(Guid.Empty, start.AddMinutes(1),
            TimeSpan.FromMinutes(1).Ticks));
        Assert.Throws<ArgumentException>(() => session.MarkPaid(Guid.Empty, start.AddMinutes(1)));
    }

    private static Session NewSession(DateTimeOffset start) => Session.Start(
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
        "PC-STANDARD", "PC Standard", 60_000L, Guid.NewGuid(), start,
        StartedTimestamp, TimestampFrequency, StartedUptimeMilliseconds, BootIdentifier);
}
