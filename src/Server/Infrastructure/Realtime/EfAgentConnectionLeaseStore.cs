using System.Data;
using GameNet.Server.Infrastructure.Transactions;
using GameNet.Server.Infrastructure.Security;
using GameNet.Server.Persistence;
using GameNet.Server.Persistence.Entities;
using GameNet.Shared.Contracts.V1.Protocol;
using GameNet.Shared.Primitives;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GameNet.Server.Infrastructure.Realtime;

public sealed class EfAgentConnectionLeaseStore(
    GameNetDbContext dbContext,
    IGameClock clock,
    ITransactionCoordinator transactions) : IAgentConnectionLeaseStore
{
    public Task<AgentConnectionLeaseState?> TryAcquireAsync(
        AgentConnectionLeaseRequest request,
        Guid credentialId,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default)
    {
        Validate(request, leaseDuration);
        if (credentialId == Guid.Empty)
            throw new ArgumentException("An active persisted credential id is required.", nameof(credentialId));

        return transactions.ExecuteAsync(async ct =>
        {
            // Acquisition uses the same per-device fence as revocation and dispatch.
            // This prevents a connection that authenticated before a revoke/rotate
            // from creating a late lease after the credential's authority was removed.
            await AgentDeviceFenceLock.AcquireAsync(dbContext, request.DeviceId, ct);
            return await TryAcquireWithinTransactionAsync(request, credentialId, leaseDuration, ct);
        }, cancellationToken);
    }

    public async Task<bool> RenewAsync(
        string deviceId,
        string connectionId,
        string leaseToken,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default)
    {
        ValidateDeviceId(deviceId);
        ValidateBoundedText(connectionId, nameof(connectionId), 128);
        ValidateBoundedText(leaseToken, nameof(leaseToken), 128);
        ValidateLeaseDuration(leaseDuration);

        var now = clock.UtcNow;
        var expires = now.Add(leaseDuration);

        var updated = await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE agent_connection_leases
            SET lease_expires_at_utc = {expires},
                updated_at_utc = {now}
            WHERE device_id = {deviceId}
              AND connection_id = {connectionId}
              AND lease_token = {leaseToken}
              AND lease_expires_at_utc > {now}
            """,
            cancellationToken);

        return updated == 1;
    }

    public async Task<bool> RecordHeartbeatAsync(
        AgentHeartbeat heartbeat,
        string connectionId,
        string leaseToken,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(heartbeat);
        ValidateDeviceId(heartbeat.DeviceId);
        ValidateBoundedText(connectionId, nameof(connectionId), 128);
        ValidateBoundedText(leaseToken, nameof(leaseToken), 128);
        ValidateBoundedText(heartbeat.AgentVersion, nameof(heartbeat.AgentVersion), 64);
        ValidateBoundedText(heartbeat.StationState, nameof(heartbeat.StationState), 64);
        ValidateLeaseDuration(leaseDuration);

        var now = clock.UtcNow;
        var expires = now.Add(leaseDuration);

        var updated = await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE agent_connection_leases
            SET last_heartbeat_at_utc = {now},
                lease_expires_at_utc = {expires},
                agent_version = {heartbeat.AgentVersion},
                station_state = {heartbeat.StationState},
                updated_at_utc = {now}
            WHERE device_id = {heartbeat.DeviceId}
              AND connection_id = {connectionId}
              AND lease_token = {leaseToken}
              AND lease_expires_at_utc > {now}
            """,
            cancellationToken);

        return updated == 1;
    }

    public async Task<AgentCommandLeaseTarget?> GetFreshLeaseForCommandAsync(
        string deviceId,
        TimeSpan heartbeatFreshness,
        CancellationToken cancellationToken = default)
    {
        ValidateDeviceId(deviceId);
        if (heartbeatFreshness <= TimeSpan.Zero || heartbeatFreshness > TimeSpan.FromMinutes(2))
            throw new ArgumentOutOfRangeException(nameof(heartbeatFreshness));

        var now = clock.UtcNow;
        var current = await dbContext.AgentConnectionLeases
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.DeviceId == deviceId, cancellationToken);

        if (current is null ||
            current.LeaseExpiresAtUtc <= now ||
            !current.LastHeartbeatAtUtc.HasValue ||
            current.LastHeartbeatAtUtc.Value < now.Subtract(heartbeatFreshness) ||
            current.LastHeartbeatAtUtc.Value > now.AddSeconds(2))
            return null;

        return new AgentCommandLeaseTarget(
            current.DeviceId,
            current.ConnectionId,
            current.LeaseToken,
            current.LeaseExpiresAtUtc,
            current.LastHeartbeatAtUtc.Value,
            current.AgentVersion,
            current.StationState);
    }

    public async Task<bool> IsCurrentLeaseAsync(
        string deviceId,
        string connectionId,
        string leaseToken,
        CancellationToken cancellationToken = default)
    {
        ValidateDeviceId(deviceId);
        ValidateBoundedText(connectionId, nameof(connectionId), 128);
        ValidateBoundedText(leaseToken, nameof(leaseToken), 128);
        var now = clock.UtcNow;

        return await dbContext.AgentConnectionLeases
            .AsNoTracking()
            .AnyAsync(x =>
                x.DeviceId == deviceId &&
                x.ConnectionId == connectionId &&
                x.LeaseToken == leaseToken &&
                x.LeaseExpiresAtUtc > now,
                cancellationToken);
    }

    public async Task<bool> SendIfCurrentLeaseAsync(
        string deviceId,
        string connectionId,
        string leaseToken,
        Func<CancellationToken, Task> send,
        CancellationToken cancellationToken = default)
    {
        ValidateDeviceId(deviceId);
        ValidateBoundedText(connectionId, nameof(connectionId), 128);
        ValidateBoundedText(leaseToken, nameof(leaseToken), 128);
        ArgumentNullException.ThrowIfNull(send);

        // This transaction contains a network send attempt, so it must never use the
        // generic serializable retry coordinator: replaying the callback could send twice.
        // The timeout bounds how long credential revocation/rotation may wait on this fence.
        using var sendTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, sendTimeout.Token);
        var token = linkedCancellation.Token;

        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted, token);
        var connection = dbContext.Database.GetDbConnection();
        string? currentConnectionId = null;
        string? currentLeaseToken = null;
        DateTimeOffset currentLeaseExpiresAtUtc = default;
        var hasActiveCredential = false;

        await using (var command = connection.CreateCommand())
        {
            command.Transaction = dbContext.Database.CurrentTransaction?.GetDbTransaction();
            command.CommandText = """
                SELECT lease.connection_id,
                       lease.lease_token,
                       lease.lease_expires_at_utc,
                       EXISTS (
                           SELECT 1
                           FROM agent_credentials credential
                           WHERE credential.device_id = lease.device_id
                             AND credential.revoked_at_utc IS NULL
                       ) AS has_active_credential
                FROM agent_connection_leases lease
                WHERE lease.device_id = @deviceId
                FOR UPDATE OF lease
                """;
            var parameter = command.CreateParameter();
            parameter.ParameterName = "deviceId";
            parameter.Value = deviceId;
            command.Parameters.Add(parameter);

            await using var reader = await command.ExecuteReaderAsync(token);
            if (await reader.ReadAsync(token))
            {
                currentConnectionId = reader.GetString(0);
                currentLeaseToken = reader.GetString(1);
                currentLeaseExpiresAtUtc = reader.GetFieldValue<DateTimeOffset>(2);
                hasActiveCredential = reader.GetBoolean(3);
            }
        }

        var now = clock.UtcNow;
        if (currentConnectionId is null ||
            !hasActiveCredential ||
            !string.Equals(currentConnectionId, connectionId, StringComparison.Ordinal) ||
            !string.Equals(currentLeaseToken, leaseToken, StringComparison.Ordinal) ||
            currentLeaseExpiresAtUtc <= now)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            return false;
        }

        // Keep the row lock only until SignalR has completed the send attempt. Do not wait
        // for the Agent acknowledgement here; the caller waits for it after this commits.
        await send(token);
        await transaction.CommitAsync(token);
        return true;
    }

    public async Task ReleaseIfOwnerAsync(
        string deviceId,
        string connectionId,
        string leaseToken,
        CancellationToken cancellationToken = default)
    {
        ValidateDeviceId(deviceId);
        ValidateBoundedText(connectionId, nameof(connectionId), 128);
        ValidateBoundedText(leaseToken, nameof(leaseToken), 128);

        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            DELETE FROM agent_connection_leases
            WHERE device_id = {deviceId}
              AND connection_id = {connectionId}
              AND lease_token = {leaseToken}
            """,
            cancellationToken);
    }

    private async Task<AgentConnectionLeaseState?> TryAcquireWithinTransactionAsync(
        AgentConnectionLeaseRequest request,
        Guid credentialId,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken)
    {
        var credentialIsActive = await dbContext.AgentCredentials.AnyAsync(
            x => x.Id == credentialId &&
                 x.DeviceId == request.DeviceId &&
                 x.RevokedAtUtc == null,
            cancellationToken);
        if (!credentialIsActive)
            return null;

        var now = clock.UtcNow;
        var token = Guid.NewGuid().ToString("N");
        var expires = now.Add(leaseDuration);

        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO agent_connection_leases
                (device_id, connection_id, lease_token, lease_expires_at_utc, updated_at_utc)
            VALUES
                ({request.DeviceId}, {request.ConnectionId}, {token}, {expires}, {now})
            ON CONFLICT (device_id) DO NOTHING
            """,
            cancellationToken);

        var current = await dbContext.AgentConnectionLeases
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.DeviceId == request.DeviceId, cancellationToken);

        if (current is null)
            throw new InvalidOperationException("AGENT_LEASE_DISAPPEARED");

        if (current.LeaseExpiresAtUtc > now &&
            !string.Equals(current.ConnectionId, request.ConnectionId, StringComparison.Ordinal))
        {
            return ToState(current, false);
        }

        if (string.Equals(current.ConnectionId, request.ConnectionId, StringComparison.Ordinal) &&
            string.Equals(current.LeaseToken, token, StringComparison.Ordinal))
        {
            return ToState(current, true);
        }

        var updated = await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE agent_connection_leases
            SET connection_id = {request.ConnectionId},
                lease_token = {token},
                lease_expires_at_utc = {expires},
                updated_at_utc = {now},
                last_heartbeat_at_utc = NULL,
                agent_version = NULL,
                station_state = NULL
            WHERE device_id = {request.DeviceId}
              AND (
                    lease_expires_at_utc <= {now}
                 OR connection_id = {request.ConnectionId}
              )
            """,
            cancellationToken);

        if (updated != 1)
        {
            var winner = await dbContext.AgentConnectionLeases
                .AsNoTracking()
                .SingleOrDefaultAsync(x => x.DeviceId == request.DeviceId, cancellationToken);

            return winner is null ? null : ToState(winner, false);
        }

        return new AgentConnectionLeaseState(
            request.DeviceId,
            request.ConnectionId,
            token,
            expires,
            true);
    }

    private static AgentConnectionLeaseState ToState(AgentConnectionLease entity, bool authoritative) =>
        new(entity.DeviceId, entity.ConnectionId, authoritative ? entity.LeaseToken : string.Empty, entity.LeaseExpiresAtUtc, authoritative);

    private static void Validate(AgentConnectionLeaseRequest request, TimeSpan leaseDuration)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateDeviceId(request.DeviceId);
        ValidateBoundedText(request.ConnectionId, nameof(request.ConnectionId), 128);
        ValidateLeaseDuration(leaseDuration);
    }

    private static void ValidateDeviceId(string deviceId)
    {
        if (!AgentDeviceIdentityRules.IsValid(deviceId))
            throw new ArgumentException("DeviceId must contain 1-128 ASCII letters, digits, dashes, underscores or dots.", nameof(deviceId));
    }

    private static void ValidateBoundedText(string value, string parameterName, int maximumLength)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (value.Length > maximumLength || value.Any(char.IsControl))
            throw new ArgumentException($"Value must be at most {maximumLength} characters and contain no control characters.", parameterName);
    }

    private static void ValidateLeaseDuration(TimeSpan leaseDuration)
    {
        if (leaseDuration <= TimeSpan.Zero || leaseDuration > TimeSpan.FromMinutes(30))
            throw new ArgumentOutOfRangeException(nameof(leaseDuration));
    }
}
