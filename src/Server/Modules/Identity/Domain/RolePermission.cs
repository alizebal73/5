namespace GameNet.Server.Modules.Identity.Domain;

public sealed class RolePermission
{
    private RolePermission() { }
    public RolePermission(Guid roleId, string permission)
    {
        if (roleId == Guid.Empty) throw new ArgumentException("Role id is required.", nameof(roleId));
        if (string.IsNullOrWhiteSpace(permission) || permission.Trim().Length > 128)
            throw new ArgumentException("Permission must contain 1-128 characters.", nameof(permission));
        RoleId = roleId;
        Permission = permission.Trim();
    }
    public Guid RoleId { get; private set; }
    public string Permission { get; private set; } = null!;
}
