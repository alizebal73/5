using GameNet.Server.Infrastructure.Transactions;
using GameNet.Server.Persistence;
using GameNet.Server.Persistence.Entities;
using GameNet.Shared.Contracts.V1.Protocol;
using GameNet.Shared.Primitives;
using Microsoft.EntityFrameworkCore;

namespace GameNet.Server.Infrastructure.Realtime;

public sealed class EfAgentConnectionLeaseStore(
    GameNetDbContext dbContext,
    IGameClock clock,
    ITransactionCoordinator transactions) : IAgentConnectionLeaseStore
{
    public Task<AgentConnectionLeaseState?> TryAcquireAsync(
        AgentConnectionLeaseRequest request,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default)
    {
        Validate(request, leaseDuration);
        return transactions.ExecuteAsync(
            ct => TryAcquireWithinTransactionAsync(request, leaseDuration, ct),
            cancellationToken);
    }

    public async Task<bool> RenewAsync(
        string deviceId,
        string connectionId,
        string leaseToken,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default)
    {
        ValidateStrings(deviceId, connectionId, leaseToken);
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
        ValidateStrings(heartbeat.DeviceId, connectionId, leaseToken);
        ArgumentException.ThrowIfNullOrWhiteSpace(heartbeat.AgentVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(heartbeat.StationState);
        ValidateLeaseDuration(leaseDuration);

        var now = clock.UtcNow;
        var expires = now.Add(leaseDuration);

        var updated = await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE agent_connection_leases
            SET last_heartbeat_at_utc = {heartbeat.SentAtUtc},
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

    public async Task ReleaseIfOwnerAsync(
        string deviceId,
        string connectionId,
        string leaseToken,
        CancellationToken cancellationToken = default)
    {
        ValidateStrings(deviceId, connectionId, leaseToken);

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
        TimeSpan leaseDuration,
        CancellationToken cancellationToken)
    {
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
        new(entity.DeviceId, entity.ConnectionId, entity.LeaseToken, entity.LeaseExpiresAtUtc, authoritative);

    private static void Validate(AgentConnectionLeaseRequest request, TimeSpan leaseDuration)
    {
        ValidateStrings(request.DeviceId, request.ConnectionId);
        ValidateLeaseDuration(leaseDuration);
    }

    private static void ValidateStrings(params string[] values)
    {
        foreach (var value in values)
            ArgumentException.ThrowIfNullOrWhiteSpace(value);
    }

    private static void ValidateLeaseDuration(TimeSpan leaseDuration)
    {
        if (leaseDuration <= TimeSpan.Zero || leaseDuration > TimeSpan.FromMinutes(30))
            throw new ArgumentOutOfRangeException(nameof(leaseDuration));
    }
}
