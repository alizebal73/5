using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GameNet.Server.Infrastructure.Audit;
using GameNet.Server.Infrastructure.Idempotency;
using GameNet.Server.Infrastructure.Transactions;
using GameNet.Server.Modules.Billing.Infrastructure;
using GameNet.Server.Modules.Customers.Domain;
using GameNet.Server.Modules.Customers.Infrastructure;
using GameNet.Server.Modules.Wallet.Application;
using GameNet.Server.Modules.Wallet.Infrastructure;
using GameNet.Server.Persistence;
using GameNet.Shared.Contracts.V1.Security;
using GameNet.Shared.Primitives;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GameNet.Server.UnitTests.Modules.Wallet;

public sealed class WalletTopUpPostgresTests
{
    [Fact]
    public async Task Cash_top_up_records_actual_receipt_wallet_ledger_balance_audit_and_idempotent_replay()
    {
        if (!IsPostgresEnabled()) return;
        var connectionString = Environment.GetEnvironmentVariable("GAMENET_DATABASE_CONNECTION");
        Assert.False(string.IsNullOrWhiteSpace(connectionString));

        var token = Guid.NewGuid().ToString("N");
        var customerId = Guid.NewGuid();
        var operatorId = Guid.NewGuid();
        var correlation = "wallet-pg-" + token;
        var customerCode = "W-" + token[..24].ToUpperInvariant();
        var idempotencyKey = "wallet-cash-topup-" + token;
        var scope = "wallet.cash-top-up:" + operatorId.ToString("D");
        var clock = new TestClock();

        try
        {
            await using (var setup = CreateContext(connectionString!))
            {
                setup.Customers.Add(Customer.CreateRegistered(
                    customerId, customerCode, "Wallet Test Customer", clock.UtcNow));
                await setup.SaveChangesAsync();
            }

            await using var db = CreateContext(connectionString!);
            var service = CreateService(db, clock);
            var actor = CreateActor(operatorId, correlation);
            var command = new WalletCashTopUpCommand(customerId, 105_000L, 110_000L, idempotencyKey, actor);

            var created = await service.TopUpCashAsync(command, CancellationToken.None);
            Assert.True(created.IsSuccess, created.IsSuccess ? "" : created.Error.Code);
            Assert.Equal(105_000L, created.Value.AmountCreditedToman);
            Assert.Equal(110_000L, created.Value.AmountReceivedToman);
            Assert.Equal(5_000L, created.Value.ChangeGivenToman);
            Assert.Equal(105_000L, created.Value.PurchasedBalanceToman);

            var replay = await service.TopUpCashAsync(command, CancellationToken.None);
            Assert.True(replay.IsSuccess, replay.IsSuccess ? "" : replay.Error.Code);
            Assert.Equal(created.Value.CashReceiptId, replay.Value.CashReceiptId);
            Assert.Equal(created.Value.WalletLedgerEntryId, replay.Value.WalletLedgerEntryId);

            var conflicting = await service.TopUpCashAsync(command with { AmountToCreditToman = 100_000L }, CancellationToken.None);
            Assert.False(conflicting.IsSuccess);
            Assert.Equal("idempotency.key_reused", conflicting.Error.Code);

            var balance = await db.WalletAccounts.AsNoTracking().SingleAsync(x => x.CustomerId == customerId);
            var ledgerRows = await db.WalletLedgerEntries.AsNoTracking().Where(x => x.CustomerId == customerId).ToArrayAsync();
            var receipts = await db.CashReceipts.AsNoTracking().Where(x => x.CustomerId == customerId).ToArrayAsync();
            Assert.Equal(105_000L, balance.PurchasedBalanceToman);
            Assert.Single(ledgerRows);
            Assert.Single(receipts);
            Assert.Equal(105_000L, ledgerRows.Sum(x => x.GetSignedAmountToman()));
            Assert.Equal(105_000L, receipts[0].AmountAppliedToman);
            Assert.Equal(110_000L, receipts[0].AmountReceivedToman);
            Assert.Equal(5_000L, receipts[0].ChangeGivenToman);
            Assert.Equal(created.Value.WalletLedgerEntryId, ledgerRows[0].Id);
            Assert.Equal(created.Value.CashReceiptId, receipts[0].Id);

            var auditRows = await db.AuditEntries.AsNoTracking()
                .Where(x => x.CorrelationId == correlation && x.Operation == "wallet.cash-top-up").ToArrayAsync();
            Assert.Single(auditRows);
            Assert.Equal(created.Value.WalletLedgerEntryId.ToString("D"), auditRows[0].ReferenceId);
            Assert.Contains("105000", auditRows[0].AfterJson ?? "", StringComparison.Ordinal);

            var unauthorized = await service.TopUpCashAsync(
                command with { IdempotencyKey = "wallet-no-permission-" + token,
                    Actor = new WalletActorContext(operatorId, correlation, "PostgreSQLTest", new HashSet<string>(StringComparer.Ordinal)) },
                CancellationToken.None);
            Assert.False(unauthorized.IsSuccess);
            Assert.Equal("wallet.forbidden", unauthorized.Error.Code);
        }
        finally
        {
            await using var cleanup = CreateContext(connectionString!);
            var keyPattern = "%" + token + "%";
            await cleanup.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM audit_entries WHERE correlation_id = {correlation}");
            await cleanup.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM idempotency_records WHERE scope = {scope} AND key LIKE {keyPattern}");
            await cleanup.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM wallet_ledger_entries WHERE customer_id = {customerId}");
            await cleanup.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM cash_receipts WHERE customer_id = {customerId}");
            await cleanup.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM wallet_accounts WHERE customer_id = {customerId}");
            await cleanup.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM customers WHERE id = {customerId}");
        }
    }

