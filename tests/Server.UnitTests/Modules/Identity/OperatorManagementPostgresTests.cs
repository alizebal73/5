using GameNet.Server.Infrastructure.Audit;
using GameNet.Server.Infrastructure.Idempotency;
using GameNet.Server.Infrastructure.Persistence.Identity;
using GameNet.Server.Infrastructure.Security;
using GameNet.Server.Infrastructure.Transactions;
using GameNet.Server.Modules.Identity.Application;
using GameNet.Server.Modules.Identity.Domain;
using GameNet.Server.Modules.Identity.Infrastructure.Security;
using GameNet.Server.Persistence;
using GameNet.Shared.Primitives;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GameNet.Server.UnitTests.Modules.Identity;

/// <summary>
/// PostgreSQL-backed Identity invariants. These tests run only when the Foundation
/// PostgreSQL harness explicitly enables GAMENET_OPERATOR_MANAGEMENT_POSTGRES_TESTS.
/// They use unique disposable rows and always clean up their data.
/// </summary>
[Collection("Identity operator management PostgreSQL serial")]
public sealed class OperatorManagementPostgresTests
{
    private const string FingerprintKey = "dGVzdC1vbmx5LWZpbmdlcnByaW50LWtleS1mb3ItZ2FtZW5ldC01LWF1dGhvcml6YXRpb24=";

    [Fact]
    public async Task Concurrent_owner_deactivation_cannot_remove_last_active_owner()
    {
        if (!IsExplicitlyEnabled()) return;

        var connectionString = GetConnectionString();
        var clock = new FixedClock(DateTimeOffset.UtcNow);
        var seeded = await SeedAsync(connectionString, ownerCount: 2, includeCashier: false);
        var barrier = new OwnerCountBarrier();
        var keyA = IdempotencyKey();
        var keyB = IdempotencyKey();

        try
        {
            await using var dbA = CreateContext(connectionString);
            await using var dbB = CreateContext(connectionString);
            var serviceA = CreateService(
                dbA,
                clock,
                new OwnerCountBarrierRepository(new EfOperatorManagementRepository(dbA), barrier));
            var serviceB = CreateService(
                dbB,
                clock,
                new OwnerCountBarrierRepository(new EfOperatorManagementRepository(dbB), barrier));

            // Each Owner tries to disable the other. The shared barrier waits until both
            // independent Serializable transactions have read "2 active Owners", making
            // the write-skew race deterministic rather than relying on Task.Delay timing.
            var disableA = serviceB.SetOperatorActiveAsync(
                new SetOperatorActiveCommand(
                    seeded.OwnerIds[0],
                    false,
                    "PostgreSQL concurrent last-Owner protection test A",
                    keyA,
                    new IdentityActor(seeded.OwnerIds[1], "identity-concurrency-b", "PostgreSQLTest")),
                CancellationToken.None);
            var disableB = serviceA.SetOperatorActiveAsync(
                new SetOperatorActiveCommand(
                    seeded.OwnerIds[1],
                    false,
                    "PostgreSQL concurrent last-Owner protection test B",
                    keyB,
                    new IdentityActor(seeded.OwnerIds[0], "identity-concurrency-a", "PostgreSQLTest")),
                CancellationToken.None);

            var outcomes = await Task.WhenAll(disableA, disableB).WaitAsync(TimeSpan.FromSeconds(25));

            Assert.Equal(2, barrier.InitialReadCount);
            Assert.True(barrier.TotalReadCount >= 3,
                "The serialized race should cause one transaction to retry and observe the remaining Owner.");
            Assert.Single(outcomes, result => result.IsSuccess);
            var rejected = Assert.Single(outcomes, result => !result.IsSuccess);
            Assert.Equal("identity.last_owner_required", rejected.Error.Code);

            await using var verifyDb = CreateContext(connectionString);
            var activeOwnerCount = await (
                from user in verifyDb.OperatorUsers.AsNoTracking()
                join userRole in verifyDb.UserRoles.AsNoTracking() on user.Id equals userRole.UserId
                join role in verifyDb.Roles.AsNoTracking() on userRole.RoleId equals role.Id
                where user.IsActive && role.Code == "owner" && seeded.OwnerIds.Contains(user.Id)
                select user.Id).Distinct().CountAsync();
            Assert.Equal(1, activeOwnerCount);

            var disabledOwner = await verifyDb.OperatorUsers.AsNoTracking()
                .SingleAsync(user => seeded.OwnerIds.Contains(user.Id) && !user.IsActive);
            var activeOwner = await verifyDb.OperatorUsers.AsNoTracking()
                .SingleAsync(user => seeded.OwnerIds.Contains(user.Id) && user.IsActive);

            var disabledSession = await verifyDb.AuthSessions.AsNoTracking()
                .SingleAsync(session => session.UserId == disabledOwner.Id);
            var activeSession = await verifyDb.AuthSessions.AsNoTracking()
                .SingleAsync(session => session.UserId == activeOwner.Id);
            Assert.NotNull(disabledSession.RevokedAtUtc);
            Assert.Null(activeSession.RevokedAtUtc);

            var ownerReferences = seeded.OwnerIds.Select(id => id.ToString("D")).ToArray();
            var audit = Assert.Single(await verifyDb.AuditEntries.AsNoTracking()
                .Where(entry => entry.Operation == "identity.user_status" &&
                                ownerReferences.Contains(entry.ReferenceId!))
                .ToArrayAsync());
            Assert.Equal(disabledOwner.Id.ToString("D"), audit.ReferenceId);
            Assert.Equal(disabledOwner.Id == seeded.OwnerIds[0]
                ? "PostgreSQL concurrent last-Owner protection test A"
                : "PostgreSQL concurrent last-Owner protection test B", audit.Reason);
            Assert.Equal("Succeeded", audit.Outcome);
            Assert.Equal(clock.UtcNow, audit.OccurredAtUtc);
            Assert.False(string.IsNullOrWhiteSpace(audit.CorrelationId));
        }
        finally
        {
            await CleanupAsync(connectionString, seeded, keyA, keyB);
        }
    }

