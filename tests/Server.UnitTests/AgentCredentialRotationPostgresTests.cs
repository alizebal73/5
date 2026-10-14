using System.Security.Cryptography;
using System.Text;
using GameNet.Server.Infrastructure.Audit;
using GameNet.Server.Infrastructure.Security;
using GameNet.Server.Infrastructure.Transactions;
using GameNet.Server.Persistence;
using GameNet.Server.Persistence.Entities;
using GameNet.Shared.Contracts.V1.Security;
using GameNet.Shared.Primitives;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GameNet.Server.UnitTests;

[Collection("Agent credential rotation PostgreSQL serial")]
public sealed class AgentCredentialRotationPostgresTests
{
    [Fact]
    public async Task Rotation_is_idempotent_and_old_credential_remains_active_until_commit()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("GAMENET_AGENT_CREDENTIAL_ROTATION_POSTGRES_TESTS"),
                "true",
                StringComparison.Ordinal))
            return;
        var connectionString = Environment.GetEnvironmentVariable("GAMENET_DATABASE_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString))
            return; // PostgreSQL behavior is exercised by the Foundation verification lane.

        var deviceId = "rotation-" + Guid.NewGuid().ToString("N");
        var operationId = Guid.NewGuid();
        var currentSecret = GenerateSecret();
        var replacementSecret = GenerateSecret();
        var changedSecret = GenerateSecret();
        var oldCredentialId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var options = new DbContextOptionsBuilder<GameNetDbContext>().UseNpgsql(connectionString).Options;

        try
        {
            await using (var seed = new GameNetDbContext(options))
            {
                seed.AgentCredentials.Add(new AgentCredential
                {
                    Id = oldCredentialId,
                    DeviceId = deviceId,
                    SecretHash = Hash(currentSecret),
                    CreatedAtUtc = now
                });
                await seed.SaveChangesAsync();
            }

            await using (var requestDb = new GameNetDbContext(options))
            {
                var service = CreateService(requestDb, now);
                var requested = await service.RequestRotationAsync(new AgentCredentialRotateRequest(deviceId, operationId));
                Assert.Equal("requested", requested.State);
                var requestReplay = await service.RequestRotationAsync(new AgentCredentialRotateRequest(deviceId, operationId));
                Assert.Equal("requested", requestReplay.State);
                Assert.Equal(requested.ExpiresAtUtc, requestReplay.ExpiresAtUtc);
            }

            await using (var stageDb = new GameNetDbContext(options))
            {
                var service = CreateService(stageDb, now.AddSeconds(1));
                var staged = await service.StageRotationAsync(new AgentCredentialRotationStageRequest(
                    operationId, deviceId, currentSecret, replacementSecret));
                Assert.Equal("staged", staged.State);
            }

            await using (var checkDb = new GameNetDbContext(options))
            {
                var service = CreateService(checkDb, now.AddSeconds(2));
                Assert.True(await service.AuthenticateAsync(deviceId, currentSecret));
                Assert.False(await service.AuthenticateAsync(deviceId, replacementSecret));
                var current = await checkDb.AgentCredentials.SingleAsync(
                    x => x.DeviceId == deviceId && x.RevokedAtUtc == null && !x.IsPendingRotation);
                Assert.Equal(oldCredentialId, current.Id);
                var staged = await checkDb.AgentCredentials.SingleAsync(x => x.RotationOperationId == operationId);
                Assert.True(staged.IsPendingRotation);
                Assert.NotEqual(current.Id, staged.Id);
            }

            await using (var replayDb = new GameNetDbContext(options))
            {
                var service = CreateService(replayDb, now.AddSeconds(3));
                var replay = await service.StageRotationAsync(new AgentCredentialRotationStageRequest(
                    operationId, deviceId, currentSecret, replacementSecret));
                Assert.Equal("staged", replay.State);
                await Assert.ThrowsAsync<AgentCredentialException>(() => service.StageRotationAsync(
                    new AgentCredentialRotationStageRequest(operationId, deviceId, currentSecret, changedSecret)));
            }

            Guid newCredentialId;
            await using (var confirmDb = new GameNetDbContext(options))
            {
                var service = CreateService(confirmDb, now.AddSeconds(4));
                var committed = await service.ConfirmRotationAsync(new AgentCredentialRotationConfirmationRequest(
                    operationId, deviceId, currentSecret, replacementSecret));
                Assert.Equal("committed", committed.State);
                newCredentialId = committed.CredentialId;
                Assert.True(await service.AuthenticateAsync(deviceId, replacementSecret));
                Assert.False(await service.AuthenticateAsync(deviceId, currentSecret));
            }

            await using (var replayConfirmDb = new GameNetDbContext(options))
            {
                var service = CreateService(replayConfirmDb, now.AddSeconds(5));
                var replay = await service.ConfirmRotationAsync(new AgentCredentialRotationConfirmationRequest(
                    operationId, deviceId, currentSecret, replacementSecret));
                Assert.Equal("already_committed", replay.State);
                Assert.Equal(newCredentialId, replay.CredentialId);
                Assert.False(await replayConfirmDb.AgentCredentials.AnyAsync(
                    x => x.DeviceId == deviceId && x.RevokedAtUtc == null && x.IsPendingRotation));
                Assert.Single(await replayConfirmDb.AgentCredentials.Where(
                    x => x.DeviceId == deviceId && x.RevokedAtUtc == null && !x.IsPendingRotation).ToListAsync());
            }
        }
        finally
        {
            await using var cleanup = new GameNetDbContext(options);
            await cleanup.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM agent_connection_leases WHERE device_id = {deviceId}");
            await cleanup.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM agent_enrollment_tokens WHERE device_id = {deviceId}");
            var credentialGuids = await cleanup.AgentCredentials
                .Where(x => x.DeviceId == deviceId)
                .Select(x => x.Id)
                .ToListAsync();
            var credentialIds = credentialGuids.Select(id => id.ToString("N")).ToList();
            credentialIds.Add(oldCredentialId.ToString("N"));
            credentialIds.Add(operationId.ToString("N"));
            await cleanup.AuditEntries
                .Where(x => x.ReferenceId != null && credentialIds.Contains(x.ReferenceId))
                .ExecuteDeleteAsync();
            await cleanup.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM agent_credentials WHERE device_id = {deviceId}");
        }
    }

    [Fact]
    public async Task Expired_unstaged_request_keeps_prior_credential_active_and_clears_only_its_signal()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("GAMENET_AGENT_CREDENTIAL_ROTATION_POSTGRES_TESTS"),
                "true",
                StringComparison.Ordinal))
            return;
        var connectionString = Environment.GetEnvironmentVariable("GAMENET_DATABASE_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString))
            return;

        var deviceId = "rotation-expired-" + Guid.NewGuid().ToString("N");
        var operationId = Guid.NewGuid();
        var currentSecret = GenerateSecret();
        var oldCredentialId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var options = new DbContextOptionsBuilder<GameNetDbContext>().UseNpgsql(connectionString).Options;

        try
        {
            await using (var seed = new GameNetDbContext(options))
            {
                seed.AgentCredentials.Add(new AgentCredential
                {
                    Id = oldCredentialId,
                    DeviceId = deviceId,
                    SecretHash = Hash(currentSecret),
                    CreatedAtUtc = now
                });
                await seed.SaveChangesAsync();
            }

            await using (var requestDb = new GameNetDbContext(options))
            {
                var requested = await CreateService(requestDb, now).RequestRotationAsync(
                    new AgentCredentialRotateRequest(deviceId, operationId));
                Assert.Equal("requested", requested.State);
            }

            await using (var expiredDb = new GameNetDbContext(options))
            {
                var result = await CreateService(expiredDb, now.AddMinutes(11)).StageRotationAsync(
                    new AgentCredentialRotationStageRequest(
                        operationId, deviceId, currentSecret, GenerateSecret()));
                Assert.Equal("expired", result.State);
            }

            await using (var verifyDb = new GameNetDbContext(options))
            {
                var service = CreateService(verifyDb, now.AddMinutes(12));
                Assert.True(await service.AuthenticateAsync(deviceId, currentSecret));
                Assert.Null(await service.GetRequestedRotationOperationIdAsync(deviceId, oldCredentialId));
                Assert.False(await verifyDb.AgentCredentials.AnyAsync(
                    x => x.DeviceId == deviceId && x.RotationOperationId == operationId));
                Assert.Single(await verifyDb.AgentCredentials.Where(
                    x => x.DeviceId == deviceId && x.RevokedAtUtc == null && !x.IsPendingRotation).ToListAsync());

                var sameExpiredOperation = await service.RequestRotationAsync(
                    new AgentCredentialRotateRequest(deviceId, operationId));
                Assert.Equal("expired", sameExpiredOperation.State);
                Assert.Null(await service.GetRequestedRotationOperationIdAsync(deviceId, oldCredentialId));
            }
        }
        finally
        {
            await using var cleanup = new GameNetDbContext(options);
            await cleanup.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM agent_connection_leases WHERE device_id = {deviceId}");
            await cleanup.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM agent_enrollment_tokens WHERE device_id = {deviceId}");
            var ids = await cleanup.AgentCredentials.Where(x => x.DeviceId == deviceId)
                .Select(x => x.Id).ToListAsync();
            var refs = ids.Select(id => id.ToString("N")).Append(oldCredentialId.ToString("N")).Append(operationId.ToString("N")).ToList();
            await cleanup.AuditEntries.Where(x => x.ReferenceId != null && refs.Contains(x.ReferenceId))
                .ExecuteDeleteAsync();
            await cleanup.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM agent_credentials WHERE device_id = {deviceId}");
        }
    }

    [Fact]
    public async Task Revoking_device_cancels_and_audits_pending_rotation_even_when_old_credential_is_removed()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("GAMENET_AGENT_CREDENTIAL_ROTATION_POSTGRES_TESTS"),
                "true",
                StringComparison.Ordinal))
            return;
        var connectionString = Environment.GetEnvironmentVariable("GAMENET_DATABASE_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString))
            return;

        var deviceId = "rotation-revoke-" + Guid.NewGuid().ToString("N");
        var operationId = Guid.NewGuid();
        var currentSecret = GenerateSecret();
        var replacementSecret = GenerateSecret();
        var oldCredentialId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var options = new DbContextOptionsBuilder<GameNetDbContext>().UseNpgsql(connectionString).Options;

        try
        {
            await using (var seed = new GameNetDbContext(options))
            {
                seed.AgentCredentials.Add(new AgentCredential
                {
                    Id = oldCredentialId,
                    DeviceId = deviceId,
                    SecretHash = Hash(currentSecret),
                    CreatedAtUtc = now
                });
                await seed.SaveChangesAsync();
            }

            await using (var stageDb = new GameNetDbContext(options))
            {
                var service = CreateService(stageDb, now);
                Assert.Equal("requested", (await service.RequestRotationAsync(
                    new AgentCredentialRotateRequest(deviceId, operationId))).State);
                Assert.Equal("staged", (await service.StageRotationAsync(
                    new AgentCredentialRotationStageRequest(
                        operationId, deviceId, currentSecret, replacementSecret))).State);
            }

            Guid pendingCredentialId;
            await using (var revokeDb = new GameNetDbContext(options))
            {
                var service = CreateService(revokeDb, now.AddSeconds(1));
                Assert.True(await service.RevokeAsync(new AgentCredentialRevokeRequest(
                    deviceId, "test-only security revocation")));
                var pending = await revokeDb.AgentCredentials.SingleAsync(
                    x => x.DeviceId == deviceId && x.RotationOperationId == operationId);
                pendingCredentialId = pending.Id;
                Assert.NotNull(pending.RevokedAtUtc);
                Assert.True(await revokeDb.AgentCredentials.Where(x => x.DeviceId == deviceId)
                    .AllAsync(x => x.RevokedAtUtc != null));
            }

            await using (var auditDb = new GameNetDbContext(options))
            {
                Assert.Single(await auditDb.AuditEntries.Where(x =>
                    x.Operation == "AgentCredential.RotationRevoked" &&
                    x.ReferenceId == pendingCredentialId.ToString("N")).ToListAsync());
                Assert.False(await CreateService(auditDb, now.AddSeconds(2))
                    .AuthenticateAsync(deviceId, currentSecret));
                Assert.False(await CreateService(auditDb, now.AddSeconds(2))
                    .AuthenticateAsync(deviceId, replacementSecret));
            }
        }
        finally
        {
            await using var cleanup = new GameNetDbContext(options);
            await cleanup.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM agent_connection_leases WHERE device_id = {deviceId}");
            await cleanup.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM agent_enrollment_tokens WHERE device_id = {deviceId}");
            var ids = await cleanup.AgentCredentials.Where(x => x.DeviceId == deviceId)
                .Select(x => x.Id).ToListAsync();
            var refs = ids.Select(id => id.ToString("N")).Append(oldCredentialId.ToString("N"))
                .Append(operationId.ToString("N")).ToList();
            await cleanup.AuditEntries.Where(x => x.ReferenceId != null && refs.Contains(x.ReferenceId))
                .ExecuteDeleteAsync();
            await cleanup.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM agent_credentials WHERE device_id = {deviceId}");
        }
    }


    [Fact]
    public async Task Concurrent_rotation_requests_for_one_device_allow_only_one_live_operation()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("GAMENET_AGENT_CREDENTIAL_ROTATION_POSTGRES_TESTS"),
                "true",
                StringComparison.Ordinal))
            return;

        var connectionString = Environment.GetEnvironmentVariable("GAMENET_DATABASE_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString))
            return;

        var deviceId = "rotation-concurrent-" + Guid.NewGuid().ToString("N");
        var operationIds = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var operationReferences = operationIds.Select(id => id.ToString("N")).ToArray();
        var currentSecret = GenerateSecret();
        var oldCredentialId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var options = new DbContextOptionsBuilder<GameNetDbContext>().UseNpgsql(connectionString).Options;

        try
        {
            await using (var seed = new GameNetDbContext(options))
            {
                seed.AgentCredentials.Add(new AgentCredential
                {
                    Id = oldCredentialId,
                    DeviceId = deviceId,
                    SecretHash = Hash(currentSecret),
                    CreatedAtUtc = now
                });
                await seed.SaveChangesAsync();
            }

            static async Task<string> RequestAsync(
                DbContextOptions<GameNetDbContext> options,
                string deviceId,
                Guid operationId,
                DateTimeOffset now)
            {
                await using var db = new GameNetDbContext(options);
                try
                {
                    var response = await CreateService(db, now).RequestRotationAsync(
                        new AgentCredentialRotateRequest(deviceId, operationId));
                    return response.State;
                }
                catch (AgentCredentialException exception)
                {
                    return exception.Code;
                }
            }

            var outcomes = await Task.WhenAll(
                Task.Run(() => RequestAsync(options, deviceId, operationIds[0], now)),
                Task.Run(() => RequestAsync(options, deviceId, operationIds[1], now)));

            Assert.Single(outcomes, outcome => outcome == "requested");
            Assert.Single(outcomes, outcome => outcome == "agent.rotation_in_progress");

            await using (var verify = new GameNetDbContext(options))
            {
                var current = await verify.AgentCredentials.SingleAsync(
                    x => x.DeviceId == deviceId && x.RevokedAtUtc == null && !x.IsPendingRotation);
                Assert.Equal(oldCredentialId, current.Id);
                Assert.True(operationIds.Contains(current.RotationRequestedOperationId ?? Guid.Empty));
                Assert.Equal(1, await verify.AuditEntries.CountAsync(x =>
                    x.ReferenceType == "AgentCredentialRotationRequest" &&
                    x.ReferenceId != null &&
                    operationReferences.Contains(x.ReferenceId)));
                Assert.False(await verify.AgentCredentials.AnyAsync(
                    x => x.DeviceId == deviceId && x.IsPendingRotation && x.RevokedAtUtc == null));
            }
        }
        finally
        {
            await using var cleanup = new GameNetDbContext(options);
            await cleanup.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM agent_connection_leases WHERE device_id = {deviceId}");
            await cleanup.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM agent_enrollment_tokens WHERE device_id = {deviceId}");
            var ids = await cleanup.AgentCredentials.Where(x => x.DeviceId == deviceId)
                .Select(x => x.Id).ToListAsync();
            var references = ids.Select(id => id.ToString("N"))
                .Concat(operationIds.Select(id => id.ToString("N")))
                .Append(oldCredentialId.ToString("N"))
                .ToList();
            await cleanup.AuditEntries.Where(x =>
                x.ReferenceId != null && references.Contains(x.ReferenceId)).ExecuteDeleteAsync();
            await cleanup.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM agent_credentials WHERE device_id = {deviceId}");
        }
    }

    private static AgentCredentialService CreateService(GameNetDbContext db, DateTimeOffset now) =>
        new(db, new EfTransactionCoordinator(db), new EfAuditWriter(db), new FixedClock(now));

    private static string GenerateSecret()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        try { return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_'); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    private static string Hash(string secret)
    {
        var bytes = Encoding.UTF8.GetBytes(secret);
        try { return Convert.ToHexString(SHA256.HashData(bytes)); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IGameClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }
}
