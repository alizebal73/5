using GameNet.Agent;
using GameNet.Agent.Identity;
using GameNet.Agent.Transport;
using Microsoft.Extensions.Options;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddWindowsService(options => options.ServiceName = "GameNet 5 Agent");
builder.Services.AddSingleton(TimeProvider.System);

builder.Services
    .AddOptions<AgentIdentityOptions>()
    .BindConfiguration(AgentIdentityOptions.SectionName)
    .Validate(options =>
        !string.IsNullOrWhiteSpace(options.RootPath) && Path.IsPathFullyQualified(options.RootPath),
        "Agent RootPath must be an absolute path.")
    .ValidateOnStart();

builder.Services
    .AddOptions<AgentTransportOptions>()
    .BindConfiguration(AgentTransportOptions.SectionName)
    .Validate(options =>
    {
        try
        {
            options.Validate();
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }, "Agent transport configuration is invalid.")
    .ValidateOnStart();

builder.Services.AddHttpClient("GameNetAgentCredentialClient");
builder.Services.AddSingleton<IAgentIdentityStore, AgentIdentityStore>();
builder.Services.AddSingleton<IAgentCredentialStore, AgentCredentialStore>();
builder.Services.AddSingleton<IAgentAccessTokenProvider, AgentAccessTokenProvider>();
builder.Services.AddSingleton<IAgentTransport, SignalRAgentTransport>();
builder.Services.AddHostedService<AgentWorker>();

await builder.Build().RunAsync();
