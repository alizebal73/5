using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GameNet.Server.Infrastructure.Audit;
using GameNet.Server.Infrastructure.Idempotency;
using GameNet.Server.Infrastructure.Security;
using GameNet.Server.Infrastructure.Transactions;
using GameNet.Server.Modules.Billing.Application;
using GameNet.Server.Modules.Billing.Domain;
using GameNet.Server.Modules.Billing.Infrastructure;
using GameNet.Server.Modules.Customers.Domain;
using GameNet.Server.Modules.Customers.Infrastructure;
using GameNet.Server.Modules.Stations.Infrastructure;
using GameNet.Server.Modules.Tariffs.Infrastructure;
using GameNet.Server.Modules.Sessions.Infrastructure;
using GameNet.Server.Modules.Sessions.Application;
using GameNet.Server.Modules.Sessions.Domain;
using GameNet.Server.Modules.Stations.Domain;
using GameNet.Server.Modules.Tariffs.Domain;
using GameNet.Server.Persistence;
using GameNet.Shared.Contracts.V1.Security;
using GameNet.Shared.Contracts.V1.Sessions;
using GameNet.Shared.Contracts.V1.Stations;
using GameNet.Shared.Primitives;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GameNet.Server.UnitTests.Modules.Sessions;

/// <summary>
/// PostgreSQL-backed end-to-end service tests. The Foundation script opts in explicitly
/// and points these tests only at its disposable PostgreSQL database.
/// </summary>
[Collection("Session workflow PostgreSQL serial")]
public sealed class SessionWorkflowPostgresTests
{
    [Fact]
    public async Task Pc_session_replay_end_invoice_and_cash_checkout_are_atomic()
    {
        if (!IsExplicitlyEnabled()) return;

        var connectionString = GetConnectionString();
        var token = Guid.NewGuid().ToString("N");
        var operatorId = Guid.NewGuid();
        var stationId = Guid.NewGuid();
        var tariffId = Guid.NewGuid();
        var correlation = "slice1-pg-" + token;
        var idempotencyPrefix = "slice1-pg-" + token + "-";
        var startKey = idempotencyPrefix + "start";
        var endKey = idempotencyPrefix + "end";
        var checkoutKey = idempotencyPrefix + "cash";
        var clock = new MutableClock(DateTimeOffset.UtcNow.AddHours(-1));
        Guid? customerId = null;

        try
        {
            await using (var db = CreateContext(connectionString))
            {
                var station = Station.Create(stationId, "PG-" + token[..12], "Slice 1 PostgreSQL PC", StationType.Pc);
                var tariff = Tariff.CreateFixedHourly(
                    tariffId, "PG-" + token[..12], "Slice 1 PostgreSQL tariff",
                    StationTypeContract.Pc, 60_000L, false, clock.UtcNow);
                db.Stations.Add(station);
                db.Tariffs.Add(tariff);
                await db.SaveChangesAsync();

                var service = CreateService(db, clock);
                var actor = Actor(operatorId, correlation);
                var billingActor = BillingActor(operatorId, correlation);
                var billing = CreateBillingService(db, clock);
                var startCommand = new StartSessionCommand(stationId, tariffId, null, startKey, actor);

                var started = await service.StartAsync(startCommand, CancellationToken.None);
                Assert.True(started.IsSuccess, started.IsSuccess ? "" : started.Error.Code);
                Assert.Equal(SessionStatusContract.Active, started.Value.Status);
                Assert.Equal(("PG-" + token[..12]).ToUpperInvariant(), started.Value.TariffCodeSnapshot);
                Assert.Equal(60_000L, started.Value.TariffHourlyRateTomanSnapshot);
                customerId = started.Value.CustomerId;

                // Null CustomerId must create a persisted minimal Walk-in customer under
                // the authoritative Server clock, and a replay must not duplicate it.
                var walkIn = await db.Customers.AsNoTracking()
                    .SingleAsync(x => x.Id == customerId.Value);
                Assert.Equal(CustomerKind.WalkIn, walkIn.Kind);
                Assert.StartsWith("W-", walkIn.Code, StringComparison.Ordinal);
                Assert.Null(walkIn.DisplayName);
                Assert.True(walkIn.IsActive);
                // PostgreSQL timestamptz persists microsecond precision; compare at that
                // boundary instead of requiring .NET's finer 100-nanosecond tick precision.
                Assert.Equal(
                    clock.UtcNow.UtcDateTime.Ticks / 10,
                    walkIn.CreatedAtUtc.UtcDateTime.Ticks / 10);

                var replay = await service.StartAsync(startCommand, CancellationToken.None);
                Assert.True(replay.IsSuccess);
                Assert.Equal(started.Value.Id, replay.Value.Id);

                var changedIntent = startCommand with { TariffId = Guid.NewGuid() };
                var conflict = await service.StartAsync(changedIntent, CancellationToken.None);
                Assert.False(conflict.IsSuccess);
                Assert.Equal("idempotency.key_reused", conflict.Error.Code);

                Assert.Equal(1, await db.Sessions.CountAsync(x => x.StationId == stationId));
                Assert.Equal(1, await db.Customers.CountAsync(x => x.Id == customerId.Value));
                Assert.Equal(1, await db.IdempotencyRecords.CountAsync(x => x.Key == startKey));

                // Editing today's tariff after Session start must not reprice this Session.
                tariff.ChangeHourlyRate(120_000L);
                await db.SaveChangesAsync();
                Assert.Equal(120_000L, await db.Tariffs.AsNoTracking()
                    .Where(x => x.Id == tariffId)
                    .Select(x => x.HourlyRateToman)
                    .SingleAsync());
                Assert.Equal(60_000L, started.Value.TariffHourlyRateTomanSnapshot);

                clock.Advance(TimeSpan.FromMinutes(30));
                var endCommand = new EndSessionCommand(
                    started.Value.Id, started.Value.Version, endKey, actor);
                var ended = await service.EndAsync(endCommand, CancellationToken.None);
                Assert.True(ended.IsSuccess, ended.IsSuccess ? "" : ended.Error.Code);
                Assert.Equal(InvoiceStatusContract.PendingPayment, ended.Value.Status);
                Assert.Equal(60_000L, ended.Value.TariffHourlyRateTomanSnapshot);
                Assert.Equal(30_000L, ended.Value.AmountToman);
                Assert.Equal(started.Value.Version + 1, ended.Value.SessionVersion);

                var invoiceReplay = await service.EndAsync(endCommand, CancellationToken.None);
                Assert.True(invoiceReplay.IsSuccess);
                Assert.Equal(ended.Value.InvoiceId, invoiceReplay.Value.InvoiceId);
                Assert.Equal(ended.Value.AmountToman, invoiceReplay.Value.AmountToman);
                Assert.Equal(1, await db.Invoices.CountAsync(x => x.SessionId == started.Value.Id));

                var insufficientCheckout = await billing.CheckoutCashAsync(
                    new CashCheckoutCommand(
                        ended.Value.InvoiceId, 29_999L, ended.Value.SessionVersion,
                        idempotencyPrefix + "cash-low", billingActor),
                    CancellationToken.None);
                Assert.False(insufficientCheckout.IsSuccess);
                Assert.Equal("billing.cash_insufficient", insufficientCheckout.Error.Code);
                Assert.Equal(InvoiceStatus.PendingPayment,
                    (await db.Invoices.SingleAsync(x => x.Id == ended.Value.InvoiceId)).Status);
                Assert.Equal(0, await db.Payments.CountAsync(x => x.InvoiceId == ended.Value.InvoiceId));
                Assert.Equal(0, await db.BillingLedgerEntries.CountAsync(x => x.InvoiceId == ended.Value.InvoiceId));

                var blockedStart = await service.StartAsync(
                    new StartSessionCommand(
                        stationId, tariffId, null, idempotencyPrefix + "start-before-settlement", actor),
                    CancellationToken.None);
                Assert.False(blockedStart.IsSuccess);
                Assert.Equal("sessions.station_pending_settlement", blockedStart.Error.Code);

                var checkoutCommand = new CashCheckoutCommand(
                    ended.Value.InvoiceId, 50_000L, ended.Value.SessionVersion, checkoutKey, billingActor);
                var pendingList = await billing.ListPendingInvoicesAsync(billingActor, CancellationToken.None);
                Assert.True(pendingList.IsSuccess);
                Assert.Contains(pendingList.Value, x => x.InvoiceId == ended.Value.InvoiceId && x.SessionVersion == ended.Value.SessionVersion);

                var staleCheckout = await billing.CheckoutCashAsync(
                    new CashCheckoutCommand(
                        ended.Value.InvoiceId, 50_000L, ended.Value.SessionVersion - 1,
                        idempotencyPrefix + "cash-stale", billingActor),
                    CancellationToken.None);
                Assert.False(staleCheckout.IsSuccess);
                Assert.Equal("billing.session_version_conflict", staleCheckout.Error.Code);
                Assert.Equal(InvoiceStatus.PendingPayment,
                    (await db.Invoices.SingleAsync(x => x.Id == ended.Value.InvoiceId)).Status);
                Assert.Equal(0, await db.Payments.CountAsync(x => x.InvoiceId == ended.Value.InvoiceId));
                Assert.Equal(0, await db.BillingLedgerEntries.CountAsync(x => x.InvoiceId == ended.Value.InvoiceId));

                var checkout = await billing.CheckoutCashAsync(checkoutCommand, CancellationToken.None);
                Assert.True(checkout.IsSuccess, checkout.IsSuccess ? "" : checkout.Error.Code);
                Assert.Equal(30_000L, checkout.Value.InvoiceAmountToman);
                Assert.Equal(50_000L, checkout.Value.AmountReceivedToman);
                Assert.Equal(20_000L, checkout.Value.ChangeGivenToman);

                var paidOutcome = await billing.GetCashCheckoutOutcomeAsync(
                    billingActor, ended.Value.InvoiceId, CancellationToken.None);
                Assert.True(paidOutcome.IsSuccess, paidOutcome.IsSuccess ? "" : paidOutcome.Error.Code);
                Assert.Equal(InvoiceStatusContract.Paid, paidOutcome.Value.Status);
                Assert.Equal(checkout.Value.PaymentId, paidOutcome.Value.PaymentId);
                Assert.Equal(checkout.Value.LedgerEntryId, paidOutcome.Value.LedgerEntryId);
                Assert.Equal(50_000L, paidOutcome.Value.AmountReceivedToman);
                Assert.Equal(20_000L, paidOutcome.Value.ChangeGivenToman);

                var recentOutcomes = await billing.ListRecentCashCheckoutsAsync(billingActor, CancellationToken.None);
                Assert.True(recentOutcomes.IsSuccess, recentOutcomes.IsSuccess ? "" : recentOutcomes.Error.Code);
                Assert.Contains(recentOutcomes.Value, x =>
                    x.InvoiceId == ended.Value.InvoiceId &&
                    x.Status == InvoiceStatusContract.Paid &&
                    x.PaymentId == checkout.Value.PaymentId);

                var checkoutReplay = await billing.CheckoutCashAsync(checkoutCommand, CancellationToken.None);
                Assert.True(checkoutReplay.IsSuccess);
                Assert.Equal(checkout.Value.PaymentId, checkoutReplay.Value.PaymentId);
                Assert.Equal(checkout.Value.LedgerEntryId, checkoutReplay.Value.LedgerEntryId);

                var session = await db.Sessions.SingleAsync(x => x.Id == started.Value.Id);
                var invoice = await db.Invoices.SingleAsync(x => x.Id == ended.Value.InvoiceId);
                var payment = await db.Payments.SingleAsync(x => x.InvoiceId == invoice.Id);
                var ledger = await db.BillingLedgerEntries.SingleAsync(x => x.InvoiceId == invoice.Id);
                Assert.Equal(SessionStatus.Paid, session.Status);
                Assert.Equal(InvoiceStatus.Paid, invoice.Status);
                Assert.Equal(PaymentMethod.Cash, payment.Method);
                Assert.Equal(30_000L, payment.AmountAppliedToman);
                Assert.Equal(50_000L, payment.AmountReceivedToman);
                Assert.Equal(20_000L, payment.ChangeGivenToman);
                Assert.Equal(BillingLedgerEntryType.CashSale, ledger.EntryType);
                Assert.Equal(invoice.AmountToman, ledger.AmountToman);
                Assert.Equal(1, await db.Payments.CountAsync(x => x.InvoiceId == invoice.Id));
                Assert.Equal(1, await db.BillingLedgerEntries.CountAsync(x => x.InvoiceId == invoice.Id));

                var audits = await db.AuditEntries.AsNoTracking()
                    .Where(x => x.CorrelationId == correlation)
                    .Select(x => x.Operation)
                    .ToArrayAsync();
                Assert.Contains("sessions.start", audits);
                Assert.Contains("sessions.end", audits);
                Assert.Contains("billing.invoice_issued", audits);
                Assert.Contains("billing.cash_checkout", audits);
                Assert.Contains("billing.cash_ledger_entry", audits);
            }
        }
        finally
        {
            await CleanupAsync(connectionString, stationId, tariffId, customerId, correlation, idempotencyPrefix);
        }
    }

