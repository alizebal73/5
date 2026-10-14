namespace GameNet.Server.Modules.Sessions.Contracts;

/// <summary>
/// Exposes only Session version facts needed by Billing to preview and settle pending
/// invoices. Session persistence remains owned by the Sessions module.
/// </summary>
public interface ISessionCheckoutAccess
{
    Task<IReadOnlyDictionary<Guid, int>> GetSessionVersionsAsync(
        IReadOnlyCollection<Guid> sessionIds,
        CancellationToken cancellationToken);
}
