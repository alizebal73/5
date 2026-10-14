using GameNet.Server.Modules.Sessions.Domain;
using Xunit;

namespace GameNet.Server.UnitTests.Modules.Sessions;

public sealed class SessionTests
{
    [Fact]
    public void Session_captures_tariff_snapshot_and_normalizes_all_lifecycle_times_to_utc()
    {
        var start = new DateTimeOffset(2026, 10, 14, 15, 0, 0, TimeSpan.FromHours(3.5));
        var session = Session.Start(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            " pc-standard ", " PC Standard ", 60_000L, Guid.NewGuid(), start);

        Assert.Equal(SessionStatus.Active, session.Status);
        Assert.Equal("PC-STANDARD", session.TariffCodeSnapshot);
        Assert.Equal("PC Standard", session.TariffNameSnapshot);
        Assert.Equal(60_000L, session.TariffHourlyRateTomanSnapshot);
        Assert.Equal(start.ToUniversalTime(), session.StartedAtUtc);
        Assert.Equal(1, session.Version);
    }

    [Fact]
    public void Session_moves_active_to_pending_settlement_to_paid_once()
    {
        var start = DateTimeOffset.Parse("2026-10-14T10:00:00Z");
        var session = Session.Start(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "PC-1", "PC standard", 60_000L, Guid.NewGuid(), start);
        var endOperator = Guid.NewGuid();
        var payOperator = Guid.NewGuid();

        session.End(endOperator, start.AddMinutes(30));

        Assert.Equal(SessionStatus.PendingSettlement, session.Status);
        Assert.Equal(start.AddMinutes(30), session.EndedAtUtc);
        Assert.Equal(endOperator, session.EndedByOperatorId);
        Assert.Equal(2, session.Version);
        Assert.Throws<InvalidOperationException>(() => session.End(endOperator, start.AddMinutes(31)));

        session.MarkPaid(payOperator, start.AddMinutes(31));

        Assert.Equal(SessionStatus.Paid, session.Status);
        Assert.Equal(start.AddMinutes(31), session.PaidAtUtc);
        Assert.Equal(payOperator, session.PaidByOperatorId);
        Assert.Equal(3, session.Version);
        Assert.Throws<InvalidOperationException>(() => session.MarkPaid(payOperator, start.AddMinutes(32)));
    }

    [Fact]
    public void Session_rejects_non_monotonic_time_and_payment_before_end()
    {
        var start = DateTimeOffset.Parse("2026-10-14T10:00:00Z");
        var session = Session.Start(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "PC-1", "PC standard", 60_000L, Guid.NewGuid(), start);

        Assert.Throws<ArgumentOutOfRangeException>(() => session.End(Guid.NewGuid(), start));
        Assert.Throws<InvalidOperationException>(() => session.MarkPaid(Guid.NewGuid(), start.AddMinutes(1)));

        session.End(Guid.NewGuid(), start.AddMinutes(1));
        Assert.Throws<ArgumentOutOfRangeException>(() => session.MarkPaid(Guid.NewGuid(), start));
    }
}
