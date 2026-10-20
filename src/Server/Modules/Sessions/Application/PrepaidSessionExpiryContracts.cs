namespace GameNet.Server.Modules.Sessions.Application;

public interface IPrepaidSessionExpiryService
{
    Task<int> ExpireDueAsync(CancellationToken cancellationToken);
}
