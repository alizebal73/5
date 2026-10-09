using GameNet.Agent;
using GameNet.Agent.Identity;
using GameNet.Agent.Transport;
using GameNet.Shared.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

var builder = Host.CreateApplicationBuilder(args);

AgentRuntimeConfiguration.AddProgramDataConfiguration(
    builder.Configuration,
    builder.Services,
    GameNetRuntimePaths.AgentConfigurationFileName);

builder.Services.AddWindowsService(options => options.ServiceName = "GameNet 5 Agent");
builder.Services.AddSingleton(TimeProvider.System);

builder.Services
    .AddOptions<AgentIdentityOptions>()
    .BindConfiguration(AgentIdentityOptions.SectionName);

builder.Services
    .AddOptions<AgentTransportOptions>()
    .BindConfiguration(AgentTransportOptions.SectionName)
    .Validate(
        options => ServerEndpointPolicy.IsValidBaseUrl(options.ServerBaseUrl),
        "ServerBaseUrl must be HTTPS for network endpoints; HTTP is permitted only for loopback development and certification.")
    .ValidateOnStart();

builder.Services.AddHttpClient("GameNetAgentCredentialClient");
builder.Services.AddSingleton<IAgentIdentityStore, AgentIdentityStore>();
builder.Services.AddSingleton<IAgentCredentialStore, AgentCredentialStore>();
builder.Services.AddSingleton<IAgentAccessTokenProvider, AgentAccessTokenProvider>();
builder.Services.AddSingleton<IAgentTransport, SignalRAgentTransport>();
builder.Services.AddHostedService<AgentWorker>();

await builder.Build().RunAsync();
