using System.Security.Cryptography;
using System.Text;
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
            var secret = GenerateSecret();
            var entity = new AgentCredential
            {
                DeviceId = request.DeviceId,
                SecretHash = HashSecret(secret),
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
            var secret = GenerateSecret();
            var replacement = new AgentCredential
            {
                DeviceId = request.DeviceId,
                SecretHash = HashSecret(secret),
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

            current.Revoke(clock.UtcNow);
            auditWriter.Append(new AuditRecord(
                clock.UtcNow,
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

    public Task<bool> AuthenticateAsync(string deviceId, string secret, CancellationToken cancellationToken = default) =>
        transactions.ExecuteAsync(async ct =>
        {
            ValidateDeviceId(deviceId);
            if (string.IsNullOrWhiteSpace(secret))
                return false;

            var credential = await dbContext.AgentCredentials
                .Where(x => x.DeviceId == deviceId && x.RevokedAtUtc == null)
                .SingleOrDefaultAsync(ct);

            var valid = credential is not null &&
                CryptographicOperations.FixedTimeEquals(
                    Convert.FromHexString(credential.SecretHash),
                    Convert.FromHexString(HashSecret(secret)));

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
                return false;
            }

            credential!.LastAuthenticatedAtUtc = clock.UtcNow;
            return true;
        }, cancellationToken);

    private static void ValidateDeviceId(string deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId) || deviceId.Length > 128)
            throw new ArgumentException("DeviceId must be 1-128 non-whitespace characters.", nameof(deviceId));
    }

    private static string GenerateSecret() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private static string HashSecret(string secret) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(secret)));
}
