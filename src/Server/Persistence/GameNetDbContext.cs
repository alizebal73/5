using GameNet.Server.Infrastructure.Persistence;
using GameNet.Server.Modules.Stations.Domain;
using Microsoft.EntityFrameworkCore;

namespace GameNet.Server.Persistence;

public sealed class GameNetDbContext(DbContextOptions<GameNetDbContext> options) : DbContext(options)
{
    public DbSet<Station> Stations => Set<Station>();
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();
    public DbSet<IdempotencyEntry> IdempotencyEntries => Set<IdempotencyEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(GameNetDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
