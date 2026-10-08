using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace GameNet.Server.Persistence;

public sealed class GameNetDbContextFactory : IDesignTimeDbContextFactory<GameNetDbContext>
{
    public GameNetDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("GAMENET_DATABASE_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection))
            throw new InvalidOperationException("GAMENET_DATABASE_CONNECTION is required for EF design-time operations.");

        var options = new DbContextOptionsBuilder<GameNetDbContext>()
            .UseNpgsql(connection, npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "public"))
            .Options;

        return new GameNetDbContext(options);
    }
}