    [Fact]
    public async Task Concurrent_starts_cannot_create_two_active_sessions_for_one_pc()
    {
        if (!IsExplicitlyEnabled()) return;

        var connectionString = GetConnectionString();
        var token = Guid.NewGuid().ToString("N");
        var stationId = Guid.NewGuid();
        var tariffId = Guid.NewGuid();
        var operatorA = Guid.NewGuid();
        var operatorB = Guid.NewGuid();
        var correlationA = "slice1-concurrent-a-" + token;
        var correlationB = "slice1-concurrent-b-" + token;
        var idempotencyPrefix = "slice1-concurrent-" + token + "-";
        Guid? customerA = null;
        Guid? customerB = null;
        var clock = new MutableClock(DateTimeOffset.UtcNow);

        try
        {
            await using (var setup = CreateContext(connectionString))
            {
                setup.Stations.Add(Station.Create(
                    stationId, "PC-" + token[..12], "Concurrent start PC", StationType.Pc));
                setup.Tariffs.Add(Tariff.CreateFixedHourly(
                    tariffId, "TR-" + token[..12], "Concurrent start tariff",
                    StationTypeContract.Pc, 60_000L, false, clock.UtcNow));
                await setup.SaveChangesAsync();
            }

            Result<SessionResponse> first;
            Result<SessionResponse> second;
            await using (var dbA = CreateContext(connectionString))
            await using (var dbB = CreateContext(connectionString))
            {
                var serviceA = CreateService(dbA, clock);
                var serviceB = CreateService(dbB, clock);
                var requestA = new StartSessionCommand(
                    stationId, tariffId, null, idempotencyPrefix + "a", Actor(operatorA, correlationA));
                var requestB = new StartSessionCommand(
                    stationId, tariffId, null, idempotencyPrefix + "b", Actor(operatorB, correlationB));

                var results = await Task.WhenAll(
                    serviceA.StartAsync(requestA, CancellationToken.None),
                    serviceB.StartAsync(requestB, CancellationToken.None)).WaitAsync(TimeSpan.FromSeconds(30));
                first = results[0];
                second = results[1];
                if (first.IsSuccess) customerA = first.Value.CustomerId;
                if (second.IsSuccess) customerB = second.Value.CustomerId;
            }

            Assert.Single(new[] { first, second }, result => result.IsSuccess);
            var failure = first.IsSuccess ? second : first;
            Assert.False(failure.IsSuccess);
            Assert.Equal("sessions.station_has_active_session", failure.Error.Code);

            await using var verify = CreateContext(connectionString);
            Assert.Equal(1, await verify.Sessions.CountAsync(x => x.StationId == stationId && x.Status == SessionStatus.Active));
        }
        finally
        {
            await CleanupAsync(connectionString, stationId, tariffId, customerA, correlationA, idempotencyPrefix);
            await CleanupAsync(connectionString, stationId, tariffId, customerB, correlationB, idempotencyPrefix);
        }
    }

