using GameNet.Shared.Contracts.V1.Customers;

namespace GameNet.CustomerClient.Features;

/// <summary>
/// Client-side boundary for submitting customer credentials to the local Agent bridge.
/// The implementation never decides eligibility, Wallet balance or Session duration.
/// </summary>
public interface ICustomerEntryGateway
{
    Task<CustomerClientEntryResponse> StartAsync(
        CustomerClientEntryRequest request,
        CancellationToken cancellationToken = default);
}
