using GameNet.Agent;
using GameNet.Agent.Identity;
using GameNet.Agent.Transport;
using GameNet.Shared.Runtime;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;

var builder = Host.CreateApplicationBuilder(args);

AddProgramDataConfiguration(
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

static void AddProgramDataConfiguration(
    IConfigurationManager configuration,
    IServiceCollection services,
    string fileName)
{
    var directory = GameNetRuntimePaths.ConfigurationDirectory;
    if (!Directory.Exists(directory))
        return;

    var fileProvider = new PhysicalFileProvider(directory);
    var source = new JsonConfigurationSource
    {
        FileProvider = fileProvider,
        Path = fileName,
        Optional = true,
        ReloadOnChange = false
    };

    // ProgramData is the installed runtime configuration. Environment and command-line
    // providers remain later in precedence so explicit diagnostic overrides still work.
    var environmentIndex = -1;
    for (var index = 0; index < configuration.Sources.Count; index++)
    {
        if (string.Equals(
                configuration.Sources[index].GetType().Name,
                "EnvironmentVariablesConfigurationSource",
                StringComparison.Ordinal))
        {
            environmentIndex = index;
            break;
        }
    }

    if (environmentIndex < 0)
        environmentIndex = configuration.Sources.Count;

    configuration.Sources.Insert(environmentIndex, source);
    services.AddSingleton<PhysicalFileProvider>(_ => fileProvider);
}
