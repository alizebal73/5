using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GameNet.Server.Infrastructure.Audit;
using GameNet.Server.Infrastructure.Idempotency;
using GameNet.Server.Infrastructure.Transactions;
using GameNet.Server.Modules.Billing.Domain;
using GameNet.Server.Modules.Billing.Infrastructure;
using GameNet.Server.Modules.Customers.Domain;
using GameNet.Server.Modules.Customers.Infrastructure;
using GameNet.Server.Modules.Wallet.Application;
using GameNet.Server.Modules.Wallet.Infrastructure;
using GameNet.Server.Persistence;
using GameNet.Shared.Contracts.V1.Security;
using GameNet.Shared.Contracts.V1.Wallet;
using GameNet.Shared.Primitives;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GameNet.Server.UnitTests.Modules.Wallet;

// The class name intentionally matches the WalletCashTopUpPostgresTests filter
// used by scripts/verify-postgresql.ps1, so this regression runs in Foundation CI.
public sealed class WalletCashTopUpPostgresTestsConcurrency
{
    [Fact]
    public async Task Concurrent_top_ups_for_a_new_wallet_preserve_both_credits_and_financial_rows_are_append_only()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("GAMENET_WALLET_POSTGRES_TESTS"), "true", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(Environment.GetEnvironmentVariable("GAMENET_CUSTOMER_POSTGRES_TESTS"), "true", StringComparison.OrdinalIgnoreCase))
            return;

        var connectionString = Environment.GetEnvironmentVariable("GAMENET_DATABASE_CONNECTION");
        Assert.False(string.IsNullOrWhiteSpace(connectionString));

        var token = Guid.NewGuid().ToString("N");
        var customerId = Guid.NewGuid();
        var operatorId = Guid.NewGuid();
        var customerCode = "WC-" + token[..20].ToUpperInvariant();
        var correlationPrefix = "wallet-concurrency-" + token;
        var now = DateTimeOffset.UtcNow;
        var clock = new TestClock(now);
        var firstKey = "wallet-concurrent-a-" + token;
        var secondKey = "wallet-concurrent-b-" + token;
        var actorA = new WalletActorContext(operatorId, correlationPrefix + "-a", "PostgreSQLConcurrencyTest",
            new HashSet<string>([Permissions.WalletWrite], StringComparer.Ordinal));
        var actorB = new WalletActorContext(operatorId, correlationPrefix + "-b", "PostgreSQLConcurrencyTest",
            new HashSet<string>([Permissions.WalletWrite], StringComparer.Ordinal));

        try
        {
            await using (var seed = CreateContext(connectionString!))
            {
                seed.Customers.Add(Customer.CreateRegistered(customerId, customerCode, "Wallet Concurrency Test", now));
                await seed.SaveChangesAsync();
            }

            // Independent DbContexts model two Server requests arriving at the same time.
            // The Wallet account does not exist before these calls, exercising the first-credit race.
            var firstTask = ExecuteTopUpAsync(connectionString!, customerId, operatorId, actorA,
                amountToCredit: 10_000, amountReceived: 12_000, firstKey, clock);
            var secondTask = ExecuteTopUpAsync(connectionString!, customerId, operatorId, actorB,
                amountToCredit: 20_000, amountReceived: 20_000, secondKey, clock);
            var results = await Task.WhenAll(firstTask, secondTask);

            Assert.All(results, result => Assert.True(result.IsSuccess, result.IsSuccess ? "" : result.Error.Code));
            Assert.Equal(new[] { 10_000L, 20_000L }, results.Select(x => x.Value.AmountCreditedToman).Order().ToArray());
            Assert.Equal(new[] { 0L, 2_000L }, results.Select(x => x.Value.ChangeGivenToman).Order().ToArray());
            Assert.Equal(2, results.Select(x => x.Value.CashReceiptId).Distinct().Count());
            Assert.Equal(2, results.Select(x => x.Value.WalletLedgerEntryId).Distinct().Count());

            await using (var verify = CreateContext(connectionString!))
            {
                var account = await verify.WalletAccounts.AsNoTracking().SingleAsync(x => x.CustomerId == customerId);
                var ledger = await verify.WalletLedgerEntries.AsNoTracking().Where(x => x.CustomerId == customerId).ToArrayAsync();
                var receipts = await verify.CashReceipts.AsNoTracking().Where(x => x.CustomerId == customerId).ToArrayAsync();

                Assert.Equal(30_000L, account.BalanceToman);
                Assert.Equal(2, account.Version);
                Assert.Equal(2, ledger.Length);
                Assert.Equal(2, receipts.Length);
                Assert.Equal(account.BalanceToman, ledger.Sum(x => x.AmountToman));
                Assert.Equal(30_000L, ledger.Sum(x => x.AmountToman));
                Assert.All(ledger, entry => Assert.Contains(receipts, receipt =>
                    receipt.Id == entry.CashReceiptId &&
                    receipt.CustomerId == entry.CustomerId &&
                    receipt.AmountAppliedToman == entry.AmountToman));
                Assert.Equal(2, await verify.AuditEntries.AsNoTracking()
                    .CountAsync(x => x.CorrelationId.StartsWith(correlationPrefix) && x.Operation == "wallet.cash_top_up"));
                Assert.Equal(2, await verify.AuditEntries.AsNoTracking()
                    .CountAsync(x => x.CorrelationId.StartsWith(correlationPrefix) && x.Operation == "billing.wallet_cash_receipt"));

                var ledgerId = ledger[0].Id;
                var receiptId = receipts[0].Id;

                var ledgerUpdateFailure = await Record.ExceptionAsync(async () =>
                {
                    await using var guard = CreateContext(connectionString!);
                    await guard.Database.ExecuteSqlInterpolatedAsync(
                        $"UPDATE wallet_ledger_entries SET amount_toman = amount_toman + 1 WHERE id = {ledgerId}");
                });
                Assert.NotNull(ledgerUpdateFailure);
                Assert.Contains("WALLET_FINANCIAL_RECORD_APPEND_ONLY", ledgerUpdateFailure!.ToString(), StringComparison.Ordinal);

                var ledgerDeleteFailure = await Record.ExceptionAsync(async () =>
                {
                    await using var guard = CreateContext(connectionString!);
                    await guard.Database.ExecuteSqlInterpolatedAsync(
                        $"DELETE FROM wallet_ledger_entries WHERE id = {ledgerId}");
                });
                Assert.NotNull(ledgerDeleteFailure);
                Assert.Contains("WALLET_FINANCIAL_RECORD_APPEND_ONLY", ledgerDeleteFailure!.ToString(), StringComparison.Ordinal);

                var receiptUpdateFailure = await Record.ExceptionAsync(async () =>
                {
                    await using var guard = CreateContext(connectionString!);
                    await guard.Database.ExecuteSqlInterpolatedAsync(
                        $"UPDATE cash_receipts SET amount_received_toman = amount_received_toman + 1 WHERE id = {receiptId}");
                });
                Assert.NotNull(receiptUpdateFailure);
                Assert.Contains("WALLET_FINANCIAL_RECORD_APPEND_ONLY", receiptUpdateFailure!.ToString(), StringComparison.Ordinal);

                var receiptDeleteFailure = await Record.ExceptionAsync(async () =>
                {
                    await using var guard = CreateContext(connectionString!);
                    await guard.Database.ExecuteSqlInterpolatedAsync(
                        $"DELETE FROM cash_receipts WHERE id = {receiptId}");
                });
                Assert.NotNull(receiptDeleteFailure);
                Assert.Contains("WALLET_FINANCIAL_RECORD_APPEND_ONLY", receiptDeleteFailure!.ToString(), StringComparison.Ordinal);
            }
        }
        finally
        {
            await using var cleanup = CreateContext(connectionString!);
            await cleanup.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM audit_entries WHERE correlation_id LIKE {correlationPrefix + "%"}");
            await cleanup.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM idempotency_records WHERE scope = {"wallet.cash-top-up:" + operatorId.ToString("D")}");
            // This is an opt-in disposable PostgreSQL test database. Production paths never disable these guards.
            await cleanup.Database.ExecuteSqlRawAsync(
                "ALTER TABLE wallet_ledger_entries DISABLE TRIGGER wallet_ledger_entries_append_only");
            try
            {
                await cleanup.Database.ExecuteSqlRawAsync(
                    "ALTER TABLE cash_receipts DISABLE TRIGGER cash_receipts_append_only");
                try
                {
                    await cleanup.Database.ExecuteSqlInterpolatedAsync(
                        $"DELETE FROM wallet_ledger_entries WHERE customer_id = {customerId}");
                    await cleanup.Database.ExecuteSqlInterpolatedAsync(
                        $"DELETE FROM wallet_accounts WHERE customer_id = {customerId}");
                    await cleanup.Database.ExecuteSqlInterpolatedAsync(
                        $"DELETE FROM cash_receipts WHERE customer_id = {customerId}");
                    await cleanup.Database.ExecuteSqlInterpolatedAsync(
                        $"DELETE FROM customers WHERE id = {customerId}");
                }
                finally
                {
                    await cleanup.Database.ExecuteSqlRawAsync(
                        "ALTER TABLE cash_receipts ENABLE TRIGGER cash_receipts_append_only");
                }
            }
            finally
            {
                await cleanup.Database.ExecuteSqlRawAsync(
                    "ALTER TABLE wallet_ledger_entries ENABLE TRIGGER wallet_ledger_entries_append_only");
            }
        }
    }

    private static async Task<Result<WalletCashTopUpResponse>> ExecuteTopUpAsync(
        string connectionString,
        Guid customerId,
        Guid operatorId,
        WalletActorContext actor,
        long amountToCredit,
        long amountReceived,
        string idempotencyKey,
        TestClock clock)
    {
        await using var db = CreateContext(connectionString);
        var pipeline = new CommandMutationPipeline(new EfTransactionCoordinator(db), new EfIdempotencyStore(db, clock));
        var fingerprint = new TestFingerprint();
        var billing = new BillingService(db, pipeline, fingerprint, null!, null!, new EfAuditWriter(db), clock);
        var wallet = new WalletService(db, new CustomerWalletEligibilityService(db), pipeline, fingerprint,
            billing, new EfAuditWriter(db), clock);
        return await wallet.CashTopUpAsync(new CashTopUpWalletCommand(
            customerId, amountToCredit, amountReceived, 1, idempotencyKey, actor), CancellationToken.None);
    }

    private static GameNetDbContext CreateContext(string connectionString) =>
        new(new DbContextOptionsBuilder<GameNetDbContext>().UseNpgsql(connectionString).Options);

    private sealed class TestClock(DateTimeOffset now) : IGameClock
    {
        public DateTimeOffset UtcNow { get; } = now;
        public GameClockSnapshot Capture() =>
            new(UtcNow, 1_000, TimeSpan.TicksPerSecond, 5_000, Guid.Parse("04ad13fd-6cb2-4b17-a54d-5e0a48f3e9f0"));
    }

    private sealed class TestFingerprint : IRequestFingerprint
    {
        public string Compute(object payload) =>
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload))));
    }
}
