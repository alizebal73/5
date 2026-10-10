using GameNet.Server.Infrastructure.Audit;
using GameNet.Server.Infrastructure.Transactions;
using GameNet.Server.Persistence;
using GameNet.Server.Persistence.Entities;
using GameNet.Shared.Contracts.V1.Security;
using GameNet.Shared.Primitives;
using Microsoft.EntityFrameworkCore;

namespace GameNet.Server.Infrastructure.Security;

public sealed class AgentEnrollmentService(
    GameNetDbContext dbContext,
    ITransactionCoordinator transactions,
    IAuditWriter auditWriter,
    IGameClock clock) : IAgentEnrollmentService
{
    private static readonly TimeSpan TokenLifetime = TimeSpan.FromMinutes(15);

    public async Task<AgentEnrollmentIssueResponse> IssueAsync(
        AgentEnrollmentIssueCommand command,
        CancellationToken cancellationToken = default)
    {
        ValidateDeviceId(command.DeviceId);
        if (command.IssuedByOperatorId == Guid.Empty)
            throw new AgentCredentialException("auth.identity_missing");

        try
        {
            return await transactions.ExecuteAsync(async ct =>
            {
                var now = clock.UtcNow;
                var exists = await dbContext.AgentCredentials.AnyAsync(
                    x => x.DeviceId == command.DeviceId && x.RevokedAtUtc == null, ct);
                if (exists)
                    throw new AgentCredentialException("agent.credential_exists");

                var pending = await dbContext.AgentEnrollmentTokens
                    .Where(x => x.DeviceId == command.DeviceId &&
                                x.RedeemedAtUtc == null &&
                                x.RevokedAtUtc == null)
                    .ToListAsync(ct);

                foreach (var oldToken in pending)
                {
                    if (oldToken.ExpiresAtUtc > now)
                        throw new AgentCredentialException("agent.enrollment_token_pending");

                    oldToken.Revoke(now);
                    auditWriter.Append(new AuditRecord(
                        now,
                        "Operator",
                        command.IssuedByOperatorId.ToString("D"),
                        "agent.enrollment_token_expired",
                        "AgentEnrollmentToken",
                        oldToken.Id.ToString("N"),
                        "Expired enrollment token retired before reissue.",
                        command.CorrelationId,
                        command.Source,
                        "Expired"));
                }

                // Clear the partial unique pending-device index before inserting its replacement.
                // This flush remains inside the same serializable transaction; failure rolls back both steps.
                if (pending.Count > 0)
                    await dbContext.SaveChangesAsync(ct);

                var tokenText = AgentCredentialSecretMaterial.Generate();
                var tokenId = Guid.NewGuid();
                var expiresAt = now.Add(TokenLifetime);
                var token = AgentEnrollmentToken.Create(
                    tokenId,
                    command.DeviceId,
                    AgentCredentialSecretMaterial.HashEnrollmentToken(tokenText),
                    command.IssuedByOperatorId,
                    now,
                    expiresAt);

                dbContext.AgentEnrollmentTokens.Add(token);
                auditWriter.Append(new AuditRecord(
                    now,
                    "Operator",
                    command.IssuedByOperatorId.ToString("D"),
                    "agent.enrollment_token_issue",
                    "AgentEnrollmentToken",
                    tokenId.ToString("N"),
                    "Short-lived Agent enrollment token issued.",
                    command.CorrelationId,
                    command.Source,
                    "Succeeded"));

                return new AgentEnrollmentIssueResponse(tokenId, command.DeviceId, tokenText, expiresAt);
            }, cancellationToken);
        }
        catch (PersistenceConflictException)
        {
            throw new AgentCredentialException("agent.enrollment_conflict");
        }
    }

    public async Task<AgentEnrollmentIssueResponse> RecoverAsync(
        AgentEnrollmentRecoverCommand command,
        CancellationToken cancellationToken = default)
    {
        ValidateDeviceId(command.DeviceId);
        if (command.IssuedByOperatorId == Guid.Empty)
            throw new AgentCredentialException("auth.identity_missing");
        if (string.IsNullOrWhiteSpace(command.Reason) || command.Reason.Trim().Length > 1000)
            throw new AgentCredentialException("agent.enrollment_reason_invalid");

        try
        {
            return await transactions.ExecuteAsync(async ct =>
            {
                var now = clock.UtcNow;
                var credential = await dbContext.AgentCredentials.SingleOrDefaultAsync(
                    x => x.DeviceId == command.DeviceId && x.RevokedAtUtc == null, ct);
                var hasPreviouslyAuthenticatedCredential = await dbContext.AgentCredentials
                    .AnyAsync(x => x.DeviceId == command.DeviceId && x.LastAuthenticatedAtUtc != null, ct);
                var latestRedeemed = await dbContext.AgentEnrollmentTokens
                    .Where(x => x.DeviceId == command.DeviceId && x.RedeemedAtUtc != null)
                    .OrderByDescending(x => x.RedeemedAtUtc)
                    .FirstOrDefaultAsync(ct);

                // Never use recovery to undo a credential that has authenticated,
                // even if it was subsequently revoked. This also permits replacing an
                // expired/unredeemed token when no credential was ever authenticated.
                if (hasPreviouslyAuthenticatedCredential)
                    throw new AgentCredentialException("agent.enrollment_recovery_not_safe");

                if (credential is not null)
                {
                    // When redemption succeeded but local persistence failed, the
                    // active credential must correspond to that recent enrollment.
                    if (latestRedeemed?.RedeemedAtUtc is not { } redeemedAt ||
                        (credential.CreatedAtUtc - redeemedAt).Duration() > TimeSpan.FromSeconds(30))
                    {
                        throw new AgentCredentialException("agent.enrollment_recovery_not_safe");
                    }

                    credential.Revoke(now);
                    auditWriter.Append(new AuditRecord(
                        now,
                        "Operator",
                        command.IssuedByOperatorId.ToString("D"),
                        "agent.enrollment_recovery_credential_revoked",
                        "AgentCredential",
                        credential.Id.ToString("N"),
                        "Never-authenticated enrollment credential revoked after explicit recovery request.",
                        command.CorrelationId,
                        command.Source,
                        "Succeeded"));
                }

                var pending = await dbContext.AgentEnrollmentTokens
                    .Where(x => x.DeviceId == command.DeviceId &&
                                x.RedeemedAtUtc == null &&
                                x.RevokedAtUtc == null)
                    .ToListAsync(ct);
                foreach (var oldToken in pending)
                {
                    oldToken.Revoke(now);
                    auditWriter.Append(new AuditRecord(
                        now,
                        "Operator",
                        command.IssuedByOperatorId.ToString("D"),
                        "agent.enrollment_recovery_token_revoked",
                        "AgentEnrollmentToken",
                        oldToken.Id.ToString("N"),
                        "Prior pending token invalidated by explicit enrollment recovery.",
                        command.CorrelationId,
                        command.Source,
                        "Succeeded"));
                }

                // Clear the partial unique pending-device index before inserting the replacement.
                if (pending.Count > 0)
                    await dbContext.SaveChangesAsync(ct);

                var tokenText = AgentCredentialSecretMaterial.Generate();
                var tokenId = Guid.NewGuid();
                var expiresAt = now.Add(TokenLifetime);
                dbContext.AgentEnrollmentTokens.Add(AgentEnrollmentToken.Create(
                    tokenId,
                    command.DeviceId,
                    AgentCredentialSecretMaterial.HashEnrollmentToken(tokenText),
                    command.IssuedByOperatorId,
                    now,
                    expiresAt));

                auditWriter.Append(new AuditRecord(
                    now,
                    "Operator",
                    command.IssuedByOperatorId.ToString("D"),
                    "agent.enrollment_recovery_issue",
                    "AgentEnrollmentToken",
                    tokenId.ToString("N"),
                    command.Reason.Trim(),
                    command.CorrelationId,
                    command.Source,
                    "Succeeded"));

                return new AgentEnrollmentIssueResponse(tokenId, command.DeviceId, tokenText, expiresAt);
            }, cancellationToken);
        }
        catch (PersistenceConflictException)
        {
            throw new AgentCredentialException("agent.enrollment_conflict");
        }
    }

    public async Task<AgentCredentialSecretResponse?> RedeemAsync(
        AgentEnrollmentRedeemRequest request,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        ValidateDeviceId(request.DeviceId);

        string tokenHash;
        try
        {
            tokenHash = AgentCredentialSecretMaterial.HashEnrollmentToken(request.Token);
        }
        catch (ArgumentException)
        {
            return null;
        }

        try
        {
            return await transactions.ExecuteAsync(async ct =>
            {
                var now = clock.UtcNow;
                var enrollment = await dbContext.AgentEnrollmentTokens.SingleOrDefaultAsync(
                    x => x.DeviceId == request.DeviceId && x.TokenHash == tokenHash, ct);

                if (enrollment is null || !enrollment.CanRedeem(now))
                {
                    auditWriter.Append(new AuditRecord(
                        now,
                        "Agent",
                        request.DeviceId,
                        "agent.enrollment_token_redeem",
                        "AgentEnrollmentToken",
                        enrollment?.Id.ToString("N"),
                        "Unknown, expired, revoked or already redeemed enrollment token.",
                        correlationId,
                        "Agent",
                        "Rejected"));
                    return null;
                }

                var credentialExists = await dbContext.AgentCredentials.AnyAsync(
                    x => x.DeviceId == request.DeviceId && x.RevokedAtUtc == null, ct);
                if (credentialExists)
                {
                    enrollment.Revoke(now);
                    auditWriter.Append(new AuditRecord(
                        now,
                        "Agent",
                        request.DeviceId,
                        "agent.enrollment_token_redeem",
                        "AgentEnrollmentToken",
                        enrollment.Id.ToString("N"),
                        "A credential already exists for this device.",
                        correlationId,
                        "Agent",
                        "Rejected"));
                    return null;
                }

                enrollment.MarkRedeemed(now);
                var secret = AgentCredentialSecretMaterial.Generate();
                var credential = new AgentCredential
                {
                    DeviceId = request.DeviceId,
                    SecretHash = AgentCredentialSecretMaterial.Hash(secret),
                    CreatedAtUtc = now
                };
                dbContext.AgentCredentials.Add(credential);
                auditWriter.Append(new AuditRecord(
                    now,
                    "Agent",
                    request.DeviceId,
                    "agent.enrollment_redeem",
                    "AgentCredential",
                    credential.Id.ToString("N"),
                    "Agent enrollment token redeemed and credential created.",
                    correlationId,
                    "Agent",
                    "Succeeded"));

                return new AgentCredentialSecretResponse(
                    credential.Id,
                    credential.DeviceId,
                    secret,
                    now);
            }, cancellationToken);
        }
        catch (PersistenceConflictException)
        {
            // A different credential path may have won the unique active-device race.
            // Treat the token as unusable; never retry credential creation outside this transaction.
            return null;
        }
    }

    public async Task<bool> RevokeAsync(
        Guid tokenId,
        string reason,
        Guid actorId,
        string correlationId,
        string source,
        CancellationToken cancellationToken = default)
    {
        if (tokenId == Guid.Empty || actorId == Guid.Empty)
            throw new AgentCredentialException("agent.enrollment_request_invalid");
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 1000)
            throw new AgentCredentialException("agent.enrollment_reason_invalid");

        return await transactions.ExecuteAsync(async ct =>
        {
            var token = await dbContext.AgentEnrollmentTokens.SingleOrDefaultAsync(x => x.Id == tokenId, ct);
            if (token is null || !token.Revoke(clock.UtcNow))
                return false;

            auditWriter.Append(new AuditRecord(
                clock.UtcNow,
                "Operator",
                actorId.ToString("D"),
                "agent.enrollment_token_revoke",
                "AgentEnrollmentToken",
                token.Id.ToString("N"),
                reason.Trim(),
                correlationId,
                source,
                "Succeeded"));
            return true;
        }, cancellationToken);
    }

    private static void ValidateDeviceId(string deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId) ||
            deviceId.Length > 128 ||
            deviceId != deviceId.Trim() ||
            deviceId.Any(character =>
                !(char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.')))
            throw new AgentCredentialException("agent.device_id_invalid");
    }
}
