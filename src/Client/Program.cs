using GameNet.Agent;
using GameNet.Agent.Identity;
using GameNet.Agent.Transport;
using Microsoft.Extensions.Options;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddWindowsService(options => options.ServiceName = "GameNet 5 Agent");
builder.Services.AddSingleton(TimeProvider.System);

builder.Services
    .AddOptions<AgentIdentityOptions>()
    .BindConfiguration(AgentIdentityOptions.SectionName);

builder.Services
    .AddOptions<AgentTransportOptions>()
    .BindConfiguration(AgentTransportOptions.SectionName)
    .Validate(options => Uri.TryCreate(options.ServerBaseUrl, UriKind.Absolute, out _), "ServerBaseUrl must be an absolute URI.")
    .ValidateOnStart();

builder.Services.AddHttpClient("GameNetAgentCredentialClient");
builder.Services.AddSingleton<IAgentIdentityStore, AgentIdentityStore>();
builder.Services.AddSingleton<IAgentCredentialStore, AgentCredentialStore>();
builder.Services.AddSingleton<IAgentAccessTokenProvider, AgentAccessTokenProvider>();
builder.Services.AddSingleton<IAgentTransport, SignalRAgentTransport>();
builder.Services.AddHostedService<AgentWorker>();

await builder.Build().RunAsync();
