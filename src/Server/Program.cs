using GameNet.Server.Composition;
using GameNet.Server.Infrastructure;
using GameNet.Server.Infrastructure.Configuration;
using GameNet.Server.Infrastructure.Health;
using GameNet.Server.Infrastructure.Observability;
using GameNet.Server.Infrastructure.Realtime;
using GameNet.Server.Infrastructure.Security;
using GameNet.Server.Infrastructure.Security.Transport;
using GameNet.Server.Modules.Identity.Api;
using GameNet.Server.Modules.Stations.Api;
using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore;
using GameNet.Server.Persistence;
using GameNet.Shared.Contracts.V1.Api;
using GameNet.Shared.Contracts.V1.System;

if (args.Any(argument => string.Equals(argument, "--provision-secrets", StringComparison.Ordinal)))
{
    if (args.Length != 1)
        throw new InvalidOperationException("Run secret provisioning with --provision-secrets as the only argument.");
    if (!OperatingSystem.IsWindows())
        throw new ServerSecretStoreException("Server secret-store provisioning requires Windows.");

    ServerSecretProvisioning.RunInteractive();
    return;
}

var migrateOnly = args.Contains("--migrate-only", StringComparer.Ordinal);
var hostArguments = args
    .Where(argument => !string.Equals(argument, "--migrate-only", StringComparison.Ordinal))
    .ToArray();
var builder = WebApplication.CreateBuilder(hostArguments);

ServerTlsHostConfiguration.AddProgramDataConfiguration(builder);

var protectedSettingsEnabled =
    builder.Environment.IsProduction() &&
    bool.TryParse(builder.Configuration["GameNet:ProtectedSettings:Enabled"], out var enableProtectedFile) &&
    enableProtectedFile;
ProductionStartupPolicy.EnsureProtectedSettingsEnabled(
    builder.Environment.EnvironmentName,
    protectedSettingsEnabled);
ProductionStartupPolicy.EnsureProtectedSettingsPathOverrideAllowed(
    builder.Environment.EnvironmentName,
    Environment.GetEnvironmentVariable(ProtectedServerSettings.PathEnvironmentVariableName));
ProtectedServerSettings.LoadInto(builder.Configuration, protectedSettingsEnabled);
ServerTlsHostConfiguration.ConfigureListeners(builder);

var serverSecrets = ServerSecretBootstrap.Load(builder.Environment, builder.Configuration, args);
builder.Services.AddSingleton<IDatabaseConnectionSecret>(serverSecrets);
builder.Services.AddSingleton<IJwtSigningKeySecret>(serverSecrets);
builder.Services.AddSingleton<IAgentProvisioningKeySecret>(serverSecrets);

builder.Host.UseWindowsService(options => options.ServiceName = "GameNet 5 Server");
builder.Services.AddGameNetServer();

var app = builder.Build();
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseMiddleware<SecureTransportMiddleware>();
app.UseMiddleware<ContractVersionMiddleware>();
app.UseAuthentication();
app.UseAuthorization();

var runtimeOptions = app.Services.GetRequiredService<IOptions<GameNetOptions>>().Value;
ProductionStartupPolicy.EnsureAuthenticationEnabled(
    app.Environment.EnvironmentName,
    runtimeOptions.Authentication.Enabled);

if (migrateOnly)
{
    await using (var migrationScope = app.Services.CreateAsyncScope())
    {
        var db = migrationScope.ServiceProvider.GetRequiredService<GameNetDbContext>();
        var pendingBefore = (await db.Database.GetPendingMigrationsAsync()).ToArray();
        Console.WriteLine($"GameNet database migration mode: {pendingBefore.Length} pending migration(s).");
        await db.Database.MigrateAsync();

        var pendingAfter = (await db.Database.GetPendingMigrationsAsync()).ToArray();
        if (pendingAfter.Length != 0)
            throw new InvalidOperationException($"Database migration ended with {pendingAfter.Length} pending migration(s).");

        Console.WriteLine("GameNet database schema is current.");
    }

    await app.DisposeAsync();
    return;
}

app.MapAgentCredentialRoutes();
app.MapAgentEnrollmentRoutes();
app.MapIdentityEndpoints();
app.MapOperatorManagementEndpoints();
app.MapStationEndpoints();
app.MapHub<AgentHub>("/hubs/agent");

app.MapGet("/health", async (
    StartupState state,
    IServerReadinessProbe readiness,
    HttpContext context,
    CancellationToken cancellationToken) =>
{
    var id = CorrelationIdMiddleware.GetCurrent(context);
    var isReady = await readiness.IsReadyAsync(cancellationToken);
    var response = new HealthResponse(
        state.Service,
        StartupState.Version,
        HealthStatuses.Healthy,
        isReady ? HealthStatuses.Ready : HealthStatuses.NotReady,
        id);

    return Results.Ok(new ApiEnvelope<HealthResponse>(response, id));
}).AllowAnonymous();

app.Run();

public partial class Program;