    [Fact]
    public async Task Concurrent_cash_top_ups_preserve_both_ledger_movements_and_the_balance_projection()
    {
        if (!IsPostgresEnabled()) return;
        var connectionString = Environment.GetEnvironmentVariable("GAMENET_DATABASE_CONNECTION");
        Assert.False(string.IsNullOrWhiteSpace(connectionString));

        var token = Guid.NewGuid().ToString("N");
        var customerId = Guid.NewGuid();
        var operatorId = Guid.NewGuid();
        var correlation = "wallet-concurrent-" + token;
        var customerCode = "W-" + token[..24].ToUpperInvariant();
        var scope = "wallet.cash-top-up:" + operatorId.ToString("D");
        var clock = new TestClock();

        try
        {
            await using (var setup = CreateContext(connectionString!))
            {
                setup.Customers.Add(Customer.CreateRegistered(
                    customerId, customerCode, "Wallet Concurrent Customer", clock.UtcNow));
                await setup.SaveChangesAsync();
            }

            async Task<GameNet.Shared.Primitives.Result<GameNet.Shared.Contracts.V1.Wallet.WalletCashTopUpResponse>> TopUp(
                long amount, string key)
            {
                await using var context = CreateContext(connectionString!);
                return await CreateService(context, clock).TopUpCashAsync(
                    new WalletCashTopUpCommand(customerId, amount, amount, key, CreateActor(operatorId, correlation)),
                    CancellationToken.None);
            }

            var results = await Task.WhenAll(
                TopUp(50_000L, "wallet-concurrent-a-" + token),
                TopUp(70_000L, "wallet-concurrent-b-" + token));

            Assert.All(results, result => Assert.True(result.IsSuccess, result.IsSuccess ? "" : result.Error.Code));
            Assert.NotEqual(results[0].Value.WalletLedgerEntryId, results[1].Value.WalletLedgerEntryId);

            await using var verify = CreateContext(connectionString!);
            var balance = await verify.WalletAccounts.AsNoTracking().SingleAsync(x => x.CustomerId == customerId);
            var ledger = await verify.WalletLedgerEntries.AsNoTracking().Where(x => x.CustomerId == customerId).ToArrayAsync();
            var receipts = await verify.CashReceipts.AsNoTracking().Where(x => x.CustomerId == customerId).ToArrayAsync();
            Assert.Equal(120_000L, balance.PurchasedBalanceToman);
            Assert.Equal(120_000L, ledger.Sum(x => x.GetSignedAmountToman()));
            Assert.Equal(2, ledger.Length);
            Assert.Equal(2, receipts.Length);
        }
        finally
        {
            await using var cleanup = CreateContext(connectionString!);
            var keyPattern = "%"+ token + "%";
            await cleanup.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM audit_entries WHERE correlation_id = {correlation}");
            await cleanup.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM idempotency_records WHERE scope = {scope} AND key LIKE {keyPattern}");
            await cleanup.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM wallet_ledger_entries WHERE customer_id = {customerId}");
            await cleanup.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM cash_receipts WHERE customer_id = {customerId}");
            await cleanup.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM wallet_accounts WHERE customer_id = {customerId}");
            await cleanup.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM customers WHERE id = {customerId}");
        }
    }

    private static WalletActorContext CreateActor(Guid operatorId, string correlation) =>
        new(operatorId, correlation, "PostgreSQLTest",
            new HashSet<string>([Permissions.WalletWrite], StringComparer.Ordinal));

    private static bool IsPostgresEnabled() =>
        string.Equals(Environment.GetEnvironmentVariable("GAMENET_SESSION_POSTGRES_TESTS"), "true", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(Environment.GetEnvironmentVariable("GAMENET_CUSTOMER_POSTGRES_TESTS"), "true", StringComparison.OrdinalIgnoreCase);

    private static WalletService CreateService(GameNetDbContext db, TestClock clock) =>
        new(db,
            new CommandMutationPipeline(new EfTransactionCoordinator(db), new EfIdempotencyStore(db, clock)),
            new TestFingerprint(),
            new WalletCustomerAccess(db),
            new WalletCashReceiptWriter(db),
            new EfAuditWriter(db),
            clock);

    private static GameNetDbContext CreateContext(string connectionString) =>
        new(new DbContextOptionsBuilder<GameNetDbContext>().UseNpgsql(connectionString).Options);

    private sealed class TestClock : IGameClock
    {
        public DateTimeOffset UtcNow { get; } = DateTimeOffset.UtcNow;
        public GameClockSnapshot Capture() =>
            new(UtcNow, 1_000, TimeSpan.TicksPerSecond, 5_000, Guid.Parse("04ad13fd-6cb2-4b17-a54d-5e0a48f3e9f0"));
    }

    private sealed class TestFingerprint : IRequestFingerprint
    {
        public string Compute(object payload) =>
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload))));
    }
}
