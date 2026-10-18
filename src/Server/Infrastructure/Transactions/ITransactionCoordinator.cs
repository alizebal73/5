namespace GameNet.Server.Infrastructure.Transactions;

public interface ITransactionCoordinator
{
    Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken = default);
}
