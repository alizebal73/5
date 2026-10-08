using GameNet.Server.Modules.Identity.Domain;

namespace GameNet.Server.Modules.Identity.Application;

public interface IIdentityRepository
{
    Task AcquireBootstrapLockAsync(CancellationToken cancellationToken);
    Task<bool> AnyUsersAsync(CancellationToken cancellationToken);
    Task<OperatorUser?> FindUserAsync(string username, CancellationToken cancellationToken);
    Task<OperatorUser?> FindUserByIdAsync(Guid userId, CancellationToken cancellationToken);
    Task<IReadOnlySet<string>> GetPermissionsAsync(Guid userId, CancellationToken cancellationToken);
    Task AddUserAsync(OperatorUser user, CancellationToken cancellationToken);
    Task AddRoleAsync(Role role, CancellationToken cancellationToken);
    Task AddUserRoleAsync(UserRole userRole, CancellationToken cancellationToken);
    Task AddRolePermissionAsync(RolePermission permission, CancellationToken cancellationToken);
    Task AddSessionAsync(AuthSession session, CancellationToken cancellationToken);
    Task<AuthSession?> FindSessionAsync(string jti, CancellationToken cancellationToken);
    Task UpdateSessionAsync(AuthSession session, CancellationToken cancellationToken);
}