    [Fact]
    public async Task Changing_an_owner_role_revokes_the_target_sessions_and_audits_reason()
    {
        if (!IsExplicitlyEnabled()) return;

        var connectionString = GetConnectionString();
        var clock = new FixedClock(DateTimeOffset.UtcNow);
        var seeded = await SeedAsync(connectionString, ownerCount: 2, includeCashier: false);
        var key = IdempotencyKey();

        try
        {
            await using var db = CreateContext(connectionString);
            var service = CreateService(db, clock);
            var actor = new IdentityActor(seeded.OwnerIds[0], "identity-role-change", "PostgreSQLTest");
            const string reason = "PostgreSQL test: remove second Owner role";

            var result = await service.AssignOperatorRoleAsync(
                new AssignOperatorRoleCommand(
                    seeded.OwnerIds[1],
                    seeded.CashierRoleId,
                    reason,
                    key,
                    actor),
                CancellationToken.None);

            Assert.True(result.IsSuccess);
            Assert.False(result.Value.IsOwner);
            Assert.Contains(seeded.CashierRoleCode, result.Value.RoleCodes);

            await using var verifyDb = CreateContext(connectionString);
            var targetRoles = await (
                from userRole in verifyDb.UserRoles.AsNoTracking()
                join role in verifyDb.Roles.AsNoTracking() on userRole.RoleId equals role.Id
                where userRole.UserId == seeded.OwnerIds[1]
                select role.Code).ToArrayAsync();
            Assert.Equal(new[] { seeded.CashierRoleCode }, targetRoles);

            var targetSession = await verifyDb.AuthSessions.AsNoTracking()
                .SingleAsync(session => session.UserId == seeded.OwnerIds[1]);
            var actorSession = await verifyDb.AuthSessions.AsNoTracking()
                .SingleAsync(session => session.UserId == seeded.OwnerIds[0]);
            Assert.NotNull(targetSession.RevokedAtUtc);
            Assert.Null(actorSession.RevokedAtUtc);

            var audit = Assert.Single(await verifyDb.AuditEntries.AsNoTracking()
                .Where(entry => entry.Operation == "identity.user_role_assign" &&
                                entry.ReferenceId == seeded.OwnerIds[1].ToString("D"))
                .ToArrayAsync());
            Assert.Equal(reason, audit.Reason);
            Assert.Equal(seeded.OwnerIds[0].ToString("D"), audit.ActorId);
            Assert.Equal(actor.CorrelationId, audit.CorrelationId);
            Assert.Equal("Succeeded", audit.Outcome);
        }
        finally
        {
            await CleanupAsync(connectionString, seeded, key);
        }
    }

