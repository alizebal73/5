using GameNet.Server.Modules.Identity.Domain;

namespace GameNet.Server.Modules.Identity.Application;

public interface IOperatorManagementRepository
{
    Task<IReadOnlyList<OperatorUserSnapshot>> ListUsersAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<RoleSnapshot>> ListRolesAsync(CancellationToken cancellationToken);
    Task<OperatorUser?> FindUserByIdAsync(Guid userId, CancellationToken cancellationToken);
    Task<Role?> FindRoleByIdAsync(Guid roleId, CancellationToken cancellationToken);
    Task<bool> UsernameExistsAsync(string normalizedUsername, CancellationToken cancellationToken);
    Task<bool> RoleCodeExistsAsync(string normalizedCode, CancellationToken cancellationToken);
    Task<bool> IsOwnerAsync(Guid userId, CancellationToken cancellationToken);
    Task<int> CountActiveOwnersAsync(CancellationToken cancellationToken);
    Task<IReadOnlySet<string>> GetRolePermissionsAsync(Guid roleId, CancellationToken cancellationToken);
    Task AddUserAsync(OperatorUser user, CancellationToken cancellationToken);
    Task AddRoleAsync(Role role, CancellationToken cancellationToken);
    Task AddUserRoleAsync(UserRole userRole, CancellationToken cancellationToken);
    Task AddRolePermissionAsync(RolePermission permission, CancellationToken cancellationToken);
    Task ReplaceRolePermissionsAsync(Guid roleId, IReadOnlyCollection<string> permissions, CancellationToken cancellationToken);
    Task ReplaceUserRoleAsync(Guid userId, Guid roleId, CancellationToken cancellationToken);
    Task RevokeActiveSessionsAsync(Guid userId, DateTimeOffset revokedAtUtc, CancellationToken cancellationToken);
}

public sealed record OperatorUserSnapshot(
    Guid Id, string Username, string DisplayName, bool IsActive, int FailedLoginCount,
    DateTimeOffset CreatedAtUtc, DateTimeOffset? LastLoginAtUtc, Guid[] RoleIds, string[] RoleCodes);
public sealed record RoleSnapshot(Guid Id, string Code, string Name, string[] Permissions);
public sealed record OperatorManagedDto(
    Guid Id, string Username, string DisplayName, bool IsActive, int FailedLoginCount,
    DateTimeOffset CreatedAtUtc, DateTimeOffset? LastLoginAtUtc, Guid[] RoleIds, string[] RoleCodes, bool IsOwner);
public sealed record RoleManagedDto(Guid Id, string Code, string Name, string[] Permissions, bool IsSystemRole);
public sealed record IdentityActor(Guid UserId, string CorrelationId, string Source);
public sealed record CreateOperatorCommand(
    string Username, string DisplayName, string Password, Guid RoleId, string IdempotencyKey, IdentityActor Actor);
public sealed record AssignOperatorRoleCommand(
    Guid OperatorId, Guid RoleId, string IdempotencyKey, IdentityActor Actor);
public sealed record SetOperatorActiveCommand(
    Guid OperatorId, bool IsActive, string IdempotencyKey, IdentityActor Actor);
public sealed record CreateRoleCommand(
    string Code, string Name, string[]? Permissions, string IdempotencyKey, IdentityActor Actor);
public sealed record UpdateRolePermissionsCommand(
    Guid RoleId, string[]? Permissions, string IdempotencyKey, IdentityActor Actor);
