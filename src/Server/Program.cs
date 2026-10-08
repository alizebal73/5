using GameNet.Server.Composition;
using GameNet.Server.Infrastructure;
using GameNet.Server.Infrastructure.Observability;
using GameNet.Server.Infrastructure.Configuration;
using GameNet.Server.Modules.Customers.Api;
using GameNet.Server.Modules.Identity.Api;
using GameNet.Server.Modules.Stations.Api;
using GameNet.Shared.Contracts.V1.Api;
using GameNet.Shared.Contracts.V1.System;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);
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

app.MapGet("/health", (StartupState state, HttpContext context) =>
{
    var id = CorrelationIdMiddleware.GetCurrent(context);
    return Results.Ok(new ApiEnvelope<HealthResponse>(
        new(state.Service, StartupState.Version, HealthStatuses.Healthy, HealthStatuses.Ready, id),
        id));
}).AllowAnonymous();

app.MapIdentityEndpoints();
app.MapStationEndpoints();
app.MapCustomerEndpoints();

app.Run();

public partial class Program;
