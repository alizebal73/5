using System.IO;
using System.Net.Http.Headers;
using GameNet.Desktop.Api;
using GameNet.Desktop.Shell;
using GameNet.Shared.Runtime;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace GameNet.Desktop.Infrastructure;

public static class DesktopHost
{
    public static IHost Build()
    {
        var builder = Host.CreateApplicationBuilder();

        AddProgramDataConfiguration(
            builder.Configuration,
            builder.Services,
            GameNetRuntimePaths.DesktopConfigurationFileName);

        builder.Services
            .AddOptions<ServerConnectionOptions>()
            .BindConfiguration(ServerConnectionOptions.SectionName)
            .Validate(
                options => ServerEndpointPolicy.IsValidBaseUrl(options.BaseUrl),
                "Server BaseUrl must be HTTPS for network endpoints; HTTP is permitted only for loopback development and certification.")
            .ValidateOnStart();

        builder.Services.AddHttpClient<IGameNetServerClient, GameNetServerClient>((provider, client) =>
        {
            var options = provider.GetRequiredService<IOptions<ServerConnectionOptions>>().Value;
            client.BaseAddress = new Uri(options.BaseUrl, UriKind.Absolute);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        });

        builder.Services.AddSingleton<MainWindow>();
        return builder.Build();
    }

    private static void AddProgramDataConfiguration(
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
}
