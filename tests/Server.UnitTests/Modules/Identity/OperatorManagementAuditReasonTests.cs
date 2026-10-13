using System.Text.Json;
using GameNet.Server.Infrastructure.Audit;
using GameNet.Server.Infrastructure.Idempotency;
using GameNet.Server.Infrastructure.Transactions;
using GameNet.Server.Modules.Identity.Application;
using GameNet.Server.Modules.Identity.Domain;
using GameNet.Shared.Contracts.V1.Security;
using GameNet.Shared.Primitives;
using Xunit;

namespace GameNet.Server.UnitTests.Modules.Identity;

public sealed class OperatorManagementAuditReasonTests
{
    [Fact]
    public async Task Create_role_records_the_submitted_normalized_reason_separately_in_audit()
    {
        var fixture = new Fixture();
        var actor = new IdentityActor(Guid.NewGuid(), "corr-test-1", "UnitTest");

        var result = await fixture.Service.CreateRoleAsync(
            new CreateRoleCommand(
                "cashier",
                "Cashier",
                Array.Empty<string>(),
                "  Added for weekend coverage  ",
                "idempotency-role-1",
                actor),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var record = Assert.Single(fixture.Audit.Records);
        Assert.Equal("identity.role_create", record.Operation);
        Assert.Equal(actor.UserId.ToString("D"), record.ActorId);
        Assert.Equal("Added for weekend coverage", record.Reason);
        Assert.Equal("corr-test-1", record.CorrelationId);
        Assert.Equal("UnitTest", record.Source);
        Assert.Equal("Succeeded", record.Outcome);
        Assert.True(fixture.Repository.RoleWasAdded);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("no")]
    [InlineData("this reason is deliberately longer than two hundred and fifty-six characters so that the identity reason validation can be proven to reject oversized explanations before any role mutation is persisted; this string continues with more context in order to cross the maximum limit and exercise the rejection path safely")]
    public async Task Create_role_rejects_missing_or_out_of_range_reason_without_mutating(string? reason)
    {
        var fixture = new Fixture();
        var actor = new IdentityActor(Guid.NewGuid(), "corr-test-2", "UnitTest");

        var result = await fixture.Service.CreateRoleAsync(
            new CreateRoleCommand(
                "cashier",
                "Cashier",
                Array.Empty<string>(),
                reason!,
                "idempotency-role-2",
                actor),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("identity.invalid", result.Error.Code);
        Assert.Empty(fixture.Audit.Records);
        Assert.False(fixture.Repository.RoleWasAdded);
    }

    private sealed class Fixture
    {
        public RecordingAuditWriter Audit { get; } = new();
        public FakeOperatorManagementRepository Repository { get; } = new();
        public OperatorManagementService Service { get; }

        public Fixture()
        {
            Service = new OperatorManagementService(
                Repository,
                new FakeIdentityRepository(),
                new FakePasswordHasher(),
                new InlineTransactionCoordinator(),
                new FakeIdempotencyStore(),
                new JsonRequestFingerprint(),
                Audit,
                new FixedClock(DateTimeOffset.Parse("2026-10-13T12:00:00Z")));
        }
    }

    private sealed class RecordingAuditWriter : IAuditWriter
    {
        public List<AuditRecord> Records { get; } = [];
        public void Append(AuditRecord record) => Records.Add(record);
    }

    private sealed class InlineTransactionCoordinator : ITransactionCoordinator
    {
        public Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken = default) =>
            action(cancellationToken);
    }

    private sealed class FakeIdempotencyStore : IIdempotencyStore
    {
        public Task<IdempotencyClaim> TryClaimAsync(
            string scope,
            string key,
            string operation,
            string requestHash,
            TimeSpan leaseDuration,
            TimeSpan retention,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new IdempotencyClaim(true, false, false, "unit-test-lease", null, null));

        public Task CompleteAsync(
            string scope,
            string key,
            string leaseToken,
            int statusCode,
            string responseJson,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class JsonRequestFingerprint : IRequestFingerprint
    {
        public string Compute(object payload) => JsonSerializer.Serialize(payload);
    }

    private sealed class FixedClock(DateTimeOffset now) : IGameClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }

    private sealed class FakePasswordHasher : IPasswordHasher
    {
        public string Hash(string password) => "not-used-by-this-test";
        public bool Verify(string password, string encodedHash) => false;
    }

    private sealed class FakeIdentityRepository : IIdentityRepository
    {
        private static readonly IReadOnlySet<string> ActorPermissions =
            new HashSet<string>([Permissions.IdentityRolesManage], StringComparer.Ordinal);

        public Task<IReadOnlySet<string>> GetPermissionsAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult(ActorPermissions);

        public Task AcquireBootstrapLockAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> AnyUsersAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<OperatorUser?> FindUserAsync(string username, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<OperatorUser?> FindUserByIdAsync(Guid userId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task AddUserAsync(OperatorUser user, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task AddRoleAsync(Role role, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task AddUserRoleAsync(UserRole userRole, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task AddRolePermissionAsync(RolePermission permission, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task AddSessionAsync(AuthSession session, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<AuthSession?> FindSessionAsync(string jti, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task UpdateSessionAsync(AuthSession session, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FakeOperatorManagementRepository : IOperatorManagementRepository
    {
        public bool RoleWasAdded { get; private set; }

        public Task<bool> RoleCodeExistsAsync(string normalizedCode, CancellationToken cancellationToken) => Task.FromResult(false);
        public Task<bool> IsOwnerAsync(Guid userId, CancellationToken cancellationToken) => Task.FromResult(false);
        public Task AddRoleAsync(Role role, CancellationToken cancellationToken)
        {
            RoleWasAdded = true;
            return Task.CompletedTask;
        }

        public Task AddRolePermissionAsync(RolePermission permission, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<IReadOnlyList<OperatorUserSnapshot>> ListUsersAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<RoleSnapshot>> ListRolesAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<OperatorUser?> FindUserByIdAsync(Guid userId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Role?> FindRoleByIdAsync(Guid roleId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> UsernameExistsAsync(string normalizedUsername, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<int> CountActiveOwnersAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlySet<string>> GetRolePermissionsAsync(Guid roleId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task AddUserAsync(OperatorUser user, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task AddUserRoleAsync(UserRole userRole, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task ReplaceRolePermissionsAsync(Guid roleId, IReadOnlyCollection<string> permissions, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task ReplaceUserRoleAsync(Guid userId, Guid roleId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task RevokeActiveSessionsAsync(Guid userId, DateTimeOffset revokedAtUtc, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task RevokeOtherActiveSessionsAsync(Guid userId, string currentSessionJti, DateTimeOffset revokedAtUtc, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
