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
            return await db.Database.CanConnectAsync(cancellationToken);
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
