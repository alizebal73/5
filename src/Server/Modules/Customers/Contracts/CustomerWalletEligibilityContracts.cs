using GameNet.Shared.Primitives;

namespace GameNet.Server.Modules.Customers.Contracts;

/// <summary>Public customer facts required by another module without exposing Customer domain types.</summary>
public sealed record CustomerWalletSnapshot(Guid CustomerId, string Code, int Version);

public interface ICustomerWalletEligibility
{
    Task<Result<CustomerWalletSnapshot>> GetRegisteredCustomerAsync(Guid customerId, CancellationToken cancellationToken);
    Task<Result<CustomerWalletSnapshot>> LockActiveRegisteredCustomerForWalletTopUpAsync(
        Guid customerId, int expectedVersion, CancellationToken cancellationToken);
}