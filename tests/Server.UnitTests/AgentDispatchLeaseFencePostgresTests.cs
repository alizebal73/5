using System.Security.Cryptography;
using System.Text;
using GameNet.Server.Infrastructure.Audit;
using GameNet.Server.Infrastructure.Realtime;
using GameNet.Server.Infrastructure.Security;
using GameNet.Server.Infrastructure.Transactions;
using GameNet.Server.Persistence;
using GameNet.Server.Persistence.Entities;
using GameNet.Shared.Contracts.V1.Security;
using GameNet.Shared.Primitives;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace GameNet.Server.UnitTests;

/// <summary>
/// Uses only the disposable PostgreSQL database created by Foundation certification.
/// The test exercises the same lease-row delete used by credential revoke/rotation.
/// </summary>
[Collection("Agent dispatch fence PostgreSQL serial")]
public sealed class AgentDispatchLeaseFencePostgresTests
{
    [Fact]
    public async Task Revocation_that_wins_first_prevents_outbound_send()
    {
        if (!IsExplicitlyEnabled())
            return;

        var connectionString = GetConnectionString();
        var clock = new FixedClock(DateTimeOffset.UtcNow);
        var deviceId = DeviceId();
        var connectionId = "connection-" + Guid.NewGuid().ToString("N");
        var leaseToken = Guid.NewGuid().ToString("N");
        var seeded = await SeedAsync(connectionString, deviceId, connectionId, leaseToken, clock.UtcNow);

        try
        {
            await using (var revokeDb = CreateContext(connectionString))
            {
                var revoked = await CreateCredentialService(revokeDb, clock).RevokeAsync(
                    new AgentCredentialRevokeRequest(deviceId, "dispatch-fence-test"));
                Assert.True(revoked);
            }

            await using var dispatchDb = CreateContext(connectionString);
            var store = CreateLeaseStore(dispatchDb, clock);
            var sends = 0;
            var accepted = await store.SendIfCurrentLeaseAsync(
                deviceId,
                connectionId,
                leaseToken,
                _ =>
                {
                    Interlocked.Increment(ref sends);
                    return Task.CompletedTask;
                });

            Assert.False(accepted);
            Assert.Equal(0, Volatile.Read(ref sends));

            await using var verifyDb = CreateContext(connectionString);
            Assert.False(await verifyDb.AgentConnectionLeases.AnyAsync(x => x.DeviceId == deviceId));
            Assert.True(await verifyDb.AgentCredentials.AnyAsync(
                x => x.DeviceId == deviceId && x.RevokedAtUtc != null));
            Assert.True(await verifyDb.AgentEnrollmentTokens.AnyAsync(
                x => x.Id == seeded.PendingTokenId && x.RevokedAtUtc != null));

            await using var redeemDb = CreateContext(connectionString);
            var enrollment = new AgentEnrollmentService(
                redeemDb, new EfTransactionCoordinator(redeemDb), new EfAuditWriter(redeemDb), clock);
            var redemption = await enrollment.RedeemAsync(
                new AgentEnrollmentRedeemRequest(deviceId, seeded.PendingTokenText),
                "agent-dispatch-fence-test");
            Assert.Null(redemption);
        }
        finally
        {
            await CleanupAsync(connectionString, deviceId);
        }
    }

