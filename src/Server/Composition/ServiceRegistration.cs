using GameNet.Server.Infrastructure;
using GameNet.Server.Infrastructure.Audit;
using GameNet.Server.Infrastructure.Configuration;
using GameNet.Server.Infrastructure.Health;
using GameNet.Server.Infrastructure.Idempotency;
using GameNet.Server.Infrastructure.Outbox;
using GameNet.Server.Infrastructure.Realtime;
using GameNet.Server.Infrastructure.Security;
using GameNet.Server.Infrastructure.Transactions;
using GameNet.Server.Persistence;
using GameNet.Server.Modules.Identity.Application;
using GameNet.Server.Infrastructure.Persistence.Identity;
using GameNet.Server.Infrastructure.Persistence.Stations;
using GameNet.Server.Modules.Stations.Application;
using GameNet.Server.Modules.Identity.Infrastructure.Security;
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
        services.AddOptions<GameNetOptions>()
            .BindConfiguration(GameNetOptions.SectionName)
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<GameNetOptions>, GameNetOptionsValidator>();

        services.AddScoped<ITransactionCoordinator, EfTransactionCoordinator>();
        services.AddScoped<IServerReadinessProbe, ServerReadinessProbe>();
        services.AddScoped<IAuditWriter, EfAuditWriter>();
        services.AddScoped<IIdempotencyStore, EfIdempotencyStore>();
        services.AddSingleton<IRequestFingerprint, HmacRequestFingerprint>();
        services.AddScoped<IOutboxWriter, EfOutboxWriter>();
        services.AddScoped<IOutboxDispatcher, EfOutboxDispatcher>();
        services.AddScoped<IAgentConnectionLeaseStore, EfAgentConnectionLeaseStore>();
        services.AddSingleton<AgentCommandResultCoordinator>();
        services.AddScoped<IAgentHealthProbeDispatcher, AgentHealthProbeDispatcher>();
        services.AddScoped<StationAgentHealthService>();
        services.AddScoped<IAgentCredentialService, AgentCredentialService>();
        services.AddScoped<IAgentEnrollmentService, AgentEnrollmentService>();
        services.AddScoped<IIdentityRepository, EfIdentityRepository>();
        services.AddScoped<IdentityService>();
        services.AddScoped<IOperatorManagementRepository, EfOperatorManagementRepository>();
        services.AddScoped<OperatorManagementService>();
        services.AddScoped<IStationRepository, EfStationRepository>();
        services.AddScoped<StationService>();
        services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();
        services.AddSingleton<ITokenService, JwtTokenService>();
        services.AddSingleton<IAgentAccessTokenIssuer, AgentAccessTokenIssuer>();

        services.AddSignalR();
        services.AddGameNetAuthentication();

        services.AddDbContext<GameNetDbContext>((provider, db) =>
        {
            var options = provider.GetRequiredService<IOptions<GameNetOptions>>().Value;
            if (!string.IsNullOrWhiteSpace(options.DatabaseConnectionString))
            {
                db.UseNpgsql(
                    options.DatabaseConnectionString,
                    npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "public"));
            }
        });

        return services;
    }
}
