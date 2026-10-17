using GameNet.Server.Modules.Customers.Domain;
using GameNet.Server.Modules.Wallet.Contracts;
using GameNet.Server.Persistence;
using GameNet.Shared.Contracts.V1.Customers;
using Microsoft.EntityFrameworkCore;

namespace GameNet.Server.Modules.Customers.Infrastructure;

public sealed class WalletCustomerAccess(GameNetDbContext db) : IWalletCustomerAccess
{
    public async Task<WalletCustomerFacts?> FindForWalletAsync(Guid customerId, CancellationToken cancellationToken)
    {
        var customer = await db.Customers.AsNoTracking().SingleOrDefaultAsync(x => x.Id == customerId, cancellationToken);
        return customer is null ? null : new WalletCustomerFacts(
            customer.Id, customer.Code, customer.DisplayName,
            (CustomerKindContract)customer.Kind, (CustomerStatusContract)customer.Status);
    }
}
