using GameNet.Shared.Contracts.V1.Customers;

namespace GameNet.Agent.Transport;

public interface IAgentCustomerSessionGateway
{
    Task<CustomerClientEntryResponse> StartAsync(
        CustomerClientEntryRequest request,
        CancellationToken cancellationToken);
}
