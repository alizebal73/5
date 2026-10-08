using GameNet.Server.Modules.Identity.Application;
using GameNet.Server.Modules.Identity.Domain;
using GameNet.Server.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GameNet.Server.Modules.Identity.Infrastructure.Persistence;

public sealed class EfIdentityRepository(GameNetDbContext db) : IIdentityRepository
{
    public async Task AcquireBootstrapLockAsync(CancellationToken cancellationToken)
    {
        var transaction = db.Database.CurrentTransaction
            ?? throw new InvalidOperationException("IDENTITY_BOOTSTRAP_REQUIRES_TRANSACTION");
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        command.CommandText = "SELECT pg_advisory_xact_lock(1936025454, 5);";
        _ = await command.ExecuteScalarAsync(cancellationToken);
    }
    public Task<bool> AnyUsersAsync(CancellationToken cancellationToken) => db.OperatorUsers.AnyAsync(cancellationToken);
    public Task<OperatorUser?> FindUserAsync(string username, CancellationToken cancellationToken) =>
        db.OperatorUsers.SingleOrDefaultAsync(user => user.Username == username, cancellationToken);
    public Task<OperatorUser?> FindUserByIdAsync(Guid userId, CancellationToken cancellationToken) =>
        db.OperatorUsers.SingleOrDefaultAsync(user => user.Id == userId, cancellationToken);
    public async Task<IReadOnlySet<string>> GetPermissionsAsync(Guid userId, CancellationToken cancellationToken)
    {
        var permissions = await (from userRole in db.UserRoles
            join rolePermission in db.RolePermissions on userRole.RoleId equals rolePermission.RoleId
            where userRole.UserId == userId
            select rolePermission.Permission).Distinct().ToArrayAsync(cancellationToken);
        return new HashSet<string>(permissions, StringComparer.Ordinal);
    }
    public async Task AddUserAsync(OperatorUser user, CancellationToken cancellationToken) =>
        await db.OperatorUsers.AddAsync(user, cancellationToken);
    public async Task AddRoleAsync(Role role, CancellationToken cancellationToken) =>
        await db.Roles.AddAsync(role, cancellationToken);
    public async Task AddUserRoleAsync(UserRole userRole, CancellationToken cancellationToken) =>
        await db.UserRoles.AddAsync(userRole, cancellationToken);
    public async Task AddRolePermissionAsync(RolePermission permission, CancellationToken cancellationToken) =>
        await db.RolePermissions.AddAsync(permission, cancellationToken);
    public async Task AddSessionAsync(AuthSession session, CancellationToken cancellationToken) =>
        await db.AuthSessions.AddAsync(session, cancellationToken);
    public Task<AuthSession?> FindSessionAsync(string jti, CancellationToken cancellationToken) =>
        db.AuthSessions.SingleOrDefaultAsync(session => session.Jti == jti, cancellationToken);
    public Task UpdateSessionAsync(AuthSession session, CancellationToken cancellationToken)
    {
        if (db.Entry(session).State == EntityState.Detached) db.AuthSessions.Attach(session);
        return Task.CompletedTask;
    }
}
