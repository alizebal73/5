namespace GameNet.Server.Modules.Customers.Contracts;

public sealed record CustomerReference(Guid Id, string Code);

/// <summary>
/// Session orchestration accesses Customers only through this contract. Implementations
/// participate in the caller's current scoped DbContext/transaction and must not commit it.
/// </summary>
public interface ICustomerSessionAccess
{
    Task<CustomerReference?> FindActiveAsync(Guid customerId, CancellationToken cancellationToken);
    Task<CustomerReference> CreateWalkInAsync(DateTimeOffset createdAtUtc, CancellationToken cancellationToken);
}
