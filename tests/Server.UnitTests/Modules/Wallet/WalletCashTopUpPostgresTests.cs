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
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GameNet.Server.UnitTests.Modules.Wallet;

public sealed class WalletCashTopUpPostgresTests
{
    [Fact]
    public async Task CashTopUp_IsAtomicIdempotentAndReconcilesCashLedgerProjectionAndAudit()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("GAMENET_WALLET_POSTGRES_TESTS"), "true", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(Environment.GetEnvironmentVariable("GAMENET_CUSTOMER_POSTGRES_TESTS"), "true", StringComparison.OrdinalIgnoreCase))
            return;

        var connectionString = Environment.GetEnvironmentVariable("GAMENET_DATABASE_CONNECTION");
        Assert.False(string.IsNullOrWhiteSpace(connectionString));
        var token = Guid.NewGuid().ToString("N");
        var customerId = Guid.NewGuid();
        var operatorId = Guid.NewGuid();
        var correlation = "wallet-pg-" + token;
        var customerCode = "W-" + token[..20].ToUpperInvariant();
        var now = DateTimeOffset.UtcNow;
        var clock = new TestClock(now);
        var actor = new WalletActorContext(operatorId, correlation, "PostgreSQLTest",
            new HashSet<string>([Permissions.WalletWrite], StringComparer.Ordinal));

        try
        {
            await using (var seed = CreateContext(connectionString!))
            {
                seed.Customers.Add(Customer.CreateRegistered(customerId, customerCode, "Wallet Test Customer", now));
                await seed.SaveChangesAsync();
            }

            await using (var db = CreateContext(connectionString!))
            {
                var pipeline = new CommandMutationPipeline(new EfTransactionCoordinator(db), new EfIdempotencyStore(db, clock));
                var fingerprint = new TestFingerprint();
                var billing = new BillingService(db, pipeline, fingerprint, null!, null!, new EfAuditWriter(db), clock);
                var wallet = new WalletService(db, new CustomerWalletEligibilityService(db), pipeline, fingerprint, billing, new EfAuditWriter(db), clock);
                var command = new CashTopUpWalletCommand(customerId, 105_000, 110_000, 1, "wallet-top-up-" + token, actor);
                var credited = await wallet.CashTopUpAsync(command, CancellationToken.None);

                Assert.True(credited.IsSuccess, credited.IsSuccess ? "" : credited.Error.Code);
                Assert.Equal(105_000, credited.Value.AmountCreditedToman);
                Assert.Equal(110_000, credited.Value.AmountReceivedToman);
                Assert.Equal(5_000, credited.Value.ChangeGivenToman);
                Assert.Equal(105_000, credited.Value.BalanceAfterToman);

                var replay = await wallet.CashTopUpAsync(command, CancellationToken.None);
                Assert.True(replay.IsSuccess);
                Assert.Equal(credited.Value.CashReceiptId, replay.Value.CashReceiptId);
                Assert.Equal(credited.Value.WalletLedgerEntryId, replay.Value.WalletLedgerEntryId);

                var conflict = await wallet.CashTopUpAsync(command with { AmountToCreditToman = 100_000 }, CancellationToken.None);
                Assert.False(conflict.IsSuccess);
                Assert.Equal("idempotency.key_reused", conflict.Error.Code);

                var balance = await wallet.GetBalanceAsync(customerId, actor, CancellationToken.None);
                Assert.True(balance.IsSuccess);
                Assert.Equal(105_000, balance.Value.BalanceToman);

                var receipt = await db.CashReceipts.AsNoTracking().SingleAsync(x => x.Id == credited.Value.CashReceiptId);
                var ledger = await db.WalletLedgerEntries.AsNoTracking().SingleAsync(x => x.Id == credited.Value.WalletLedgerEntryId);
                var account = await db.WalletAccounts.AsNoTracking().SingleAsync(x => x.CustomerId == customerId);
                Assert.Equal(receipt.AmountAppliedToman, ledger.AmountToman);
                Assert.Equal(ledger.BalanceAfterToman, account.BalanceToman);
                Assert.Equal(await db.WalletLedgerEntries.Where(x => x.CustomerId == customerId).SumAsync(x => x.AmountToman), account.BalanceToman);
                Assert.Contains(await db.AuditEntries.AsNoTracking().Where(x => x.CorrelationId == correlation).ToArrayAsync(),
                    x => x.Operation == "wallet.cash_top_up" && x.ReferenceId == customerId.ToString("D"));
            }
        }
        finally
        {
            await using var cleanup = CreateContext(connectionString!);
            await cleanup.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM audit_entries WHERE correlation_id = {correlation}");
            await cleanup.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM idempotency_records WHERE scope = {"wallet.cash-top-up:" + operatorId.ToString("D")}");
            // Cleanup is limited to the opt-in disposable PostgreSQL test database; production paths never disable these guards.
            await cleanup.Database.ExecuteSqlRawAsync("ALTER TABLE wallet_ledger_entries DISABLE TRIGGER wallet_ledger_entries_append_only");
            try
            {
                await cleanup.Database.ExecuteSqlRawAsync("ALTER TABLE cash_receipts DISABLE TRIGGER cash_receipts_append_only");
                try
                {
                    await cleanup.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM wallet_ledger_entries WHERE customer_id = {customerId}");
                    await cleanup.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM wallet_accounts WHERE customer_id = {customerId}");
                    await cleanup.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM cash_receipts WHERE customer_id = {customerId}");
                    await cleanup.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM customers WHERE id = {customerId}");
                }
                finally
                {
                    await cleanup.Database.ExecuteSqlRawAsync("ALTER TABLE cash_receipts ENABLE TRIGGER cash_receipts_append_only");
                }
            }
            finally
            {
                await cleanup.Database.ExecuteSqlRawAsync("ALTER TABLE wallet_ledger_entries ENABLE TRIGGER wallet_ledger_entries_append_only");
            }
        }
    }

    private static GameNetDbContext CreateContext(string connectionString) =>
        new(new DbContextOptionsBuilder<GameNetDbContext>().UseNpgsql(connectionString).Options);

    private sealed class TestClock(DateTimeOffset now) : GameNet.Shared.Primitives.IGameClock
    {
        public DateTimeOffset UtcNow { get; } = now;
        public GameNet.Shared.Primitives.GameClockSnapshot Capture() =>
            new(UtcNow, 1_000, TimeSpan.TicksPerSecond, 5_000, Guid.Parse("04ad13fd-6cb2-4b17-a54d-5e0a48f3e9f0"));
    }

    private sealed class TestFingerprint : IRequestFingerprint
    {
        public string Compute(object payload) =>
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload))));
    }
}