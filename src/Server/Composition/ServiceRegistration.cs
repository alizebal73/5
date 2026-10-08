using GameNet.Server.Infrastructure;
using GameNet.Server.Infrastructure.Configuration;
using GameNet.Server.Infrastructure.Health;
using GameNet.Server.Infrastructure.Security;
using GameNet.Server.Infrastructure.Transactions;
using GameNet.Server.Persistence;
using GameNet.Shared.Primitives;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GameNet.Server.Composition;

public static class ServiceRegistration
{
    public static IServiceCollection AddGameNetServer(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IGameClock, SystemGameClock>();
        services.AddSingleton<StartupState>();
        services.AddOptions<GameNetOptions>().BindConfiguration(GameNetOptions.SectionName).ValidateOnStart();
        services.AddSingleton<IValidateOptions<GameNetOptions>, GameNetOptionsValidator>();

        services.AddScoped<ITransactionCoordinator, EfTransactionCoordinator>();
        services.AddScoped<IServerReadinessProbe, ServerReadinessProbe>();

        services.AddGameNetAuthentication();

        services.AddDbContext<GameNetDbContext>((provider, db) =>
        {
            var options = provider.GetRequiredService<IOptions<GameNetOptions>>().Value;
            if (!string.IsNullOrWhiteSpace(options.DatabaseConnectionString))
                db.UseNpgsql(options.DatabaseConnectionString);
        });

        return services;
    }
}
