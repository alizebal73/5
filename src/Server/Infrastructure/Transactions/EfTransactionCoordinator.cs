using GameNet.Server.Application;
using GameNet.Server.Persistence;
using Npgsql;
using Microsoft.EntityFrameworkCore;

namespace GameNet.Server.Infrastructure.Transactions;

public sealed class EfTransactionCoordinator(GameNetDbContext db) : ITransactionCoordinator
{
    public async Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> action,
        CancellationToken cancellationToken = default)
    {
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            await using var tx = await db.Database.BeginTransactionAsync(
                System.Data.IsolationLevel.Serializable,
                cancellationToken);

            try
            {
                var result = await action(cancellationToken);
                await db.SaveChangesAsync(cancellationToken);
                await tx.CommitAsync(cancellationToken);
                return result;
            }
            catch (PostgresException ex) when (
                (ex.SqlState is PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.DeadlockDetected)
                && attempt < 3)
            {
                await tx.RollbackAsync(cancellationToken);
                db.ChangeTracker.Clear();
            }
            catch (DbUpdateConcurrencyException)
            {
                await tx.RollbackAsync(cancellationToken);
                db.ChangeTracker.Clear();
                throw new PersistenceConflictException("persistence.concurrency", "The data changed concurrently.");
            }
            catch (DbUpdateException ex) when (IsUniqueViolation(ex))
            {
                await tx.RollbackAsync(cancellationToken);
                db.ChangeTracker.Clear();
                throw new PersistenceConflictException("persistence.unique", "A uniqueness constraint was violated.");
            }
        }

        throw new InvalidOperationException("TRANSACTION_RETRY_EXHAUSTED");
    }

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}
