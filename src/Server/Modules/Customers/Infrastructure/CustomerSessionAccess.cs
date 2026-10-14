using GameNet.Server.Modules.Customers.Contracts;
using GameNet.Server.Modules.Customers.Domain;
using GameNet.Server.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GameNet.Server.Modules.Customers.Infrastructure;

/// <summary>
/// Participates in the ambient use-case transaction. It deliberately does not call SaveChanges
/// or open a second transaction; the owning mutation pipeline commits all related records once.
/// </summary>
public sealed class CustomerSessionAccess(GameNetDbContext db) : ICustomerSessionAccess
{
    public async Task<CustomerReference?> FindActiveAsync(
        Guid customerId,
        CancellationToken cancellationToken)
    {
        var customer = await db.Customers.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == customerId && x.IsActive, cancellationToken);
        return customer is null ? null : new CustomerReference(customer.Id, customer.Code);
    }

    public async Task<CustomerReference> CreateWalkInAsync(
        DateTimeOffset createdAtUtc,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var customer = Customer.CreateWalkIn(Guid.NewGuid(), createdAtUtc);
        await db.Customers.AddAsync(customer, cancellationToken);
        return new CustomerReference(customer.Id, customer.Code);
    }
}
