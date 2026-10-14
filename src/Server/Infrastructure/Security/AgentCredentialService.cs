using System.Security.Cryptography;
using GameNet.Server.Infrastructure.Audit;
using GameNet.Server.Infrastructure.Realtime;
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
    private static readonly TimeSpan RotationLifetime = TimeSpan.FromMinutes(10);

    public async Task<AgentCredentialSecretResponse> ProvisionAsync(
        AgentCredentialProvisionRequest request, CancellationToken cancellationToken = default)
    {
        ValidateDeviceId(request.DeviceId);
        await using var fence = await AgentDeviceFenceLock.AcquireAsync(dbContext, request.DeviceId, cancellationToken);
        return await transactions.ExecuteAsync(async ct =>
        {
            var exists = await dbContext.AgentCredentials.AnyAsync(
                x => x.DeviceId == request.DeviceId && x.RevokedAtUtc == null, ct);
            if (exists) throw new AgentCredentialException("agent.credential_exists");

            var now = clock.UtcNow;
            await RevokePendingEnrollmentTokensAsync(request.DeviceId, now,
                "Agent credential provisioned; prior pending enrollment token retired.", ct);
            var secret = AgentCredentialSecretMaterial.Generate();
            var entity = new AgentCredential
            {
                DeviceId = request.DeviceId,
                SecretHash = AgentCredentialSecretMaterial.Hash(secret),
                CreatedAtUtc = now
            };
            dbContext.AgentCredentials.Add(entity);
            auditWriter.Append(new AuditRecord(
                now, "System", request.DeviceId, "AgentCredential.Provisioned",
                "AgentCredential", entity.Id.ToString("N"),
                "Agent credential provisioned.", "foundation", "Server", "Succeeded"));
            return new AgentCredentialSecretResponse(entity.Id, entity.DeviceId, secret, now);
        }, cancellationToken);
    }

    public async Task<AgentCredentialRotationRequestedResponse> RequestRotationAsync(
        AgentCredentialRotateRequest request, CancellationToken cancellationToken = default)
    {
        ValidateDeviceId(request.DeviceId);
        ValidateOperationId(request.OperationId);
        await using var fence = await AgentDeviceFenceLock.AcquireAsync(dbContext, request.DeviceId, cancellationToken);
        return await transactions.ExecuteAsync(async ct =>
        {
            var now = clock.UtcNow;
            var existingOperation = await dbContext.AgentCredentials
                .SingleOrDefaultAsync(x => x.RotationOperationId == request.OperationId, ct);
            if (existingOperation is not null)
            {
                if (!string.Equals(existingOperation.DeviceId, request.DeviceId, StringComparison.Ordinal))
                    throw new AgentCredentialException("agent.rotation_operation_reused");
                if (existingOperation.RevokedAtUtc is not null ||
                    (existingOperation.IsPendingRotation && existingOperation.RotationExpiresAtUtc <= now))
                    return new AgentCredentialRotationRequestedResponse(request.OperationId, request.DeviceId, "expired", null);
                return new AgentCredentialRotationRequestedResponse(
                    request.OperationId, request.DeviceId,
                    existingOperation.IsPendingRotation ? "staged" : "committed",
                    existingOperation.RotationExpiresAtUtc);
            }

            var current = await dbContext.AgentCredentials
                .Where(x => x.DeviceId == request.DeviceId && x.RevokedAtUtc == null && !x.IsPendingRotation)
                .SingleOrDefaultAsync(ct);
            if (current is null) throw new AgentCredentialException("agent.credential_not_found");

            if (current.RotationRequestedOperationId.HasValue)
            {
                var existingId = current.RotationRequestedOperationId.Value;
                var pending = await dbContext.AgentCredentials.SingleOrDefaultAsync(
                    x => x.RotationOperationId == existingId, ct);
                var expired = current.RotationRequestedAtUtc.HasValue &&
                              current.RotationRequestedAtUtc.Value + RotationLifetime <= now;
                expired |= pending?.RotationExpiresAtUtc.HasValue == true &&
                           pending.RotationExpiresAtUtc.Value <= now;
                if (!expired)
                {
                    if (existingId == request.OperationId)
                        return new AgentCredentialRotationRequestedResponse(
                            existingId, request.DeviceId, pending is null ? "requested" :
                            pending.IsPendingRotation ? "staged" : "committed", pending?.RotationExpiresAtUtc);
                    throw new AgentCredentialException("agent.rotation_in_progress");
                }

                if (pending is { IsPendingRotation: true, RevokedAtUtc: null }) pending.Revoke(now);
                current.ClearRotationRequest();
                auditWriter.Append(new AuditRecord(
                    now, "System", request.DeviceId, "AgentCredential.RotationExpired",
                    "AgentCredential", current.Id.ToString("N"),
                    "An expired credential rotation request was superseded.", "foundation", "Server", "Expired"));
            }

            current.RequestRotation(request.OperationId, now);
            auditWriter.Append(new AuditRecord(
                now, "System", request.DeviceId, "AgentCredential.RotationRequested",
                "AgentCredential", current.Id.ToString("N"),
                "Operator requested a staged Agent credential rotation.", "foundation", "Server", "Accepted"));
            return new AgentCredentialRotationRequestedResponse(
                request.OperationId, request.DeviceId, "requested", now.Add(RotationLifetime));
        }, cancellationToken);
    }

    public async Task<AgentCredentialRotationStageResponse> StageRotationAsync(
        AgentCredentialRotationStageRequest request, CancellationToken cancellationToken = default)
    {
        ValidateRotationSecrets(request.OperationId, request.DeviceId, request.CurrentSecret, request.ReplacementSecret);
        await using var fence = await AgentDeviceFenceLock.AcquireAsync(dbContext, request.DeviceId, cancellationToken);
        return await transactions.ExecuteAsync(async ct =>
        {
            var now = clock.UtcNow;
            var oldHash = AgentCredentialSecretMaterial.Hash(request.CurrentSecret);
            var replacementHash = AgentCredentialSecretMaterial.Hash(request.ReplacementSecret);
            var existing = await dbContext.AgentCredentials
                .SingleOrDefaultAsync(x => x.RotationOperationId == request.OperationId, ct);
            if (existing is not null)
            {
                if (!string.Equals(existing.DeviceId, request.DeviceId, StringComparison.Ordinal))
                    throw new AgentCredentialException("agent.rotation_operation_reused");
                var replaced = existing.ReplacesCredentialId.HasValue
                    ? await dbContext.AgentCredentials.SingleOrDefaultAsync(x => x.Id == existing.ReplacesCredentialId.Value, ct)
                    : null;
                if (replaced is null || !FixedHexEquals(existing.SecretHash, replacementHash) ||
                    !FixedHexEquals(replaced.SecretHash, oldHash))
                    throw new AgentCredentialException("agent.rotation_payload_mismatch");
                if (existing.RevokedAtUtc is not null)
                    return new AgentCredentialRotationStageResponse(
                        request.OperationId, request.DeviceId, "expired", null);

                if (existing.IsPendingRotation && existing.RotationExpiresAtUtc <= now)
                {
                    var prior = existing.ReplacesCredentialId.HasValue
                        ? await dbContext.AgentCredentials.SingleOrDefaultAsync(
                            x => x.Id == existing.ReplacesCredentialId.Value, ct)
                        : null;
                    if (prior is not null &&
                        prior.RevokedAtUtc is null &&
                        prior.RotationRequestedOperationId == request.OperationId)
                    {
                        prior.ClearRotationRequest();
                    }

                    existing.Revoke(now);
                    auditWriter.Append(new AuditRecord(
                        now, "System", request.DeviceId, "AgentCredential.RotationExpired",
                        "AgentCredential", existing.Id.ToString("N"),
                        "An expired pending Agent credential rotation was safely retired; the prior credential was retained.",
                        "foundation", "Server", "Expired"));
                    return new AgentCredentialRotationStageResponse(
                        request.OperationId, request.DeviceId, "expired", null);
                }

                return new AgentCredentialRotationStageResponse(
                    request.OperationId, request.DeviceId,
                    existing.IsPendingRotation ? "staged" : "committed",
                    existing.RotationExpiresAtUtc);
            }

            var current = await dbContext.AgentCredentials
                .Where(x => x.DeviceId == request.DeviceId && x.RevokedAtUtc == null && !x.IsPendingRotation)
                .SingleOrDefaultAsync(ct);
            if (current is null || !FixedHexEquals(current.SecretHash, oldHash))
                throw new AgentCredentialException("agent.rotation_credential_rejected");

            // If the local pending record survived a long outage, the operation may
            // have expired or been superseded before the Server staged it. Return an
            // explicit expired state only after proving that this prior credential is
            // still the active one. The Agent will independently verify the old secret
            // through /auth/token before it deletes local pending state.
            if (current.RotationRequestedOperationId != request.OperationId ||
                !current.RotationRequestedAtUtc.HasValue)
            {
                return new AgentCredentialRotationStageResponse(
                    request.OperationId, request.DeviceId, "expired", null);
            }

            if (current.RotationRequestedAtUtc.Value + RotationLifetime <= now)
            {
                current.ClearRotationRequest();
                auditWriter.Append(new AuditRecord(
                    now, "System", request.DeviceId, "AgentCredential.RotationExpired",
                    "AgentCredential", current.Id.ToString("N"),
                    "An unstaged Agent credential rotation request expired; the prior credential was retained.",
                    "foundation", "Server", "Expired"));
                return new AgentCredentialRotationStageResponse(
                    request.OperationId, request.DeviceId, "expired", null);
            }

            var otherPending = await dbContext.AgentCredentials.AnyAsync(
                x => x.DeviceId == request.DeviceId && x.RevokedAtUtc == null && x.IsPendingRotation, ct);
            if (otherPending) throw new AgentCredentialException("agent.rotation_in_progress");

            var replacement = new AgentCredential
            {
                DeviceId = request.DeviceId,
                SecretHash = replacementHash,
                CreatedAtUtc = now,
                IsPendingRotation = true,
                RotationOperationId = request.OperationId,
                ReplacesCredentialId = current.Id,
                RotationExpiresAtUtc = now.Add(RotationLifetime)
            };
            dbContext.AgentCredentials.Add(replacement);
            auditWriter.Append(new AuditRecord(
                now, "System", request.DeviceId, "AgentCredential.RotationStaged",
                "AgentCredential", replacement.Id.ToString("N"),
                "Replacement credential hash staged; current credential remains active.", "foundation", "Server", "Staged"));
            return new AgentCredentialRotationStageResponse(
                request.OperationId, request.DeviceId, "staged", replacement.RotationExpiresAtUtc);
        }, cancellationToken);
    }

    public async Task<AgentCredentialRotationConfirmationResponse> ConfirmRotationAsync(
        AgentCredentialRotationConfirmationRequest request, CancellationToken cancellationToken = default)
    {
        ValidateRotationSecrets(request.OperationId, request.DeviceId, request.CurrentSecret, request.ReplacementSecret);
        await using var fence = await AgentDeviceFenceLock.AcquireAsync(dbContext, request.DeviceId, cancellationToken);
        return await transactions.ExecuteAsync(async ct =>
        {
            var now = clock.UtcNow;
            var oldHash = AgentCredentialSecretMaterial.Hash(request.CurrentSecret);
            var replacementHash = AgentCredentialSecretMaterial.Hash(request.ReplacementSecret);
            var replacement = await dbContext.AgentCredentials.SingleOrDefaultAsync(
                x => x.RotationOperationId == request.OperationId, ct);
            if (replacement is null || !string.Equals(replacement.DeviceId, request.DeviceId, StringComparison.Ordinal) ||
                !replacement.ReplacesCredentialId.HasValue || !FixedHexEquals(replacement.SecretHash, replacementHash))
                throw new AgentCredentialException("agent.rotation_not_staged");

            var current = await dbContext.AgentCredentials.SingleOrDefaultAsync(
                x => x.Id == replacement.ReplacesCredentialId.Value, ct);
            if (current is null || !string.Equals(current.DeviceId, request.DeviceId, StringComparison.Ordinal) ||
                !FixedHexEquals(current.SecretHash, oldHash))
                throw new AgentCredentialException("agent.rotation_credential_rejected");

            if (!replacement.IsPendingRotation)
            {
                if (replacement.RevokedAtUtc is not null || current.RevokedAtUtc is null)
                    throw new AgentCredentialException("agent.rotation_not_staged");
                return new AgentCredentialRotationConfirmationResponse(
                    request.OperationId, request.DeviceId, "already_committed", replacement.Id);
            }

            if (replacement.RevokedAtUtc is not null || !replacement.RotationExpiresAtUtc.HasValue ||
                replacement.RotationExpiresAtUtc.Value <= now)
                throw new AgentCredentialException("agent.rotation_expired");
            if (current.RevokedAtUtc is not null || current.RotationRequestedOperationId != request.OperationId)
                throw new AgentCredentialException("agent.rotation_not_requested");

            await RevokePendingEnrollmentTokensAsync(request.DeviceId, now,
                "Agent credential rotation committed; prior pending enrollment token retired.", ct);
            current.ClearRotationRequest();
            current.Revoke(now);
            replacement.CompleteRotation();
            await FenceActiveLeaseAsync(request.DeviceId, ct);
            auditWriter.Append(new AuditRecord(
                now, "System", request.DeviceId, "AgentCredential.RotationCommitted",
                "AgentCredential", replacement.Id.ToString("N"),
                "Replacement credential committed after Agent persistence acknowledgement.", "foundation", "Server", "Succeeded"));
            return new AgentCredentialRotationConfirmationResponse(
                request.OperationId, request.DeviceId, "committed", replacement.Id);
        }, cancellationToken);
    }

    public async Task<bool> RevokeAsync(AgentCredentialRevokeRequest request, CancellationToken cancellationToken = default)
    {
        ValidateDeviceId(request.DeviceId);
        await using var fence = await AgentDeviceFenceLock.AcquireAsync(dbContext, request.DeviceId, cancellationToken);
        return await transactions.ExecuteAsync(async ct =>
        {
            var current = await dbContext.AgentCredentials
                .Where(x => x.DeviceId == request.DeviceId && x.RevokedAtUtc == null && !x.IsPendingRotation)
                .SingleOrDefaultAsync(ct);
            var pendingCredentials = await dbContext.AgentCredentials
                .Where(x => x.DeviceId == request.DeviceId && x.RevokedAtUtc == null && x.IsPendingRotation)
                .ToListAsync(ct);
            var now = clock.UtcNow;
            var revokedPendingTokens = await RevokePendingEnrollmentTokensAsync(request.DeviceId, now, request.Reason, ct);
            foreach (var pending in pendingCredentials) pending.Revoke(now);

            if (current is null)
            {
                await FenceActiveLeaseAsync(request.DeviceId, ct);
                return revokedPendingTokens > 0 || pendingCredentials.Count > 0;
            }
            current.ClearRotationRequest();
            current.Revoke(now);
            await FenceActiveLeaseAsync(request.DeviceId, ct);
            auditWriter.Append(new AuditRecord(
                now, "System", request.DeviceId, "AgentCredential.Revoked",
                "AgentCredential", current.Id.ToString("N"), request.Reason, "foundation", "Server", "Succeeded"));
            return true;
        }, cancellationToken);
    }

    public async Task<bool> AuthenticateAsync(string deviceId, string secret, CancellationToken cancellationToken = default) =>
        (await AuthenticateAndGetCredentialIdAsync(deviceId, secret, cancellationToken)) is not null;

    public Task<Guid?> AuthenticateAndGetCredentialIdAsync(
        string deviceId, string secret, CancellationToken cancellationToken = default) =>
        transactions.ExecuteAsync<Guid?>(async ct =>
        {
            if (!AgentDeviceIdentityRules.IsValid(deviceId) || !AgentCredentialSecretMaterial.IsCanonicalSecret(secret))
                return null;
            var credential = await dbContext.AgentCredentials
                .Where(x => x.DeviceId == deviceId && x.RevokedAtUtc == null && !x.IsPendingRotation)
                .SingleOrDefaultAsync(ct);
            var valid = credential is not null && SecretMatchesHash(secret, credential.SecretHash);
            if (!valid)
            {
                auditWriter.Append(new AuditRecord(
                    clock.UtcNow, "System", deviceId, "AgentCredential.AuthenticationRejected",
                    "AgentCredential", credential?.Id.ToString("N"),
                    "Invalid or revoked Agent credential.", "foundation", "Server", "Rejected"));
                return null;
            }
            credential!.LastAuthenticatedAtUtc = clock.UtcNow;
            return credential.Id;
        }, cancellationToken);

    public Task<bool> IsCredentialActiveAsync(Guid credentialId, string deviceId, CancellationToken cancellationToken = default)
    {
        if (credentialId == Guid.Empty || !AgentDeviceIdentityRules.IsValid(deviceId))
            return Task.FromResult(false);
        return dbContext.AgentCredentials.AsNoTracking().AnyAsync(
            x => x.Id == credentialId && x.DeviceId == deviceId && x.RevokedAtUtc == null && !x.IsPendingRotation,
            cancellationToken);
    }

    public Task<Guid?> GetRequestedRotationOperationIdAsync(
        string deviceId, Guid credentialId, CancellationToken cancellationToken = default)
    {
        if (credentialId == Guid.Empty || !AgentDeviceIdentityRules.IsValid(deviceId))
            return Task.FromResult<Guid?>(null);
        return dbContext.AgentCredentials.AsNoTracking()
            .Where(x => x.Id == credentialId && x.DeviceId == deviceId &&
                        x.RevokedAtUtc == null && !x.IsPendingRotation)
            .Select(x => x.RotationRequestedOperationId)
            .SingleOrDefaultAsync(cancellationToken);
    }

    private async Task<int> RevokePendingEnrollmentTokensAsync(
        string deviceId, DateTimeOffset revokedAtUtc, string reason, CancellationToken cancellationToken)
    {
        var pending = await dbContext.AgentEnrollmentTokens
            .Where(x => x.DeviceId == deviceId && x.RedeemedAtUtc == null && x.RevokedAtUtc == null)
            .ToListAsync(cancellationToken);
        var revokedCount = 0;
        foreach (var token in pending)
        {
            if (!token.Revoke(revokedAtUtc)) continue;
            revokedCount++;
            auditWriter.Append(new AuditRecord(
                revokedAtUtc, "System", deviceId, "agent.enrollment_token_invalidated_by_credential_change",
                "AgentEnrollmentToken", token.Id.ToString("N"),
                string.IsNullOrWhiteSpace(reason) ? "Credential authority changed; pending enrollment token retired." : reason.Trim(),
                "foundation", "Server", "Revoked"));
        }
        return revokedCount;
    }

    private async Task FenceActiveLeaseAsync(string deviceId, CancellationToken cancellationToken)
    {
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            DELETE FROM agent_connection_leases
            WHERE device_id = {deviceId}
            """, cancellationToken);
    }

    private static bool SecretMatchesHash(string secret, string expectedHash) =>
        FixedHexEquals(expectedHash, AgentCredentialSecretMaterial.Hash(secret));

    private static bool FixedHexEquals(string expectedHex, string actualHex)
    {
        byte[]? expected = null;
        byte[]? actual = null;
        try
        {
            expected = Convert.FromHexString(expectedHex);
            actual = Convert.FromHexString(actualHex);
            return expected.Length == actual.Length && CryptographicOperations.FixedTimeEquals(expected, actual);
        }
        catch (FormatException) { return false; }
        finally
        {
            if (expected is not null) CryptographicOperations.ZeroMemory(expected);
            if (actual is not null) CryptographicOperations.ZeroMemory(actual);
        }
    }

    private static void ValidateRotationSecrets(Guid operationId, string deviceId, string currentSecret, string replacementSecret)
    {
        ValidateOperationId(operationId);
        ValidateDeviceId(deviceId);
        if (!AgentCredentialSecretMaterial.IsCanonicalSecret(currentSecret) ||
            !AgentCredentialSecretMaterial.IsCanonicalSecret(replacementSecret))
            throw new AgentCredentialException("agent.rotation_secret_invalid");
        if (FixedHexEquals(AgentCredentialSecretMaterial.Hash(currentSecret), AgentCredentialSecretMaterial.Hash(replacementSecret)))
            throw new AgentCredentialException("agent.rotation_secret_unchanged");
    }

    private static void ValidateOperationId(Guid operationId)
    {
        if (operationId == Guid.Empty) throw new AgentCredentialException("agent.rotation_operation_invalid");
    }

    private static void ValidateDeviceId(string deviceId)
    {
        if (!AgentDeviceIdentityRules.IsValid(deviceId)) throw new AgentCredentialException("agent.device_id_invalid");
    }
}