    private static BillingService CreateBillingService(GameNetDbContext db, IGameClock clock)
    {
        var audit = new EfAuditWriter(db);
        return new BillingService(
            db,
            new CommandMutationPipeline(new EfTransactionCoordinator(db), new EfIdempotencyStore(db, clock)),
            new TestRequestFingerprint(),
            new SessionSettlement(db, audit, clock),
            new SessionCheckoutAccess(db),
            audit,
            clock);
    }

    private static BillingActorContext BillingActor(Guid operatorId, string correlation) =>
        new(operatorId, correlation, "PostgreSQLTest",
            new HashSet<string>([Permissions.BillingWrite], StringComparer.Ordinal));

    private static SessionService CreateService(GameNetDbContext db, IGameClock clock)
    {
        var audit = new EfAuditWriter(db);
        return new SessionService(
            db,
            new CommandMutationPipeline(new EfTransactionCoordinator(db), new EfIdempotencyStore(db, clock)),
            new TestRequestFingerprint(),
            new CustomerSessionAccess(db),
            new StationSessionAccess(db),
            new TariffSessionAccess(db),
            new BillingInvoiceWriter(db, audit, clock),
            audit,
            clock);
    }

    private static SessionActorContext Actor(Guid operatorId, string correlation) =>
        new(operatorId, correlation, "PostgreSQLTest",
            new HashSet<string>([Permissions.SessionsOperate, Permissions.BillingWrite], StringComparer.Ordinal));

