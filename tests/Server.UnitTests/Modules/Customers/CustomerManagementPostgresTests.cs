using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GameNet.Server.Infrastructure.Audit;
using GameNet.Server.Infrastructure.Idempotency;
using GameNet.Server.Infrastructure.Transactions;
using GameNet.Server.Modules.Customers.Contracts;
using GameNet.Server.Modules.Customers.Infrastructure;
using GameNet.Server.Persistence;
using GameNet.Shared.Contracts.V1.Customers;
using GameNet.Shared.Contracts.V1.Security;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GameNet.Server.UnitTests.Modules.Customers;

public sealed class CustomerManagementPostgresTests
{
    [Fact]
    public async Task Customer_create_search_edit_status_idempotency_and_audit_are_persisted()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("GAMENET_SESSION_POSTGRES_TESTS"), "true", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(Environment.GetEnvironmentVariable("GAMENET_CUSTOMER_POSTGRES_TESTS"), "true", StringComparison.OrdinalIgnoreCase))
            return;
        var connectionString = Environment.GetEnvironmentVariable("GAMENET_DATABASE_CONNECTION");
        Assert.False(string.IsNullOrWhiteSpace(connectionString));
        var token = Guid.NewGuid().ToString("N");
        var operatorId = Guid.NewGuid();
        var correlation = "customer-pg-" + token;
        var code = "C-" + token[..20].ToUpperInvariant();
        var clock = new TestClock();
        var actor = new CustomerActorContext(operatorId, correlation, "PostgreSQLTest",
            new HashSet<string>([Permissions.CustomersRead, Permissions.CustomersWrite], StringComparer.Ordinal));
        try
        {
            await using var db = CreateContext(connectionString!);
            var service = new CustomerManagementService(db,
                new CommandMutationPipeline(new EfTransactionCoordinator(db), new EfIdempotencyStore(db, clock)),
                new TestFingerprint(), new EfAuditWriter(db), clock);
            var createKey = "customer-create-" + token;
            var created = await service.CreateAsync(new CreateCustomerCommand(
                " " + code.ToLowerInvariant() + " ", " Test Customer ", " Test Nick ", "+۹۸۹۱۲۱۲۳۴۵۶۷", createKey, actor), CancellationToken.None);
            Assert.True(created.IsSuccess, created.IsSuccess ? "" : created.Error.Code);
            Assert.Equal(code, created.Value.Code);
            Assert.Equal("Test Customer", created.Value.DisplayName);
            Assert.Equal("Test Nick", created.Value.Nickname);
            Assert.Equal("+989121234567", created.Value.PhoneNumber);
            Assert.Equal(CustomerStatusContract.Active, created.Value.Status);
            var replay = await service.CreateAsync(new CreateCustomerCommand(code, "Test Customer", "Test Nick", "+989121234567", createKey, actor), CancellationToken.None);
            Assert.True(replay.IsSuccess);
            Assert.Equal(created.Value.Id, replay.Value.Id);
            var conflict = await service.CreateAsync(new CreateCustomerCommand(code, "Different Customer", null, null, createKey, actor), CancellationToken.None);
            Assert.False(conflict.IsSuccess);
            Assert.Equal("idempotency.key_reused", conflict.Error.Code);
            var listed = await service.ListAsync(actor, "test nick", includeInactive: true, take: 20, CancellationToken.None);
            Assert.True(listed.IsSuccess);
            Assert.Contains(listed.Value, x => x.Id == created.Value.Id);
            var edited = await service.UpdateProfileAsync(new UpdateCustomerProfileCommand(
                created.Value.Id, "Updated Customer", "New Nick", "09121234567", created.Value.Version, "customer-edit-" + token, actor), CancellationToken.None);
            Assert.True(edited.IsSuccess, edited.IsSuccess ? "" : edited.Error.Code);
            Assert.Equal("Updated Customer", edited.Value.DisplayName);
            Assert.Equal(created.Value.Version + 1, edited.Value.Version);
            var staleUpdate = await service.UpdateProfileAsync(new UpdateCustomerProfileCommand(
                created.Value.Id, "Should Not Win", null, null, created.Value.Version, "customer-stale-" + token, actor), CancellationToken.None);
            Assert.False(staleUpdate.IsSuccess);
            Assert.Equal("customers.version_conflict", staleUpdate.Error.Code);
            var blocked = await service.SetStatusAsync(new SetCustomerStatusCommand(
                created.Value.Id, CustomerStatusContract.Blocked, edited.Value.Version, "Verified operator request", "customer-block-" + token, actor), CancellationToken.None);
            Assert.True(blocked.IsSuccess, blocked.IsSuccess ? "" : blocked.Error.Code);
            Assert.Equal(CustomerStatusContract.Blocked, blocked.Value.Status);
            var notSelectable = await new GameNet.Server.Modules.Customers.Infrastructure.CustomerSessionAccess(db)
                .FindActiveAsync(created.Value.Id, CancellationToken.None);
            Assert.NotNull(notSelectable);
            Assert.True(notSelectable!.IsBlocked);
            var reactivated = await service.SetStatusAsync(new SetCustomerStatusCommand(
                created.Value.Id, CustomerStatusContract.Active, blocked.Value.Version, "Reviewed restriction", "customer-reactivate-" + token, actor), CancellationToken.None);
            Assert.True(reactivated.IsSuccess);
            Assert.Equal(CustomerStatusContract.Active, reactivated.Value.Status);
            var auditRows = await db.AuditEntries.AsNoTracking().Where(x => x.CorrelationId == correlation).ToArrayAsync();
            Assert.Contains(auditRows, x => x.Operation == "customers.create" && x.ReferenceId == created.Value.Id.ToString("D"));
            Assert.Contains(auditRows, x => x.Operation == "customers.update_profile" && x.ReferenceId == created.Value.Id.ToString("D"));
            Assert.Contains(auditRows, x => x.Operation == "customers.set_status" && x.Reason == "Verified operator request");
            Assert.DoesNotContain(auditRows, x => (x.BeforeJson ?? "").Contains("+989121234567", StringComparison.Ordinal));
            Assert.DoesNotContain(auditRows, x => (x.AfterJson ?? "").Contains("+989121234567", StringComparison.Ordinal));
        }
        finally
        {
            await using var cleanup = CreateContext(connectionString!);
            await cleanup.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM audit_entries WHERE correlation_id = {correlation}");
            await cleanup.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM idempotency_records WHERE scope LIKE {"customers.%:" + operatorId.ToString("D")} AND key LIKE {"%" + token + "%"}");
            await cleanup.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM customers WHERE code = {code}");
        }
    }

    private static GameNetDbContext CreateContext(string connectionString) =>
        new(new DbContextOptionsBuilder<GameNetDbContext>().UseNpgsql(connectionString).Options);

    private sealed class TestClock : GameNet.Shared.Primitives.IGameClock
    {
        public DateTimeOffset UtcNow { get; } = DateTimeOffset.UtcNow;
    }

    private sealed class TestFingerprint : IRequestFingerprint
    {
        public string Compute(object payload) =>
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload))));
    }
}
