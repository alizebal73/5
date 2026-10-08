using GameNet.Server.Modules.Identity.Application;
using GameNet.Server.Modules.Identity.Domain;
using GameNet.Server.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GameNet.Server.Infrastructure.Persistence.Identity;

public sealed class EfIdentityRepository(GameNetDbContext db) : IIdentityRepository
{
    public async Task AcquireBootstrapLockAsync(CancellationToken cancellationToken)
    {
        var connection = db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = "SELECT pg_advisory_xact_lock(8217351);";
        await command.ExecuteScalarAsync(cancellationToken);
    }

    public Task<bool> AnyUsersAsync(CancellationToken cancellationToken) =>
        db.OperatorUsers.AsNoTracking().AnyAsync(cancellationToken);

    public Task<OperatorUser?> FindUserAsync(string username, CancellationToken cancellationToken) =>
        db.OperatorUsers.SingleOrDefaultAsync(x => x.Username == username, cancellationToken);

    public Task<OperatorUser?> FindUserByIdAsync(Guid userId, CancellationToken cancellationToken) =>
        db.OperatorUsers.SingleOrDefaultAsync(x => x.Id == userId, cancellationToken);

    public async Task<IReadOnlySet<string>> GetPermissionsAsync(Guid userId, CancellationToken cancellationToken)
    {
        var values = await (
            from ur in db.UserRoles
            join rp in db.RolePermissions on ur.RoleId equals rp.RoleId
            where ur.UserId == userId
            select rp.Permission)
            .ToListAsync(cancellationToken);

        return values.ToHashSet(StringComparer.Ordinal);
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
        db.AuthSessions.SingleOrDefaultAsync(x => x.Jti == jti, cancellationToken);

    public Task UpdateSessionAsync(AuthSession session, CancellationToken cancellationToken)
    {
        db.AuthSessions.Update(session);
        return Task.CompletedTask;
    }
}
