using GameNet.Server.Persistence;
using Microsoft.EntityFrameworkCore.Storage;

namespace GameNet.Server.Infrastructure.Realtime;

/// <summary>
/// Serializes lease acquisition, outbound dispatch, and credential revocation/rotation
/// for one device across every Server process sharing PostgreSQL.
/// </summary>
internal static class AgentDeviceFenceLock
{
    public static async Task AcquireAsync(
        GameNetDbContext dbContext,
        string deviceId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);

        var transaction = dbContext.Database.CurrentTransaction
            ?? throw new InvalidOperationException("AGENT_DEVICE_FENCE_REQUIRES_TRANSACTION");

        await using var command = dbContext.Database.GetDbConnection().CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        command.CommandText = "SELECT pg_advisory_xact_lock(hashtextextended(@lockKey, 0))";

        var parameter = command.CreateParameter();
        parameter.ParameterName = "lockKey";
        parameter.Value = "GameNet.Agent.Device:" + deviceId;
        command.Parameters.Add(parameter);

        // Transaction-level advisory locks are released automatically on commit/rollback.
        // The key is namespaced to avoid sharing a lock domain with unrelated app locks.
        await command.ExecuteScalarAsync(cancellationToken);
    }
}
