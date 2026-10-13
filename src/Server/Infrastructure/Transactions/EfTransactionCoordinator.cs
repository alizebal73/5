using GameNet.Server.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace GameNet.Server.Infrastructure.Transactions;

public sealed class EfTransactionCoordinator(GameNetDbContext db) : ITransactionCoordinator
{
    public async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken = default)
    {
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken);
            try
            {
                var result = await action(cancellationToken);
                await db.SaveChangesAsync(cancellationToken);
                await tx.CommitAsync(cancellationToken);
                return result;
            }
            catch (Exception ex) when (ContainsUniqueViolation(ex))
            {
                await RollbackIfActiveAsync(tx, cancellationToken);
                db.ChangeTracker.Clear();
                throw new PersistenceConflictException("persistence.unique", ex);
            }
            catch (PostgresException ex) when (
                ex.SqlState is PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.DeadlockDetected)
            {
                // PostgreSQL/Npgsql may already have completed the transaction after a
                // serialization failure. Do not let a secondary rollback exception mask
                // the concurrency failure or prevent a bounded retry.
                await RollbackIfActiveAsync(tx, cancellationToken);
                db.ChangeTracker.Clear();

                if (attempt == 3)
                    throw new InvalidOperationException("TRANSACTION_RETRY_EXHAUSTED", ex);
            }
        }

        throw new InvalidOperationException("TRANSACTION_RETRY_EXHAUSTED");
    }

    private static async Task RollbackIfActiveAsync(
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction,
        CancellationToken cancellationToken)
    {
        var dbTransaction = transaction.GetDbTransaction();
        if (dbTransaction.Connection is null)
            return;

        try
        {
            await transaction.RollbackAsync(cancellationToken);
        }
        catch (InvalidOperationException) when (dbTransaction.Connection is null)
        {
            // The provider completed the transaction between the state check and rollback.
        }
    }

    private static bool ContainsUniqueViolation(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
            if (current is PostgresException pg && string.Equals(pg.SqlState, PostgresErrorCodes.UniqueViolation, StringComparison.Ordinal))
                return true;
        return false;
    }
}