    [Fact]
    public async Task Late_lease_created_after_revocation_cannot_authorize_a_send()
    {
        if (!IsExplicitlyEnabled())
            return;

        var connectionString = GetConnectionString();
        var clock = new FixedClock(DateTimeOffset.UtcNow);
        var deviceId = DeviceId();
        var initialConnectionId = "connection-" + Guid.NewGuid().ToString("N");
        var initialLeaseToken = Guid.NewGuid().ToString("N");
        var seeded = await SeedAsync(connectionString, deviceId, initialConnectionId, initialLeaseToken, clock.UtcNow);

        try
        {
            await using (var revokeDb = CreateContext(connectionString))
            {
                var revoked = await CreateCredentialService(revokeDb, clock).RevokeAsync(
                    new AgentCredentialRevokeRequest(deviceId, "dispatch-fence-test"));
                Assert.True(revoked);
            }

            // Model a ConnectAsync that passed its first credential check before the
            // revoke but inserts its lease after revoke's lease delete committed.
            var lateConnectionId = "late-connection-" + Guid.NewGuid().ToString("N");
            var lateLeaseToken = Guid.NewGuid().ToString("N");
            await using (var lateLeaseDb = CreateContext(connectionString))
            {
                lateLeaseDb.AgentConnectionLeases.Add(new AgentConnectionLease
                {
                    DeviceId = deviceId,
                    ConnectionId = lateConnectionId,
                    LeaseToken = lateLeaseToken,
                    LeaseExpiresAtUtc = clock.UtcNow.AddMinutes(1),
                    UpdatedAtUtc = clock.UtcNow,
                    LastHeartbeatAtUtc = clock.UtcNow,
                    AgentVersion = "fence-test",
                    StationState = "Ready"
                });
                await lateLeaseDb.SaveChangesAsync();
            }

            await using var dispatchDb = CreateContext(connectionString);
            var store = CreateLeaseStore(dispatchDb, clock);
            var sends = 0;
            var accepted = await store.SendIfCurrentLeaseAsync(
                deviceId,
                lateConnectionId,
                lateLeaseToken,
                _ =>
                {
                    Interlocked.Increment(ref sends);
                    return Task.CompletedTask;
                });

            Assert.False(accepted);
            Assert.Equal(0, Volatile.Read(ref sends));
            Assert.False(await dispatchDb.AgentCredentials.AnyAsync(
                x => x.DeviceId == deviceId && x.RevokedAtUtc == null));
            Assert.True(await dispatchDb.AgentEnrollmentTokens.AnyAsync(
                x => x.Id == seeded.PendingTokenId && x.RevokedAtUtc != null));
        }
        finally
        {
            await CleanupAsync(connectionString, deviceId);
        }
    }

    [Fact]
    public async Task Revocation_waits_for_an_in_progress_send_attempt_then_removes_lease()
    {
        if (!IsExplicitlyEnabled())
            return;

        var connectionString = GetConnectionString();
        var clock = new FixedClock(DateTimeOffset.UtcNow);
        var deviceId = DeviceId();
        var connectionId = "connection-" + Guid.NewGuid().ToString("N");
        var leaseToken = Guid.NewGuid().ToString("N");
        var seeded = await SeedAsync(connectionString, deviceId, connectionId, leaseToken, clock.UtcNow);

        var sendEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowSendToFinish = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<bool>? dispatchTask = null;
        Task<bool>? revokeTask = null;

        try
        {
            await using var dispatchDb = CreateContext(connectionString);
            var store = CreateLeaseStore(dispatchDb, clock);
            dispatchTask = store.SendIfCurrentLeaseAsync(
                deviceId,
                connectionId,
                leaseToken,
                async token =>
                {
                    sendEntered.TrySetResult(true);
                    await allowSendToFinish.Task.WaitAsync(token);
                });

            await sendEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));

            await using var revokeDb = CreateContext(connectionString);
            await revokeDb.Database.OpenConnectionAsync();
            var revokeConnection = revokeDb.Database.GetDbConnection();
            int revokeBackendPid;
            await using (var pidCommand = revokeConnection.CreateCommand())
            {
                pidCommand.CommandText = "SELECT pg_backend_pid()";
                revokeBackendPid = Convert.ToInt32(await pidCommand.ExecuteScalarAsync());
            }

            revokeTask = CreateCredentialService(revokeDb, clock).RevokeAsync(
                new AgentCredentialRevokeRequest(deviceId, "dispatch-fence-test"));

            // Observe PostgreSQL's real lock wait rather than inferring a race from Task.Delay.
            await AssertBackendIsBlockedOnLockAsync(connectionString, revokeBackendPid);

            Assert.False(dispatchTask.IsCompleted);
            Assert.False(revokeTask.IsCompleted);

