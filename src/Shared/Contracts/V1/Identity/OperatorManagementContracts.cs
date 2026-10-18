namespace GameNet.Shared.Contracts.V1.Identity;

public sealed record OperatorAdminResponse(
    Guid Id, string Username, string DisplayName, bool IsActive, int FailedLoginCount,
    DateTimeOffset CreatedAtUtc, DateTimeOffset? LastLoginAtUtc,
    Guid[] RoleIds, string[] RoleCodes, bool IsOwner);
public sealed record RoleAdminResponse(Guid Id, string Code, string Name, string[] Permissions, bool IsSystemRole);
public sealed record CreateOperatorRequest(string Username, string DisplayName, string Password, Guid RoleId, string Reason);
public sealed record AssignOperatorRoleRequest(Guid RoleId, string Reason);
public sealed record SetOperatorActiveRequest(bool IsActive, string Reason);
public sealed record CreateRoleRequest(string Code, string Name, string[]? Permissions, string Reason);
public sealed record UpdateRolePermissionsRequest(string[]? Permissions, string Reason);
