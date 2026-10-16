using GameNet.Server.Modules.Customers.Contracts;
using GameNet.Shared.Contracts.V1.Customers;
using GameNet.Shared.Primitives;

namespace GameNet.Server.Modules.Customers.Application;

public interface ICustomerManagementService
{
    Task<Result<IReadOnlyList<CustomerResponse>>> ListAsync(
        CustomerActorContext actor, string? search, bool includeInactive, int take, CancellationToken cancellationToken);

    Task<Result<CustomerResponse>> CreateAsync(CreateCustomerCommand command, CancellationToken cancellationToken);

    Task<Result<CustomerResponse>> UpdateProfileAsync(
        UpdateCustomerProfileCommand command, CancellationToken cancellationToken);

    Task<Result<CustomerResponse>> SetStatusAsync(
        SetCustomerStatusCommand command, CancellationToken cancellationToken);
}
