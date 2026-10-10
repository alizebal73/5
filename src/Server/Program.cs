using GameNet.Server.Composition;
using GameNet.Server.Infrastructure;
using GameNet.Server.Infrastructure.Configuration;
using GameNet.Server.Infrastructure.Health;
using GameNet.Server.Infrastructure.Observability;
using GameNet.Server.Infrastructure.Realtime;
using GameNet.Server.Infrastructure.Security;
using Microsoft.Extensions.Options;
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

var builder = WebApplication.CreateBuilder(args);
ServerTlsHostConfiguration.Configure(builder);

var serverSecrets = ServerSecretBootstrap.Load(builder.Environment, builder.Configuration, args);
builder.Services.AddSingleton<IDatabaseConnectionSecret>(serverSecrets);
builder.Services.AddSingleton<IJwtSigningKeySecret>(serverSecrets);
builder.Services.AddSingleton<IAgentProvisioningKeySecret>(serverSecrets);

builder.Host.UseWindowsService(options => options.ServiceName = "GameNet 5 Server");
builder.Services.AddGameNetServer();

var app = builder.Build();
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseMiddleware<ContractVersionMiddleware>();
app.UseAuthentication();
app.UseAuthorization();

var runtimeOptions = app.Services.GetRequiredService<IOptions<GameNetOptions>>().Value;
if (app.Environment.IsProduction() && !runtimeOptions.Authentication.Enabled)
    throw new InvalidOperationException("Production authentication must be enabled.");

app.MapAgentCredentialRoutes();
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
