using GameNet.Server.Persistence;

namespace GameNet.Server.Infrastructure.Health;

public interface IServerReadinessProbe
{
    Task<bool> IsReadyAsync(CancellationToken cancellationToken);
}

public sealed class ServerReadinessProbe(
    GameNetDbContext db,
    ILogger<ServerReadinessProbe> logger) : IServerReadinessProbe
{
    public async Task<bool> IsReadyAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (!await db.Database.CanConnectAsync(cancellationToken))
                return false;

            var pending = await db.Database.GetPendingMigrationsAsync(cancellationToken);
            return !pending.Any();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Server readiness probe failed.");
            return false;
        }
    }
}
