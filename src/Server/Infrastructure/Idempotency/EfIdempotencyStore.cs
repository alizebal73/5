using GameNet.Shared.Primitives;
using GameNet.Server.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GameNet.Server.Infrastructure.Idempotency;

public sealed class EfIdempotencyStore(GameNetDbContext dbContext, IGameClock clock) : IIdempotencyStore
{
    public async Task<IdempotencyClaim> TryClaimAsync(string scope, string key, string operation, string requestHash,
        TimeSpan leaseDuration, TimeSpan retention, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestHash);
        if (scope.Length > 160 || key.Length > 200 || operation.Length > 200 || requestHash.Length > 128)
            throw new ArgumentOutOfRangeException(nameof(key));
        if (leaseDuration <= TimeSpan.Zero || leaseDuration > TimeSpan.FromHours(1))
            throw new ArgumentOutOfRangeException(nameof(leaseDuration));
        if (retention <= leaseDuration) throw new ArgumentOutOfRangeException(nameof(retention));

        var now = clock.UtcNow;
        var leaseExpiresAtUtc = now.Add(leaseDuration);
        var expiresAtUtc = now.Add(retention);
        var token = Guid.NewGuid().ToString("N");
        var insertedToken = await dbContext.Database.SqlQueryRaw<string>(
            """
            INSERT INTO idempotency_records
                (scope, key, operation, request_hash, state, lease_token, status_code, response_json,
                 created_at_utc, lease_expires_at_utc, expires_at_utc)
            VALUES ({0}, {1}, {2}, {3}, 'processing', {4}, 0, '{}'::jsonb, {5}, {6}, {7})
            ON CONFLICT (scope, key) DO NOTHING
            RETURNING lease_token AS "Value"
            """, scope, key, operation, requestHash, token, now, leaseExpiresAtUtc, expiresAtUtc)
            .SingleOrDefaultAsync(cancellationToken);

        if (string.Equals(insertedToken, token, StringComparison.Ordinal))
            return new IdempotencyClaim(true, false, false, token, null, null);

        var record = await dbContext.IdempotencyRecords.AsNoTracking()
            .SingleAsync(x => x.Scope == scope && x.Key == key, cancellationToken);
        var completedActive = record.State == "completed" && record.ExpiresAtUtc > now;
        var processingActive = record.State == "processing" && record.LeaseExpiresAtUtc > now;
        if (completedActive || processingActive)
        {
            if (!string.Equals(record.Operation, operation, StringComparison.Ordinal) ||
                !string.Equals(record.RequestHash, requestHash, StringComparison.Ordinal))
                throw new IdempotencyKeyConflictException();
            if (completedActive) return new IdempotencyClaim(false, true, false, string.Empty, record.StatusCode, record.ResponseJson);
            return new IdempotencyClaim(false, false, true, string.Empty, null, null);
        }

        var reclaimed = await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE idempotency_records
            SET operation = {operation}, request_hash = {requestHash}, state = {"processing"},
                lease_token = {token}, lease_expires_at_utc = {leaseExpiresAtUtc}, expires_at_utc = {expiresAtUtc}
            WHERE scope = {scope} AND key = {key}
              AND ((state = {"processing"} AND lease_expires_at_utc <= {now})
                OR (state = {"completed"} AND expires_at_utc <= {now}))
            """, cancellationToken);
        if (reclaimed == 1) return new IdempotencyClaim(true, false, false, token, null, null);

        var final = await dbContext.IdempotencyRecords.AsNoTracking()
            .SingleAsync(x => x.Scope == scope && x.Key == key, cancellationToken);
        var finalActive = (final.State == "completed" && final.ExpiresAtUtc > now) ||
                          (final.State == "processing" && final.LeaseExpiresAtUtc > now);
        if (finalActive && (!string.Equals(final.Operation, operation, StringComparison.Ordinal) ||
                            !string.Equals(final.RequestHash, requestHash, StringComparison.Ordinal)))
            throw new IdempotencyKeyConflictException();
        if (final.State == "completed" && final.ExpiresAtUtc > now)
            return new IdempotencyClaim(false, true, false, string.Empty, final.StatusCode, final.ResponseJson);
        return new IdempotencyClaim(false, false, true, string.Empty, null, null);
    }

    public async Task CompleteAsync(string scope, string key, string leaseToken, int statusCode, string responseJson,
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
            SET state = {"completed"}, status_code = {statusCode}, response_json = CAST({responseJson} AS jsonb),
                lease_token = {""}, lease_expires_at_utc = {now}
            WHERE scope = {scope} AND key = {key} AND lease_token = {leaseToken}
              AND state = {"processing"} AND lease_expires_at_utc > {now}
            """, cancellationToken);
        if (updated != 1) throw new InvalidOperationException("IDEMPOTENCY_LEASE_NOT_OWNED");
    }
}
