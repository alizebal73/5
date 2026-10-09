using GameNet.Server.Modules.Identity.Application;
using GameNet.Server.Modules.Identity.Domain;
using GameNet.Server.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GameNet.Server.Infrastructure.Persistence.Identity;

public sealed class EfOperatorManagementRepository(GameNetDbContext db) : IOperatorManagementRepository
{
    public async Task<IReadOnlyList<OperatorUserSnapshot>> ListUsersAsync(CancellationToken ct)
    {
        var users = await db.OperatorUsers.AsNoTracking().OrderBy(x => x.Username)
            .Select(x => new { x.Id, x.Username, x.DisplayName, x.IsActive, x.FailedLoginCount, x.CreatedAtUtc, x.LastLoginAtUtc })
            .ToArrayAsync(ct);
        var roles = await (from ur in db.UserRoles.AsNoTracking()
            join role in db.Roles.AsNoTracking() on ur.RoleId equals role.Id
            select new { ur.UserId, ur.RoleId, role.Code }).ToArrayAsync(ct);
        return users.Select(user =>
        {
            var assigned = roles.Where(x => x.UserId == user.Id).OrderBy(x => x.Code, StringComparer.Ordinal).ToArray();
            return new OperatorUserSnapshot(user.Id, user.Username, user.DisplayName, user.IsActive,
                user.FailedLoginCount, user.CreatedAtUtc, user.LastLoginAtUtc,
                assigned.Select(x => x.RoleId).ToArray(), assigned.Select(x => x.Code).ToArray());
        }).ToArray();
    }

    public async Task<IReadOnlyList<RoleSnapshot>> ListRolesAsync(CancellationToken ct)
    {
        var roles = await db.Roles.AsNoTracking().OrderBy(x => x.Code)
            .Select(x => new { x.Id, x.Code, x.Name }).ToArrayAsync(ct);
        var permissions = await db.RolePermissions.AsNoTracking()
            .Select(x => new { x.RoleId, x.Permission }).ToArrayAsync(ct);
        return roles.Select(role => new RoleSnapshot(role.Id, role.Code, role.Name,
            permissions.Where(x => x.RoleId == role.Id).Select(x => x.Permission)
                .Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToArray())).ToArray();
    }

    public Task<OperatorUser?> FindUserByIdAsync(Guid id, CancellationToken ct) =>
        db.OperatorUsers.SingleOrDefaultAsync(x => x.Id == id, ct);
    public Task<Role?> FindRoleByIdAsync(Guid id, CancellationToken ct) =>
        db.Roles.SingleOrDefaultAsync(x => x.Id == id, ct);
    public Task<bool> UsernameExistsAsync(string username, CancellationToken ct) =>
        db.OperatorUsers.AnyAsync(x => x.Username == username, ct);
    public Task<bool> RoleCodeExistsAsync(string code, CancellationToken ct) =>
        db.Roles.AnyAsync(x => x.Code == code, ct);
    public Task<bool> IsOwnerAsync(Guid id, CancellationToken ct) =>
        (from ur in db.UserRoles.AsNoTracking()
         join role in db.Roles.AsNoTracking() on ur.RoleId equals role.Id
         where ur.UserId == id && role.Code == "owner"
         select ur.UserId).AnyAsync(ct);
    public Task<int> CountActiveOwnersAsync(CancellationToken ct) =>
        (from user in db.OperatorUsers.AsNoTracking()
         join ur in db.UserRoles.AsNoTracking() on user.Id equals ur.UserId
         join role in db.Roles.AsNoTracking() on ur.RoleId equals role.Id
         where user.IsActive && role.Code == "owner"
         select user.Id).Distinct().CountAsync(ct);

    public async Task<IReadOnlySet<string>> GetRolePermissionsAsync(Guid roleId, CancellationToken ct)
    {
        var values = await db.RolePermissions.AsNoTracking().Where(x => x.RoleId == roleId)
            .Select(x => x.Permission).ToArrayAsync(ct);
        return new HashSet<string>(values, StringComparer.Ordinal);
    }
    public async Task AddUserAsync(OperatorUser user, CancellationToken ct) => await db.OperatorUsers.AddAsync(user, ct);
    public async Task AddRoleAsync(Role role, CancellationToken ct) => await db.Roles.AddAsync(role, ct);
    public async Task AddUserRoleAsync(UserRole link, CancellationToken ct) => await db.UserRoles.AddAsync(link, ct);
    public async Task AddRolePermissionAsync(RolePermission permission, CancellationToken ct) => await db.RolePermissions.AddAsync(permission, ct);

    public async Task ReplaceRolePermissionsAsync(Guid roleId, IReadOnlyCollection<string> permissions, CancellationToken ct)
    {
        var desired = new HashSet<string>(permissions, StringComparer.Ordinal);
        var current = await db.RolePermissions.Where(x => x.RoleId == roleId).ToListAsync(ct);
        db.RolePermissions.RemoveRange(current.Where(x => !desired.Contains(x.Permission)));
        var existing = current.Select(x => x.Permission).ToHashSet(StringComparer.Ordinal);
        foreach (var permission in desired.Where(x => !existing.Contains(x)))
            await db.RolePermissions.AddAsync(new RolePermission(roleId, permission), ct);
    }

    public async Task ReplaceUserRoleAsync(Guid userId, Guid roleId, CancellationToken ct)
    {
        var current = await db.UserRoles.Where(x => x.UserId == userId).ToListAsync(ct);
        db.UserRoles.RemoveRange(current);
        await db.UserRoles.AddAsync(new UserRole(userId, roleId), ct);
    }

    public async Task RevokeActiveSessionsAsync(Guid userId, DateTimeOffset revokedAtUtc, CancellationToken ct)
    {
        var sessions = await db.AuthSessions
            .Where(x => x.UserId == userId && x.RevokedAtUtc == null && x.ExpiresAtUtc > revokedAtUtc)
            .ToListAsync(ct);
        foreach (var session in sessions) session.Revoke(revokedAtUtc);
    }
}
