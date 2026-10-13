using GameNet.Server.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GameNet.Server.Infrastructure.Realtime;

/// <summary>
/// Holds a PostgreSQL session-level advisory lock for one device. The lock must be
/// acquired before starting any transaction so a waiting SERIALIZABLE transaction
/// does not retain a snapshot from before the preceding fenced operation committed.
/// </summary>
internal sealed class AgentDeviceFenceLock : IAsyncDisposable
{
    private readonly GameNetDbContext dbContext;
    private readonly string lockKey;
    private bool disposed;

    private AgentDeviceFenceLock(GameNetDbContext dbContext, string lockKey)
    {
        this.dbContext = dbContext;
        this.lockKey = lockKey;
    }

    public static async Task<AgentDeviceFenceLock> AcquireAsync(
        GameNetDbContext dbContext,
        string deviceId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);
        if (dbContext.Database.CurrentTransaction is not null)
            throw new InvalidOperationException("AGENT_DEVICE_FENCE_MUST_PRECEDE_TRANSACTION");

        var key = "GameNet.Agent.Device:" + deviceId;
        await dbContext.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            var connection = dbContext.Database.GetDbConnection();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT pg_advisory_lock(hashtextextended(@lockKey, 0))";
            var parameter = command.CreateParameter();
            parameter.ParameterName = "lockKey";
            parameter.Value = key;
            command.Parameters.Add(parameter);
            await command.ExecuteScalarAsync(cancellationToken);

            return new AgentDeviceFenceLock(dbContext, key);
        }
        catch
        {
            // Cancellation can race a successful server-side lock acquisition. Attempt
            // an unconditional release before returning this session to the pool.
            try
            {
                var connection = dbContext.Database.GetDbConnection();
                await using var unlock = connection.CreateCommand();
                unlock.CommandText = "SELECT pg_advisory_unlock(hashtextextended(@lockKey, 0))";
                var parameter = unlock.CreateParameter();
                parameter.ParameterName = "lockKey";
                parameter.Value = key;
                unlock.Parameters.Add(parameter);
                await unlock.ExecuteScalarAsync(CancellationToken.None);
            }
            catch
            {
                // A broken connection releases its session lock when the backend ends.
            }

            try { await dbContext.Database.CloseConnectionAsync(); }
            catch { /* Preserve the original acquisition/cancellation error. */ }
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed)
            return;
        disposed = true;

        try
        {
            var connection = dbContext.Database.GetDbConnection();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT pg_advisory_unlock(hashtextextended(@lockKey, 0))";
            var parameter = command.CreateParameter();
            parameter.ParameterName = "lockKey";
            parameter.Value = lockKey;
            command.Parameters.Add(parameter);
            var released = await command.ExecuteScalarAsync(CancellationToken.None);
            if (released is not bool didRelease || !didRelease)
                throw new InvalidOperationException("AGENT_DEVICE_FENCE_UNLOCK_FAILED");
        }
        finally
        {
            await dbContext.Database.CloseConnectionAsync();
        }
    }
}
