using GameNet.Shared.Primitives;

namespace GameNet.Server.Modules.Customers.Contracts;

/// <summary>Public customer facts required by another module without exposing Customer domain types.</summary>
public sealed record CustomerWalletSnapshot(Guid CustomerId, string Code, int Version);

public interface ICustomerWalletEligibility
{
    Task<Result<CustomerWalletSnapshot>> GetRegisteredCustomerAsync(Guid customerId, CancellationToken cancellationToken);
    Task<Result<CustomerWalletSnapshot>> LockActiveRegisteredCustomerForWalletTopUpAsync(
        Guid customerId, int expectedVersion, CancellationToken cancellationToken);
    Task<Result<CustomerWalletSnapshot>> LockActiveRegisteredCustomerForSessionAsync(
        Guid customerId, CancellationToken cancellationToken) =>
        Task.FromResult(Result<CustomerWalletSnapshot>.Failure(
            new Error("customers.session_lock_unavailable", "Customer session lock is not configured.")));
}