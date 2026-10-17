using GameNet.Shared.Contracts.V1.Customers;

namespace GameNet.Server.Modules.Wallet.Contracts;

public sealed record WalletCustomerFacts(Guid Id, string Code, string? DisplayName,
    CustomerKindContract Kind, CustomerStatusContract Status);

public interface IWalletCustomerAccess
{
    Task<WalletCustomerFacts?> FindForWalletAsync(Guid customerId, CancellationToken cancellationToken);
}