    [Fact]
    public async Task Active_status_idempotency_replays_same_reason_and_rejects_changed_reason()
    {
        if (!IsExplicitlyEnabled()) return;

        var connectionString = GetConnectionString();
        var clock = new FixedClock(DateTimeOffset.UtcNow);
        var seeded = await SeedAsync(connectionString, ownerCount: 1, includeCashier: true);
        var key = IdempotencyKey();

        try
        {
            Assert.NotNull(seeded.CashierUserId);
            await using var db = CreateContext(connectionString);
            var service = CreateService(db, clock);
            var actor = new IdentityActor(seeded.OwnerIds[0], "identity-idempotency", "PostgreSQLTest");
            const string reason = "Operator no longer works at this venue";

            var command = new SetOperatorActiveCommand(
                seeded.CashierUserId!.Value,
                false,
                reason,
                key,
                actor);

            var first = await service.SetOperatorActiveAsync(command, CancellationToken.None);
            var replay = await service.SetOperatorActiveAsync(command, CancellationToken.None);
            var changedReason = await service.SetOperatorActiveAsync(
                command with { Reason = "Different business justification" },
                CancellationToken.None);

            Assert.True(first.IsSuccess);
            Assert.True(replay.IsSuccess);
            Assert.Equal(first.Value.Id, replay.Value.Id);
            Assert.False(changedReason.IsSuccess);
            Assert.Equal("idempotency.key_reused", changedReason.Error.Code);

            await using var verifyDb = CreateContext(connectionString);
            var target = await verifyDb.OperatorUsers.AsNoTracking()
                .SingleAsync(user => user.Id == seeded.CashierUserId.Value);
            Assert.False(target.IsActive);
            var session = await verifyDb.AuthSessions.AsNoTracking()
                .SingleAsync(value => value.UserId == seeded.CashierUserId.Value);
            Assert.NotNull(session.RevokedAtUtc);

            var audits = await verifyDb.AuditEntries.AsNoTracking()
                .Where(entry => entry.Operation == "identity.user_status" &&
                                entry.ReferenceId == seeded.CashierUserId.Value.ToString("D"))
                .ToArrayAsync();
            var audit = Assert.Single(audits);
            Assert.Equal(reason, audit.Reason);
            Assert.Equal(actor.UserId.ToString("D"), audit.ActorId);
        }
        finally
        {
            await CleanupAsync(connectionString, seeded, key);
        }
    }

    private static bool IsExplicitlyEnabled() =>
        string.Equals(
            Environment.GetEnvironmentVariable("GAMENET_OPERATOR_MANAGEMENT_POSTGRES_TESTS"),
            "true",
            StringComparison.OrdinalIgnoreCase);

    private static string GetConnectionString()
    {
        var value = Environment.GetEnvironmentVariable("GAMENET_DATABASE_CONNECTION");
        Assert.False(string.IsNullOrWhiteSpace(value));
        return value!;
    }

    private static string IdempotencyKey() => "identity-pg-" + Guid.NewGuid().ToString("N");

