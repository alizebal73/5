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
using GameNet.Shared.Contracts.V1.Api;
using GameNet.Shared.Contracts.V1.System;

var builder = WebApplication.CreateBuilder(args);

var databaseConnection = Environment.GetEnvironmentVariable("GAMENET_DATABASE_CONNECTION");
if (!string.IsNullOrWhiteSpace(databaseConnection) &&
    string.IsNullOrWhiteSpace(builder.Configuration["GameNet:DatabaseConnectionString"]))
{
    builder.Configuration["GameNet:DatabaseConnectionString"] = databaseConnection;
}

var bootstrapSecret = Environment.GetEnvironmentVariable("GAMENET_BOOTSTRAP_SECRET");
if (!string.IsNullOrWhiteSpace(bootstrapSecret) &&
    string.IsNullOrWhiteSpace(builder.Configuration["GameNet:Setup:BootstrapSecret"]))
{
    builder.Configuration["GameNet:Setup:BootstrapSecret"] = bootstrapSecret;
}

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
if (app.Environment.IsProduction() && !runtimeOptions.Authentication.Enabled)
    throw new InvalidOperationException("Production authentication must be enabled.");

app.MapAgentCredentialRoutes();
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
