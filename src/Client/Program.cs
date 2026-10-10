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
    .BindConfiguration(AgentIdentityOptions.SectionName)
    .Validate(options =>
    {
        try
        {
            _ = options.ResolveRootPath();
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }, "Agent RootPath must resolve to an absolute path.")
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
builder.Services.AddSingleton<IAgentEnrollmentBootstrapper, AgentEnrollmentBootstrapper>();
builder.Services.AddSingleton<IAgentAccessTokenProvider, AgentAccessTokenProvider>();
builder.Services.AddSingleton<IAgentTransport, SignalRAgentTransport>();
builder.Services.AddHostedService<AgentWorker>();

await builder.Build().RunAsync();
