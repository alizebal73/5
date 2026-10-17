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
        if (customer is null)
            return null;

        var kind = customer.Kind == CustomerKind.Registered
            ? CustomerKindContract.Registered
            : CustomerKindContract.WalkIn;
        var status = customer.Status switch
        {
            CustomerStatus.Active => CustomerStatusContract.Active,
            CustomerStatus.Inactive => CustomerStatusContract.Inactive,
            CustomerStatus.Blocked => CustomerStatusContract.Blocked,
            _ => throw new InvalidOperationException("Unsupported Customer status.")
        };
        return new WalletCustomerFacts(customer.Id, customer.Code, customer.DisplayName, kind, status);
    }
}
