using GameNet.Server.Modules.Sessions.Application;
using GameNet.Server.Modules.Sessions.Domain;
using GameNet.Server.Modules.Stations.Domain;
using GameNet.Server.Modules.Customers.Domain;
using GameNet.Server.Modules.Tariffs.Domain;
using GameNet.Server.Persistence;
using GameNet.Shared.Contracts.V1.Security;
using GameNet.Shared.Contracts.V1.Sessions;
using GameNet.Shared.Contracts.V1.Stations;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GameNet.Server.UnitTests.Modules.Sessions;

public sealed partial class SessionWorkflowPostgresTests
{
    [Theory]
    [InlineData(StationType.Pc, StationTypeContract.Pc, false)]
    [InlineData(StationType.Ps5, StationTypeContract.Ps5, false)]
    [InlineData(StationType.Foosball, StationTypeContract.Foosball, true)]
    public async Task Session_transfer_preserves_lifecycle_tariff_and_timing_and_replays_idempotently(
        StationType stationType,
        StationTypeContract tariffType,
        bool startPaused)
    {
        if (!IsExplicitlyEnabled()) return;

        var connectionString = GetConnectionString();
        var token = Guid.NewGuid().ToString("N");
        var operatorId = Guid.NewGuid();
        var sourceStationId = Guid.NewGuid();
        var destinationStationId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var tariffId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var correlation = "transfer-ok-" + token;
        var idempotencyPrefix = "transfer-ok-" + token + "-";
        var key = idempotencyPrefix + "move";
        var clock = new MutableClock(DateTimeOffset.UtcNow.AddHours(-1));

        try
        {
            await using var db = CreateContext(connectionString);
            var source = Station.Create(sourceStationId, "TRS-" + token[..10], "Transfer source", stationType);
            var destination = Station.Create(destinationStationId, "TRD-" + token[..10], "Transfer target", stationType);
            var tariff = Tariff.CreateFixedHourly(
                tariffId, "TRF-" + token[..10], "Transfer tariff", tariffType, 120_000L, false, clock.UtcNow);
            var customer = Customer.CreateWalkIn(customerId, clock.UtcNow);
            var snapshot = clock.Capture();
            var session = Session.Start(
                sessionId, sourceStationId, customerId, tariffId, tariff.Code, tariff.Name,
                tariff.HourlyRateToman, operatorId, snapshot.UtcNow, snapshot.Timestamp,
                snapshot.TimestampFrequency, snapshot.SystemUptimeMilliseconds, snapshot.BootIdentifier);
            if (startPaused)
                session.Pause(operatorId, TimeSpan.FromMinutes(7).Ticks);

            db.Stations.AddRange(source, destination);
            db.Tariffs.Add(tariff);
            db.Customers.Add(customer);
            db.Sessions.Add(session);
            if (stationType == StationType.Pc)
            {
                await BindActiveAgentAsync(db, source, clock, "src-" + token[..9]);
                await BindActiveAgentAsync(db, destination, clock, "dst-" + token[..9]);
            }
            else
            {
                await db.SaveChangesAsync();
            }

            var originalVersion = session.Version;
            var originalStatus = session.Status;
            var originalAccumulatedTicks = session.AccumulatedElapsedTicks;
            var originalSegmentTimestamp = session.CurrentSegmentStartedMonotonicTimestamp;
            var originalSegmentFrequency = session.CurrentSegmentStartedMonotonicFrequency;
            var originalSegmentUptime = session.CurrentSegmentStartedSystemUptimeMilliseconds;
            var originalSegmentBoot = session.CurrentSegmentStartedBootIdentifier;
            var originalRate = session.TariffHourlyRateTomanSnapshot;
            var reason = "Customer requested a different station";
            var command = new TransferSessionCommand(
                sessionId, destinationStationId, originalVersion, reason, true,
                key, Actor(operatorId, correlation));
            var service = CreateService(db, clock);

            var moved = await service.TransferAsync(command, CancellationToken.None);
            Assert.True(moved.IsSuccess, moved.IsSuccess ? "" : moved.Error.Code);
            Assert.Equal(destinationStationId, moved.Value.StationId);
            Assert.Equal(originalVersion + 1, moved.Value.Version);
            Assert.Equal(originalStatus == SessionStatus.Paused ? SessionStatusContract.Paused : SessionStatusContract.Active, moved.Value.Status);

            var replay = await service.TransferAsync(command, CancellationToken.None);
            Assert.True(replay.IsSuccess);
            Assert.Equal(moved.Value, replay.Value);

            var persisted = await db.Sessions.AsNoTracking().SingleAsync(x => x.Id == sessionId);
            Assert.Equal(destinationStationId, persisted.StationId);
            Assert.Equal(originalStatus, persisted.Status);
            Assert.Equal(originalAccumulatedTicks, persisted.AccumulatedElapsedTicks);
            Assert.Equal(originalSegmentTimestamp, persisted.CurrentSegmentStartedMonotonicTimestamp);
            Assert.Equal(originalSegmentFrequency, persisted.CurrentSegmentStartedMonotonicFrequency);
            Assert.Equal(originalSegmentUptime, persisted.CurrentSegmentStartedSystemUptimeMilliseconds);
            Assert.Equal(originalSegmentBoot, persisted.CurrentSegmentStartedBootIdentifier);
            Assert.Equal(originalRate, persisted.TariffHourlyRateTomanSnapshot);
            Assert.Equal(0, await db.Sessions.CountAsync(x => x.StationId == sourceStationId));
            Assert.Equal(1, await db.Sessions.CountAsync(x => x.StationId == destinationStationId));
            Assert.Equal(0, await db.Invoices.CountAsync(x => x.SessionId == sessionId));

            var audit = await db.AuditEntries.AsNoTracking()
                .SingleAsync(x => x.CorrelationId == correlation && x.Operation == "sessions.transfer");
            Assert.Equal(reason, audit.Reason);
            Assert.Contains(sourceStationId.ToString("D"), audit.BeforeJson ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(destinationStationId.ToString("D"), audit.AfterJson ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(reason, audit.AfterJson ?? string.Empty, StringComparison.Ordinal);
        }
        finally
        {
            await CleanupTransferAsync(
                connectionString, [sessionId], [customerId], sourceStationId, destinationStationId,
                tariffId, correlation, idempotencyPrefix);
        }
    }

    [Fact]
    public async Task Session_transfer_rejects_cross_type_and_busy_destination_without_mutation()
    {
        if (!IsExplicitlyEnabled()) return;

        var connectionString = GetConnectionString();
        var token = Guid.NewGuid().ToString("N");
        var operatorId = Guid.NewGuid();
        var sourceStationId = Guid.NewGuid();
        var destinationStationId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var tariffId = Guid.NewGuid();
        var correlation = "transfer-mismatch-" + token;
        var idempotencyPrefix = "transfer-mismatch-" + token + "-";
        var clock = new MutableClock(DateTimeOffset.UtcNow.AddHours(-1));

        try
        {
            await using var db = CreateContext(connectionString);
            db.Stations.Add(Station.Create(sourceStationId, "TMS-" + token[..10], "PS5 source", StationType.Ps5));
            db.Stations.Add(Station.Create(destinationStationId, "TMF-" + token[..10], "Foosball target", StationType.Foosball));
            var tariff = Tariff.CreateFixedHourly(
                tariffId, "TMT-" + token[..10], "PS5 transfer tariff",
                StationTypeContract.Ps5, 120_000L, false, clock.UtcNow);
            db.Tariffs.Add(tariff);
            db.Customers.Add(Customer.CreateWalkIn(customerId, clock.UtcNow));
            var snapshot = clock.Capture();
            db.Sessions.Add(Session.Start(
                sessionId, sourceStationId, customerId, tariffId, tariff.Code, tariff.Name,
                tariff.HourlyRateToman, operatorId, snapshot.UtcNow, snapshot.Timestamp,
                snapshot.TimestampFrequency, snapshot.SystemUptimeMilliseconds, snapshot.BootIdentifier));
            await db.SaveChangesAsync();

            var result = await CreateService(db, clock).TransferAsync(
                new TransferSessionCommand(
                    sessionId, destinationStationId, 1, "Customer requested a different station", true,
                    idempotencyPrefix + "move", Actor(operatorId, correlation)),
                CancellationToken.None);

            Assert.False(result.IsSuccess);
            Assert.Equal("sessions.transfer_type_mismatch", result.Error.Code);
            Assert.Equal(sourceStationId, (await db.Sessions.AsNoTracking().SingleAsync(x => x.Id == sessionId)).StationId);
            Assert.Empty(await db.AuditEntries.AsNoTracking().Where(x => x.CorrelationId == correlation).ToArrayAsync());
        }
        finally
        {
            await CleanupTransferAsync(
                connectionString, [sessionId], [customerId], sourceStationId, destinationStationId,
                tariffId, correlation, idempotencyPrefix);
        }
    }

    [Fact]
    public async Task Session_transfer_rejects_pc_destination_without_fresh_authoritative_agent()
    {
        if (!IsExplicitlyEnabled()) return;

        var connectionString = GetConnectionString();
        var token = Guid.NewGuid().ToString("N");
        var operatorId = Guid.NewGuid();
        var sourceStationId = Guid.NewGuid();
        var destinationStationId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var tariffId = Guid.NewGuid();
        var correlation = "transfer-agent-" + token;
        var idempotencyPrefix = "transfer-agent-" + token + "-";
        var clock = new MutableClock(DateTimeOffset.UtcNow.AddHours(-1));

        try
        {
            await using var db = CreateContext(connectionString);
            var source = Station.Create(sourceStationId, "TPS-" + token[..10], "PC source", StationType.Pc);
            db.Stations.Add(source);
            db.Stations.Add(Station.Create(destinationStationId, "TPD-" + token[..10], "PC target", StationType.Pc));
            var tariff = Tariff.CreateFixedHourly(
                tariffId, "TPC-" + token[..10], "PC transfer tariff",
                StationTypeContract.Pc, 60_000L, false, clock.UtcNow);
            db.Tariffs.Add(tariff);
            db.Customers.Add(Customer.CreateWalkIn(customerId, clock.UtcNow));
            var snapshot = clock.Capture();
            db.Sessions.Add(Session.Start(
                sessionId, sourceStationId, customerId, tariffId, tariff.Code, tariff.Name,
                tariff.HourlyRateToman, operatorId, snapshot.UtcNow, snapshot.Timestamp,
                snapshot.TimestampFrequency, snapshot.SystemUptimeMilliseconds, snapshot.BootIdentifier));
            await BindActiveAgentAsync(db, source, clock, "src-" + token[..9]);

            var result = await CreateService(db, clock).TransferAsync(
                new TransferSessionCommand(
                    sessionId, destinationStationId, 1, "Customer requested another PC", true,
                    idempotencyPrefix + "move", Actor(operatorId, correlation)),
                CancellationToken.None);

            Assert.False(result.IsSuccess);
            Assert.Equal("sessions.transfer_agent_unavailable", result.Error.Code);
            Assert.Equal(sourceStationId, (await db.Sessions.AsNoTracking().SingleAsync(x => x.Id == sessionId)).StationId);
        }
        finally
        {
            await CleanupTransferAsync(
                connectionString, [sessionId], [customerId], sourceStationId, destinationStationId,
                tariffId, correlation, idempotencyPrefix);
        }
    }

    [Fact]
    public async Task Session_transfer_rejects_a_destination_with_an_unsettled_session()
    {
        if (!IsExplicitlyEnabled()) return;

        var connectionString = GetConnectionString();
        var token = Guid.NewGuid().ToString("N");
        var operatorId = Guid.NewGuid();
        var sourceStationId = Guid.NewGuid();
        var destinationStationId = Guid.NewGuid();
        var sourceSessionId = Guid.NewGuid();
        var destinationSessionId = Guid.NewGuid();
        var sourceCustomerId = Guid.NewGuid();
        var destinationCustomerId = Guid.NewGuid();
        var tariffId = Guid.NewGuid();
        var correlation = "transfer-occupied-" + token;
        var idempotencyPrefix = "transfer-occupied-" + token + "-";
        var clock = new MutableClock(DateTimeOffset.UtcNow.AddHours(-1));

        try
        {
            await using var db = CreateContext(connectionString);
            db.Stations.Add(Station.Create(sourceStationId, "TOS-" + token[..10], "Transfer source", StationType.Ps5));
            db.Stations.Add(Station.Create(destinationStationId, "TOT-" + token[..10], "Occupied target", StationType.Ps5));
            var tariff = Tariff.CreateFixedHourly(
                tariffId, "TOTF-" + token[..10], "PS5 transfer tariff",
                StationTypeContract.Ps5, 120_000L, false, clock.UtcNow);
            db.Tariffs.Add(tariff);
            db.Customers.Add(Customer.CreateWalkIn(sourceCustomerId, clock.UtcNow));
            db.Customers.Add(Customer.CreateWalkIn(destinationCustomerId, clock.UtcNow));
            var snapshot = clock.Capture();
            db.Sessions.Add(Session.Start(
                sourceSessionId, sourceStationId, sourceCustomerId, tariffId, tariff.Code, tariff.Name,
                tariff.HourlyRateToman, operatorId, snapshot.UtcNow, snapshot.Timestamp,
                snapshot.TimestampFrequency, snapshot.SystemUptimeMilliseconds, snapshot.BootIdentifier));
            db.Sessions.Add(Session.Start(
                destinationSessionId, destinationStationId, destinationCustomerId, tariffId, tariff.Code, tariff.Name,
                tariff.HourlyRateToman, operatorId, snapshot.UtcNow, snapshot.Timestamp,
                snapshot.TimestampFrequency, snapshot.SystemUptimeMilliseconds, snapshot.BootIdentifier));
            await db.SaveChangesAsync();

            var result = await CreateService(db, clock).TransferAsync(
                new TransferSessionCommand(
                    sourceSessionId, destinationStationId, 1, "Customer requested another console", true,
                    idempotencyPrefix + "move", Actor(operatorId, correlation)),
                CancellationToken.None);

            Assert.False(result.IsSuccess);
            Assert.Equal("sessions.transfer_destination_occupied", result.Error.Code);
            Assert.Equal(sourceStationId, (await db.Sessions.AsNoTracking().SingleAsync(x => x.Id == sourceSessionId)).StationId);
            Assert.Equal(destinationStationId, (await db.Sessions.AsNoTracking().SingleAsync(x => x.Id == destinationSessionId)).StationId);
            Assert.Empty(await db.AuditEntries.AsNoTracking().Where(x => x.CorrelationId == correlation).ToArrayAsync());
        }
        finally
        {
            await CleanupTransferAsync(
                connectionString, [sourceSessionId, destinationSessionId],
                [sourceCustomerId, destinationCustomerId], sourceStationId, destinationStationId,
                tariffId, correlation, idempotencyPrefix);
        }
    }

    private static async Task CleanupTransferAsync(
        string connectionString,
        Guid[] sessionIds,
        Guid[] customerIds,
        Guid sourceStationId,
        Guid destinationStationId,
        Guid tariffId,
        string correlation,
        string idempotencyPrefix)
    {
        await using var db = CreateContext(connectionString);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM audit_entries WHERE correlation_id = {correlation}");
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM billing_ledger_entries WHERE invoice_id IN (SELECT id FROM invoices WHERE station_id IN ({sourceStationId}, {destinationStationId}))");
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM cash_sale_reversals WHERE invoice_id IN (SELECT id FROM invoices WHERE station_id IN ({sourceStationId}, {destinationStationId}))");
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM cash_tender_corrections WHERE invoice_id IN (SELECT id FROM invoices WHERE station_id IN ({sourceStationId}, {destinationStationId}))");
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM payments WHERE invoice_id IN (SELECT id FROM invoices WHERE station_id IN ({sourceStationId}, {destinationStationId}))");
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM invoices WHERE station_id IN ({sourceStationId}, {destinationStationId})");
        foreach (var sessionId in sessionIds)
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM sessions WHERE id = {sessionId}");
        foreach (var customerId in customerIds)
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM customers WHERE id = {customerId}");
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM agent_connection_leases WHERE device_id IN (SELECT agent_device_id FROM stations WHERE id IN ({sourceStationId}, {destinationStationId}))");
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM agent_credentials WHERE device_id IN (SELECT agent_device_id FROM stations WHERE id IN ({sourceStationId}, {destinationStationId}))");
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM tariffs WHERE id = {tariffId}");
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM stations WHERE id IN ({sourceStationId}, {destinationStationId})");
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM idempotency_records WHERE key LIKE {idempotencyPrefix + "%"}");
    }

}
