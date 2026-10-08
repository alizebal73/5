using GameNet.Server.Modules.Customers.Application;
using GameNet.Server.Modules.Customers.Domain;
using GameNet.Server.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GameNet.Server.Infrastructure.Persistence.Customers;

public sealed class EfCustomerRepository(GameNetDbContext db) : ICustomerRepository
{
    public async Task<IReadOnlyList<Customer>> SearchAsync(
        string? search,
        CancellationToken cancellationToken)
    {
        var query = db.Customers.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToUpper();
            query = query.Where(x =>
                x.Code.ToUpper().Contains(term) ||
                x.DisplayName.ToUpper().Contains(term) ||
                (x.Phone != null && x.Phone.Contains(search.Trim())));
        }

        return await query
            .OrderBy(x => x.Code)
            .Take(200)
            .ToListAsync(cancellationToken);
    }

    public Task<Customer?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        db.Customers.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

    public async Task AddAsync(Customer customer, CancellationToken cancellationToken) =>
        await db.Customers.AddAsync(customer, cancellationToken);
}
