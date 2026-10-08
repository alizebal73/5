using GameNet.Server.Infrastructure.Time;
using GameNet.Server.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GameNet.Server.Infrastructure.Idempotency;

public sealed class EfIdempotencyStore(
    GameNetDbContext dbContext,
    IGameClock clock) : IIdempotencyStore
{
    public async Task<IdempotencyClaim> TryClaimAsync(
        string scope,
        string key,
        string operation,
        TimeSpan leaseDuration,
        TimeSpan retention,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);

        if (leaseDuration <= TimeSpan.Zero || leaseDuration > TimeSpan.FromHours(1))
            throw new ArgumentOutOfRangeException(nameof(leaseDuration));
        if (retention <= leaseDuration)
            throw new ArgumentOutOfRangeException(nameof(retention));

        var now = clock.UtcNow;
        var leaseExpiresAtUtc = now.Add(leaseDuration);
        var expiresAtUtc = now.Add(retention);
        var token = Guid.NewGuid().ToString("N");

        var insertedToken = await dbContext.Database
            .SqlQueryRaw<string>(
                """
                INSERT INTO idempotency_records
                    (scope, key, operation, state, lease_token, status_code, response_json,
                     created_at_utc, lease_expires_at_utc, expires_at_utc)
                VALUES
                    ({0}, {1}, {2}, 'processing', {3}, 0, '{}'::jsonb,
                     {4}, {5}, {6})
                ON CONFLICT (scope, key) DO NOTHING
                RETURNING lease_token AS "Value"
                """,
                scope,
                key,
                operation,
                token,
                now,
                leaseExpiresAtUtc,
                expiresAtUtc)
            .SingleOrDefaultAsync(cancellationToken);

        if (string.Equals(insertedToken, token, StringComparison.Ordinal))
            return new IdempotencyClaim(true, false, false, token, null, null);

        var record = await dbContext.IdempotencyRecords
            .AsNoTracking()
            .SingleAsync(x => x.Scope == scope && x.Key == key, cancellationToken);

        if (!string.Equals(record.Operation, operation, StringComparison.Ordinal))
            throw new InvalidOperationException("IDEMPOTENCY_KEY_REUSED_FOR_DIFFERENT_OPERATION");

        if (record.State == "completed" && record.ExpiresAtUtc > now)
            return new IdempotencyClaim(false, true, false, string.Empty, record.StatusCode, record.ResponseJson);

        if (record.State == "processing" && record.LeaseExpiresAtUtc > now)
            return new IdempotencyClaim(false, false, true, string.Empty, null, null);

        var reclaimed = await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE idempotency_records
            SET state = {"processing"},
                lease_token = {token},
                lease_expires_at_utc = {leaseExpiresAtUtc},
                expires_at_utc = {expiresAtUtc}
            WHERE scope = {scope}
              AND key = {key}
              AND operation = {operation}
              AND (
                    (state = {"processing"} AND lease_expires_at_utc <= {now})
                 OR (state = {"completed"} AND expires_at_utc <= {now})
              )
            """,
            cancellationToken);

        if (reclaimed == 1)
            return new IdempotencyClaim(true, false, false, token, null, null);

        var final = await dbContext.IdempotencyRecords
            .AsNoTracking()
            .SingleAsync(x => x.Scope == scope && x.Key == key, cancellationToken);

        if (final.State == "completed" && final.ExpiresAtUtc > now)
            return new IdempotencyClaim(false, true, false, string.Empty, final.StatusCode, final.ResponseJson);

        return new IdempotencyClaim(false, false, true, string.Empty, null, null);
    }

    public async Task CompleteAsync(
        string scope,
        string key,
        string leaseToken,
        int statusCode,
        string responseJson,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(leaseToken);
        ArgumentNullException.ThrowIfNull(responseJson);

        var now = clock.UtcNow;

        var updated = await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE idempotency_records
            SET state = {"completed"},
                status_code = {statusCode},
                response_json = CAST({responseJson} AS jsonb),
                lease_token = {""},
                lease_expires_at_utc = {now}
            WHERE scope = {scope}
              AND key = {key}
              AND lease_token = {leaseToken}
              AND state = {"processing"}
              AND lease_expires_at_utc > {now}
            """,
            cancellationToken);

        if (updated != 1)
            throw new InvalidOperationException("IDEMPOTENCY_LEASE_NOT_OWNED");
    }
}
