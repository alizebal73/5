using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace GameNet.Server.Persistence;

public sealed class GameNetDbContextFactory : IDesignTimeDbContextFactory<GameNetDbContext>
{
    public GameNetDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<GameNetDbContext>()
            .UseNpgsql("Host=127.0.0.1;Database=gamenet_design;Username=postgres")
            .Options;

        return new GameNetDbContext(options);
    }
}
