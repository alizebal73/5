using System.Data;
using GameNet.Server.Application;
using GameNet.Server.Infrastructure.Persistence;
using GameNet.Server.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GameNet.Server.Infrastructure.Idempotency;

public sealed class PostgresIdempotencyStore(GameNetDbContext db, TimeProvider timeProvider) : IIdempotencyStore
{
    private static readonly TimeSpan LeaseTimeout = TimeSpan.FromMinutes(2);

    public async Task<IdempotencyClaim> TryClaimAsync(
        string scope,
        string key,
        string operation,
        string requestHash,
        CancellationToken cancellationToken = default)
    {
        await AcquireKeyLockAsync(scope, key, cancellationToken);

        var existing = await db.IdempotencyEntries
            .SingleOrDefaultAsync(x => x.Scope == scope && x.Key == key, cancellationToken);

        if (existing is null)
        {
            var lease = Guid.NewGuid().ToString("N");
            db.IdempotencyEntries.Add(new IdempotencyEntry
            {
                Scope = scope,
                Key = key,
                Operation = operation,
                RequestHash = requestHash,
                LeaseToken = lease,
                ClaimedAtUtc = timeProvider.GetUtcNow(),
                Completed = false
            });
            await db.SaveChangesAsync(cancellationToken);
            return new(IdempotencyClaimState.Claimed, lease, null, null);
        }

        if (!string.Equals(existing.Operation, operation, StringComparison.Ordinal) ||
            !string.Equals(existing.RequestHash, requestHash, StringComparison.Ordinal))
            return new(IdempotencyClaimState.Conflict, existing.LeaseToken, existing.StatusCode, existing.ResponseJson);

        if (existing.Completed)
            return new(IdempotencyClaimState.Completed, existing.LeaseToken, existing.StatusCode, existing.ResponseJson);

        if (timeProvider.GetUtcNow() - existing.ClaimedAtUtc <= LeaseTimeout)
            return new(IdempotencyClaimState.InFlight, existing.LeaseToken, null, null);

        existing.LeaseToken = Guid.NewGuid().ToString("N");
        existing.ClaimedAtUtc = timeProvider.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
        return new(IdempotencyClaimState.Claimed, existing.LeaseToken, null, null);
    }

    public async Task CompleteAsync(
        string scope,
        string key,
        string leaseToken,
        int statusCode,
        string responseJson,
        CancellationToken cancellationToken = default)
    {
        var entry = await db.IdempotencyEntries
            .SingleAsync(x => x.Scope == scope && x.Key == key, cancellationToken);

        if (!string.Equals(entry.LeaseToken, leaseToken, StringComparison.Ordinal))
            throw new InvalidOperationException("IDEMPOTENCY_LEASE_LOST");

        entry.Completed = true;
        entry.StatusCode = statusCode;
        entry.ResponseJson = responseJson;
        entry.CompletedAtUtc = timeProvider.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task AcquireKeyLockAsync(string scope, string key, CancellationToken cancellationToken)
    {
        var connection = db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = "SELECT pg_advisory_xact_lock(hashtextextended(@scope || ':' || @key, 0));";

        var p1 = command.CreateParameter();
        p1.ParameterName = "@scope";
        p1.Value = scope;
        command.Parameters.Add(p1);

        var p2 = command.CreateParameter();
        p2.ParameterName = "@key";
        p2.Value = key;
        command.Parameters.Add(p2);

        await command.ExecuteScalarAsync(cancellationToken);
    }
}
