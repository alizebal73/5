namespace GameNet.Server.Modules.Identity.Domain;

public sealed class RolePermission
{
    private RolePermission() { }

    public RolePermission(Guid roleId, string permission)
    {
        RoleId = roleId;
        Permission = Normalize(permission);
    }

    public Guid RoleId { get; private set; }
    public string Permission { get; private set; } = null!;

    private static string Normalize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Permission is required.", nameof(value));
        return value.Trim();
    }
}
