using System.Security.Cryptography;
using System.Text.Json;
using GameNet.Server.Infrastructure.Audit;
using GameNet.Server.Infrastructure.Transactions;
using GameNet.Server.Persistence;
using GameNet.Server.Persistence.Entities;
using GameNet.Shared.Contracts.V1.Security;
using GameNet.Shared.Primitives;
using Microsoft.EntityFrameworkCore;

namespace GameNet.Server.Infrastructure.Security;

public sealed class AgentCredentialService(
    GameNetDbContext dbContext,
    ITransactionCoordinator transactions,
    IAuditWriter auditWriter,
    IGameClock clock) : IAgentCredentialService
{
    public Task<AgentCredentialSecretResponse> ProvisionAsync(
        AgentCredentialProvisionRequest request,
        CancellationToken cancellationToken = default) =>
        transactions.ExecuteAsync(async ct =>
        {
            ValidateDeviceId(request.DeviceId);
            var exists = await dbContext.AgentCredentials.AnyAsync(
                x => x.DeviceId == request.DeviceId && x.RevokedAtUtc == null, ct);

            if (exists)
                throw new AgentCredentialException("agent.credential_exists");

            var now = clock.UtcNow;
            var secret = AgentCredentialSecretMaterial.Generate();
            var entity = new AgentCredential
            {
                DeviceId = request.DeviceId,
                SecretHash = AgentCredentialSecretMaterial.Hash(secret),
                CreatedAtUtc = now
            };

            dbContext.AgentCredentials.Add(entity);
            auditWriter.Append(new AuditRecord(
                now,
                "System",
                request.DeviceId,
                "AgentCredential.Provisioned",
                "AgentCredential",
                entity.Id.ToString("N"),
                "Agent credential provisioned.",
                "foundation",
                "Server",
                "Succeeded"));

            return new AgentCredentialSecretResponse(entity.Id, entity.DeviceId, secret, now);
        }, cancellationToken);

    public Task<AgentCredentialSecretResponse> RotateAsync(
        AgentCredentialRotateRequest request,
        CancellationToken cancellationToken = default) =>
        transactions.ExecuteAsync(async ct =>
        {
            ValidateDeviceId(request.DeviceId);
            var current = await dbContext.AgentCredentials
                .Where(x => x.DeviceId == request.DeviceId && x.RevokedAtUtc == null)
                .SingleOrDefaultAsync(ct);

            if (current is null)
                throw new AgentCredentialException("agent.credential_not_found");

            var now = clock.UtcNow;
            current.Revoke(now);
            await FenceActiveLeaseAsync(request.DeviceId, ct);
            var secret = AgentCredentialSecretMaterial.Generate();
            var replacement = new AgentCredential
            {
                DeviceId = request.DeviceId,
                SecretHash = AgentCredentialSecretMaterial.Hash(secret),
                CreatedAtUtc = now
            };
            dbContext.AgentCredentials.Add(replacement);

            auditWriter.Append(new AuditRecord(
                now,
                "System",
                request.DeviceId,
                "AgentCredential.Rotated",
                "AgentCredential",
                replacement.Id.ToString("N"),
                "Agent credential rotated.",
                "foundation",
                "Server",
                "Succeeded"));

            return new AgentCredentialSecretResponse(replacement.Id, replacement.DeviceId, secret, now);
        }, cancellationToken);

    public Task<bool> RevokeAsync(
        AgentCredentialRevokeRequest request,
        CancellationToken cancellationToken = default) =>
        transactions.ExecuteAsync(async ct =>
        {
            ValidateDeviceId(request.DeviceId);
            var current = await dbContext.AgentCredentials
                .Where(x => x.DeviceId == request.DeviceId && x.RevokedAtUtc == null)
                .SingleOrDefaultAsync(ct);

            if (current is null)
                return false;

            var now = clock.UtcNow;
            current.Revoke(now);
            await FenceActiveLeaseAsync(request.DeviceId, ct);
            auditWriter.Append(new AuditRecord(
                now,
                "System",
                request.DeviceId,
                "AgentCredential.Revoked",
                "AgentCredential",
                current.Id.ToString("N"),
                request.Reason,
                "foundation",
                "Server",
                "Succeeded"));

            return true;
        }, cancellationToken);

    public async Task<bool> AuthenticateAsync(
        string deviceId,
        string secret,
        CancellationToken cancellationToken = default) =>
        (await AuthenticateAndGetCredentialIdAsync(deviceId, secret, cancellationToken)) is not null;

    public Task<Guid?> AuthenticateAndGetCredentialIdAsync(
        string deviceId,
        string secret,
        CancellationToken cancellationToken = default) =>
        transactions.ExecuteAsync<Guid?>(async ct =>
        {
            // Reject malformed identities and token shapes before hashing or querying storage.
            if (!AgentDeviceIdentityRules.IsValid(deviceId) ||
                !AgentCredentialSecretMaterial.IsCanonicalSecret(secret))
                return null;

            var credential = await dbContext.AgentCredentials
                .Where(x => x.DeviceId == deviceId && x.RevokedAtUtc == null)
                .SingleOrDefaultAsync(ct);

            var valid = false;
            byte[]? storedHash = null;
            byte[]? suppliedHash = null;
            try
            {
                if (credential is not null)
                {
                    storedHash = Convert.FromHexString(credential.SecretHash);
                    suppliedHash = Convert.FromHexString(AgentCredentialSecretMaterial.Hash(secret));
                    valid = storedHash.Length == suppliedHash.Length &&
                            CryptographicOperations.FixedTimeEquals(storedHash, suppliedHash);
                }
            }
            finally
            {
                if (storedHash is not null) CryptographicOperations.ZeroMemory(storedHash);
                if (suppliedHash is not null) CryptographicOperations.ZeroMemory(suppliedHash);
            }

            if (!valid)
            {
                auditWriter.Append(new AuditRecord(
                    clock.UtcNow,
                    "System",
                    deviceId,
                    "AgentCredential.AuthenticationRejected",
                    "AgentCredential",
                    credential?.Id.ToString("N"),
                    "Invalid or revoked Agent credential.",
                    "foundation",
                    "Server",
                    "Rejected"));
                return null;
            }

            credential!.LastAuthenticatedAtUtc = clock.UtcNow;
            return credential.Id;
        }, cancellationToken);

    public Task<bool> IsCredentialActiveAsync(
        Guid credentialId,
        string deviceId,
        CancellationToken cancellationToken = default)
    {
        if (credentialId == Guid.Empty || !AgentDeviceIdentityRules.IsValid(deviceId))
            return Task.FromResult(false);

        return dbContext.AgentCredentials
            .AsNoTracking()
            .AnyAsync(
                x => x.Id == credentialId &&
                     x.DeviceId == deviceId &&
                     x.RevokedAtUtc == null,
                cancellationToken);
    }

    private async Task FenceActiveLeaseAsync(string deviceId, CancellationToken cancellationToken)
    {
        // Revoke/rotation must immediately remove this device's authority to receive
        // commands. The delete participates in the caller's serializable transaction.
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            DELETE FROM agent_connection_leases
            WHERE device_id = {deviceId}
            """,
            cancellationToken);
    }

    private static void ValidateDeviceId(string deviceId)
    {
        if (!AgentDeviceIdentityRules.IsValid(deviceId))
            throw new AgentCredentialException("agent.device_id_invalid");
    }
}
