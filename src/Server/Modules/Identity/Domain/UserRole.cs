namespace GameNet.Server.Modules.Identity.Domain;

public sealed class UserRole
{
    private UserRole() { }
    public UserRole(Guid userId, Guid roleId)
    {
        if (userId == Guid.Empty) throw new ArgumentException("User id is required.", nameof(userId));
        if (roleId == Guid.Empty) throw new ArgumentException("Role id is required.", nameof(roleId));
        UserId = userId;
        RoleId = roleId;
    }
    public Guid UserId { get; private set; }
    public Guid RoleId { get; private set; }
}
