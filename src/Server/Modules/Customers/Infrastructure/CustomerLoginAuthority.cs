using GameNet.Server.Modules.Customers.Contracts;
using GameNet.Server.Persistence;
using GameNet.Shared.Primitives;
using Microsoft.EntityFrameworkCore;

namespace GameNet.Server.Modules.Customers.Infrastructure;

public sealed class CustomerLoginAuthority(GameNetDbContext db, IGameClock clock) : ICustomerLoginAuthority
{
    public Task<bool> IsLoginUsableAsync(Guid customerId, string jti, CancellationToken cancellationToken)
    {
        if (customerId == Guid.Empty || string.IsNullOrWhiteSpace(jti) || jti.Length != 32)
            return Task.FromResult(false);
        var now = clock.UtcNow;
        return (from login in db.CustomerLoginSessions.AsNoTracking()
                join customer in db.Customers.AsNoTracking() on login.CustomerId equals customer.Id
                where login.Jti == jti && login.CustomerId == customerId &&
                      login.RevokedAtUtc == null && login.ExpiresAtUtc > now &&
                      customer.Kind == GameNet.Server.Modules.Customers.Domain.CustomerKind.Registered &&
                      customer.IsActive && !customer.IsBlocked && customer.LoginUsername != null && customer.PasswordHash != null
                select login.Jti).AnyAsync(cancellationToken);
    }
}