            allowSendToFinish.TrySetResult(true);
            Assert.True(await dispatchTask.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.True(await revokeTask.WaitAsync(TimeSpan.FromSeconds(5)));

            var attemptsAfterRevoke = 0;
            var sentAfterRevoke = await store.SendIfCurrentLeaseAsync(
                deviceId,
                connectionId,
                leaseToken,
                _ =>
                {
                    Interlocked.Increment(ref attemptsAfterRevoke);
                    return Task.CompletedTask;
                });
            Assert.False(sentAfterRevoke);
            Assert.Equal(0, Volatile.Read(ref attemptsAfterRevoke));

            await using var verifyDb = CreateContext(connectionString);
            Assert.False(await verifyDb.AgentConnectionLeases.AnyAsync(x => x.DeviceId == deviceId));
            Assert.True(await verifyDb.AgentCredentials.AnyAsync(
                x => x.DeviceId == deviceId && x.RevokedAtUtc != null));
            Assert.True(await verifyDb.AgentEnrollmentTokens.AnyAsync(
                x => x.Id == seeded.PendingTokenId && x.RevokedAtUtc != null));
        }
        finally
        {
            // Always release the fake transport gate, even when an assertion fails.
            allowSendToFinish.TrySetResult(true);
            if (dispatchTask is not null)
            {
                try { await dispatchTask.WaitAsync(TimeSpan.FromSeconds(7)); }
                catch { /* Preserve the original test failure; cleanup follows. */ }
            }
            if (revokeTask is not null)
            {
                try { await revokeTask.WaitAsync(TimeSpan.FromSeconds(7)); }
                catch { /* Preserve the original test failure; cleanup follows. */ }
            }
            await CleanupAsync(connectionString, deviceId);
        }
    }

    private static bool IsExplicitlyEnabled() =>
        string.Equals(
            Environment.GetEnvironmentVariable("GAMENET_AGENT_DISPATCH_POSTGRES_TESTS"),
            "true",
            StringComparison.OrdinalIgnoreCase);

    private static string GetConnectionString()
    {
        var value = Environment.GetEnvironmentVariable("GAMENET_DATABASE_CONNECTION");
        Assert.False(string.IsNullOrWhiteSpace(value));
        return value!;
    }

    private static string DeviceId() => "pc-" + Guid.NewGuid().ToString("N")[..12];

    private static GameNetDbContext CreateContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<GameNetDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        return new GameNetDbContext(options);
    }

    private static EfAgentConnectionLeaseStore CreateLeaseStore(
        GameNetDbContext db,
        IGameClock clock) =>
        new(db, clock, new EfTransactionCoordinator(db));

    private static AgentCredentialService CreateCredentialService(
        GameNetDbContext db,
        IGameClock clock) =>
        new(db, new EfTransactionCoordinator(db), new EfAuditWriter(db), clock);

    private static async Task<SeededDevice> SeedAsync(
        string connectionString,
        string deviceId,
        string connectionId,
        string leaseToken,
        DateTimeOffset now)
    {
        var pendingTokenText = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var pendingTokenHash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(pendingTokenText)));
        var pendingTokenId = Guid.NewGuid();

        await using var db = CreateContext(connectionString);
        db.AgentCredentials.Add(new AgentCredential
        {
            DeviceId = deviceId,
            SecretHash = new string('a', 64),
            CreatedAtUtc = now
        });
        db.AgentConnectionLeases.Add(new AgentConnectionLease
        {
            DeviceId = deviceId,
            ConnectionId = connectionId,
            LeaseToken = leaseToken,
            LeaseExpiresAtUtc = now.AddMinutes(1),
            UpdatedAtUtc = now,
            LastHeartbeatAtUtc = now,
            AgentVersion = "fence-test",
            StationState = "Ready"
        });
        db.AgentEnrollmentTokens.Add(AgentEnrollmentToken.Create(
            pendingTokenId,
            deviceId,
            pendingTokenHash,
            Guid.NewGuid(),
            now,
            now.AddMinutes(15)));
        await db.SaveChangesAsync();
        return new SeededDevice(pendingTokenId, pendingTokenText);
    }

    private sealed record SeededDevice(Guid PendingTokenId, string PendingTokenText);

    private static async Task AssertBackendIsBlockedOnLockAsync(
        string connectionString,
        int backendPid)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await using var observer = new NpgsqlConnection(connectionString);
        await observer.OpenAsync(timeout.Token);

        while (true)
        {
            await using var command = new NpgsqlCommand(
                "SELECT cardinality(pg_blocking_pids(@pid)) > 0",
                observer);
            command.Parameters.AddWithValue("pid", backendPid);
            var blocked = (bool)(await command.ExecuteScalarAsync(timeout.Token))!;
            if (blocked)
                return;

            await Task.Delay(TimeSpan.FromMilliseconds(20), timeout.Token);
        }
    }

    private static async Task CleanupAsync(string connectionString, string deviceId)
    {
        await using var db = CreateContext(connectionString);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM agent_connection_leases WHERE device_id = {deviceId}");
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM agent_enrollment_tokens WHERE device_id = {deviceId}");
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM agent_credentials WHERE device_id = {deviceId}");
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM audit_entries WHERE actor_id = {deviceId}");
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IGameClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }
}