    private static GameNetDbContext CreateContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<GameNetDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        return new GameNetDbContext(options);
    }

    private static OperatorManagementService CreateService(
        GameNetDbContext db,
        IGameClock clock,
        IOperatorManagementRepository? repository = null) =>
        new(
            repository ?? new EfOperatorManagementRepository(db),
            new EfIdentityRepository(db),
            new Pbkdf2PasswordHasher(),
            new EfTransactionCoordinator(db),
            new EfIdempotencyStore(db, clock),
            new HmacRequestFingerprint(new TestFingerprintSecret(FingerprintKey)),
            new EfAuditWriter(db),
            clock);

    private static async Task<SeededIdentity> SeedAsync(
        string connectionString,
        int ownerCount,
        bool includeCashier)
    {
        var now = DateTimeOffset.UtcNow;
        var ownerRole = Role.Create(Guid.NewGuid(), "owner", "Owner");
        var cashierRole = Role.Create(
            Guid.NewGuid(),
            "cashier-" + Guid.NewGuid().ToString("N")[..8],
            "PostgreSQL Test Cashier");
        var ownerIds = Enumerable.Range(0, ownerCount).Select(_ => Guid.NewGuid()).ToArray();
        var userIds = new List<Guid>(ownerIds);
        Guid? cashierUserId = null;

        await using var db = CreateContext(connectionString);
        db.Roles.Add(ownerRole);
        db.Roles.Add(cashierRole);

        for (var index = 0; index < ownerIds.Length; index++)
        {
            var ownerId = ownerIds[index];
            var user = OperatorUser.Create(
                ownerId,
                "owner-" + Guid.NewGuid().ToString("N")[..16],
                "PostgreSQL Test Owner " + (index + 1),
                "test-only-placeholder-hash",
                now);
            db.OperatorUsers.Add(user);
            db.UserRoles.Add(new UserRole(ownerId, ownerRole.Id));
            db.AuthSessions.Add(new AuthSession(
                Guid.NewGuid(),
                ownerId,
                "jti-" + Guid.NewGuid().ToString("N"),
                now,
                now.AddHours(1)));
        }

        if (includeCashier)
        {
            cashierUserId = Guid.NewGuid();
            userIds.Add(cashierUserId.Value);
            db.OperatorUsers.Add(OperatorUser.Create(
                cashierUserId.Value,
                "cashier-" + Guid.NewGuid().ToString("N")[..16],
                "PostgreSQL Test Cashier User",
                "test-only-placeholder-hash",
                now));
            db.UserRoles.Add(new UserRole(cashierUserId.Value, cashierRole.Id));
            db.RolePermissions.Add(new RolePermission(cashierRole.Id, "stations.read"));
            db.AuthSessions.Add(new AuthSession(
                Guid.NewGuid(),
                cashierUserId.Value,
                "jti-" + Guid.NewGuid().ToString("N"),
                now,
                now.AddHours(1)));
        }

        await db.SaveChangesAsync();
        return new SeededIdentity(
            ownerRole.Id,
            cashierRole.Id,
            cashierRole.Code,
            ownerIds,
            userIds.ToArray(),
            cashierUserId);
    }

    private static async Task CleanupAsync(
        string connectionString,
        SeededIdentity seeded,
        params string[] idempotencyKeys)
    {
        await using var db = CreateContext(connectionString);

        foreach (var key in idempotencyKeys)
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM idempotency_records WHERE key = {key}");

        foreach (var userId in seeded.UserIds)
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM auth_sessions WHERE user_id = {userId}");
            var textId = userId.ToString("D");
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM audit_entries WHERE actor_id = {textId} OR reference_id = {textId}");
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM user_roles WHERE user_id = {userId}");
        }

        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM user_roles WHERE role_id = {seeded.OwnerRoleId} OR role_id = {seeded.CashierRoleId}");
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM role_permissions WHERE role_id = {seeded.OwnerRoleId} OR role_id = {seeded.CashierRoleId}");

        foreach (var userId in seeded.UserIds)
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM operator_users WHERE id = {userId}");

        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM roles WHERE id = {seeded.OwnerRoleId} OR id = {seeded.CashierRoleId}");
    }

    private sealed record SeededIdentity(
        Guid OwnerRoleId,
        Guid CashierRoleId,
        string CashierRoleCode,
        Guid[] OwnerIds,
        Guid[] UserIds,
        Guid? CashierUserId);

    private sealed class OwnerCountBarrier
    {
        private readonly TaskCompletionSource<bool> bothInitialReads =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int totalReadCount;

        public int InitialReadCount => Math.Min(Volatile.Read(ref totalReadCount), 2);
        public int TotalReadCount => Volatile.Read(ref totalReadCount);

        public async Task WaitForBothInitialReadsAsync(CancellationToken cancellationToken)
        {
            var arrival = Interlocked.Increment(ref totalReadCount);
            if (arrival == 2) bothInitialReads.TrySetResult(true);
            if (arrival <= 2)
                await bothInitialReads.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
        }
    }

    private sealed class OwnerCountBarrierRepository(
        IOperatorManagementRepository inner,
        OwnerCountBarrier barrier) : IOperatorManagementRepository
    {
        public Task<IReadOnlyList<OperatorUserSnapshot>> ListUsersAsync(CancellationToken cancellationToken) =>
            inner.ListUsersAsync(cancellationToken);
        public Task<IReadOnlyList<RoleSnapshot>> ListRolesAsync(CancellationToken cancellationToken) =>
            inner.ListRolesAsync(cancellationToken);
        public Task<OperatorUser?> FindUserByIdAsync(Guid userId, CancellationToken cancellationToken) =>
            inner.FindUserByIdAsync(userId, cancellationToken);
        public Task<Role?> FindRoleByIdAsync(Guid roleId, CancellationToken cancellationToken) =>
            inner.FindRoleByIdAsync(roleId, cancellationToken);
        public Task<bool> UsernameExistsAsync(string normalizedUsername, CancellationToken cancellationToken) =>
            inner.UsernameExistsAsync(normalizedUsername, cancellationToken);
        public Task<bool> RoleCodeExistsAsync(string normalizedCode, CancellationToken cancellationToken) =>
            inner.RoleCodeExistsAsync(normalizedCode, cancellationToken);
        public Task<bool> IsOwnerAsync(Guid userId, CancellationToken cancellationToken) =>
            inner.IsOwnerAsync(userId, cancellationToken);
        public async Task<int> CountActiveOwnersAsync(CancellationToken cancellationToken)
        {
            var count = await inner.CountActiveOwnersAsync(cancellationToken);
            await barrier.WaitForBothInitialReadsAsync(cancellationToken);
            return count;
        }
        public Task<IReadOnlySet<string>> GetRolePermissionsAsync(Guid roleId, CancellationToken cancellationToken) =>
            inner.GetRolePermissionsAsync(roleId, cancellationToken);
        public Task AddUserAsync(OperatorUser user, CancellationToken cancellationToken) =>
            inner.AddUserAsync(user, cancellationToken);
        public Task AddRoleAsync(Role role, CancellationToken cancellationToken) =>
            inner.AddRoleAsync(role, cancellationToken);
        public Task AddUserRoleAsync(UserRole userRole, CancellationToken cancellationToken) =>
            inner.AddUserRoleAsync(userRole, cancellationToken);
        public Task AddRolePermissionAsync(RolePermission permission, CancellationToken cancellationToken) =>
            inner.AddRolePermissionAsync(permission, cancellationToken);
        public Task ReplaceRolePermissionsAsync(Guid roleId, IReadOnlyCollection<string> permissions, CancellationToken cancellationToken) =>
            inner.ReplaceRolePermissionsAsync(roleId, permissions, cancellationToken);
        public Task ReplaceUserRoleAsync(Guid userId, Guid roleId, CancellationToken cancellationToken) =>
            inner.ReplaceUserRoleAsync(userId, roleId, cancellationToken);
        public Task RevokeActiveSessionsAsync(Guid userId, DateTimeOffset revokedAtUtc, CancellationToken cancellationToken) =>
            inner.RevokeActiveSessionsAsync(userId, revokedAtUtc, cancellationToken);
        public Task RevokeOtherActiveSessionsAsync(Guid userId, string currentSessionJti, DateTimeOffset revokedAtUtc, CancellationToken cancellationToken) =>
            inner.RevokeOtherActiveSessionsAsync(userId, currentSessionJti, revokedAtUtc, cancellationToken);
    }

    private sealed record TestFingerprintSecret(string FingerprintKey) : IIdempotencyFingerprintSecret;

    private sealed class FixedClock(DateTimeOffset utcNow) : IGameClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }
}