    private static GameNetDbContext CreateContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<GameNetDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        return new GameNetDbContext(options);
    }

    private static bool IsExplicitlyEnabled() =>
        string.Equals(Environment.GetEnvironmentVariable("GAMENET_SESSION_POSTGRES_TESTS"), "true", StringComparison.OrdinalIgnoreCase);

    private static string GetConnectionString()
    {
        var value = Environment.GetEnvironmentVariable("GAMENET_DATABASE_CONNECTION");
        Assert.False(string.IsNullOrWhiteSpace(value));
        return value!;
    }

    private static async Task CleanupAsync(
        string connectionString,
        Guid stationId,
        Guid tariffId,
        Guid? customerId,
        string correlation,
        string idempotencyPrefix)
    {
        await using var db = CreateContext(connectionString);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM audit_entries WHERE correlation_id = {correlation}");
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM billing_ledger_entries WHERE invoice_id IN (SELECT id FROM invoices WHERE station_id = {stationId})");
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM payments WHERE invoice_id IN (SELECT id FROM invoices WHERE station_id = {stationId})");
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM invoices WHERE station_id = {stationId}");
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM sessions WHERE station_id = {stationId}");
        if (customerId.HasValue)
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM customers WHERE id = {customerId.Value}");
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM tariffs WHERE id = {tariffId}");
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM stations WHERE id = {stationId}");
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM idempotency_records WHERE key LIKE {idempotencyPrefix + "%"}");
    }

    private sealed class MutableClock(DateTimeOffset utcNow) : IGameClock
    {
        private long timestamp;
        private long uptimeMilliseconds = 5_000;
        private static readonly Guid BootId = Guid.Parse("04ad13fd-6cb2-4b17-a54d-5e0a48f3e9f0");

        public DateTimeOffset UtcNow { get; private set; } = utcNow;

        public GameClockSnapshot Capture() => new(
            UtcNow, timestamp, TimeSpan.TicksPerSecond, uptimeMilliseconds, BootId);

        public void Advance(TimeSpan amount)
        {
            UtcNow = UtcNow.Add(amount);
            timestamp = checked(timestamp + amount.Ticks);
            uptimeMilliseconds = checked(uptimeMilliseconds + (long)amount.TotalMilliseconds);
        }

        public void JumpWallClock(TimeSpan amount) => UtcNow = UtcNow.Add(amount);
    }

    private sealed class TestRequestFingerprint : IRequestFingerprint
    {
        public string Compute(object payload) =>
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload))));
    }
}
