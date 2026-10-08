using GameNet.Server.Persistence;
using Microsoft.EntityFrameworkCore;
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
            catch (PostgresException ex) when ((ex.SqlState is PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.DeadlockDetected) && attempt < 3)
            {
                await tx.RollbackAsync(cancellationToken);
                db.ChangeTracker.Clear();
            }
        }
        throw new InvalidOperationException("TRANSACTION_RETRY_EXHAUSTED");
    }
}
