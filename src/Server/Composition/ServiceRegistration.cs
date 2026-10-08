using GameNet.Server.Application;
using GameNet.Server.Infrastructure;
using GameNet.Server.Infrastructure.Audit;
using GameNet.Server.Infrastructure.Idempotency;
using GameNet.Server.Infrastructure.Observability;
using GameNet.Server.Infrastructure.Persistence.Stations;
using GameNet.Server.Infrastructure.Security;
using GameNet.Server.Infrastructure.Transactions;
using GameNet.Server.Modules.Stations.Application;
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
        services.AddScoped<IAuditWriter, PostgresAuditWriter>();
        services.AddScoped<IIdempotencyStore, PostgresIdempotencyStore>();
        services.AddScoped<IStationRepository, EfStationRepository>();
        services.AddScoped<StationService>();

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
