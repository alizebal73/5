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
                await tx.RollbackAsync(cancellationToken);
                db.ChangeTracker.Clear();
                throw new PersistenceConflictException("persistence.unique", ex);
            }
            catch (Exception ex) when (ContainsRetryableTransactionFailure(ex))
            {
                // EF/Npgsql can wrap SQLSTATE 40001/40P01 in an InvalidOperationException
                // when a provider execution strategy observes a transient error inside an
                // explicit serializable transaction. Inspect the full exception chain so
                // wrapped serialization failures get the same bounded whole-transaction retry.
                // Do not explicitly roll back here: transaction disposal at the end of this
                // attempt safely rolls back an active provider transaction.
                db.ChangeTracker.Clear();

                if (attempt == 3)
                    throw new InvalidOperationException("TRANSACTION_RETRY_EXHAUSTED", ex);
            }
        }

        throw new InvalidOperationException("TRANSACTION_RETRY_EXHAUSTED");
    }

    private static bool ContainsUniqueViolation(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
            if (current is PostgresException pg && string.Equals(pg.SqlState, PostgresErrorCodes.UniqueViolation, StringComparison.Ordinal))
                return true;
        return false;
    }

    private static bool ContainsRetryableTransactionFailure(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException pg &&
                pg.SqlState is PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.DeadlockDetected)
                return true;
        }

        return false;
    }
}
