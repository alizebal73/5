namespace GameNet.Server.Application;

public interface ITransactionCoordinator
{
    Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> action,
        CancellationToken cancellationToken = default);
}
