using GameNet.Server.Infrastructure.Persistence;
using GameNet.Server.Modules.Customers.Domain;
using GameNet.Server.Modules.Identity.Domain;
using GameNet.Server.Modules.Stations.Domain;
using Microsoft.EntityFrameworkCore;

namespace GameNet.Server.Persistence;

public sealed class GameNetDbContext(DbContextOptions<GameNetDbContext> options) : DbContext(options)
{
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<OperatorUser> OperatorUsers => Set<OperatorUser>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<AuthSession> AuthSessions => Set<AuthSession>();
    public DbSet<Station> Stations => Set<Station>();
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();
    public DbSet<IdempotencyEntry> IdempotencyEntries => Set<IdempotencyEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(GameNetDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
