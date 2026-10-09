using GameNet.Shared.Runtime;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;

namespace GameNet.Agent.Transport;

public static class AgentRuntimeConfiguration
{
    public static void AddProgramDataConfiguration(
        IConfigurationManager configuration,
        IServiceCollection services,
        string fileName)
    {
        var testConfigurationDirectory = Environment.GetEnvironmentVariable(
            GameNetRuntimePaths.TestConfigurationDirectoryEnvironmentVariableName);
        var isTestConfiguration = !string.IsNullOrWhiteSpace(testConfigurationDirectory);
        var directory = GameNetRuntimePaths.ConfigurationDirectory;

        if (!Directory.Exists(directory))
        {
            if (isTestConfiguration)
            {
                throw new DirectoryNotFoundException(
                    $"{GameNetRuntimePaths.TestConfigurationDirectoryEnvironmentVariableName} points to a missing directory.");
            }

            return;
        }

        var configurationFile = Path.Combine(directory, fileName);
        if (isTestConfiguration && !File.Exists(configurationFile))
        {
            throw new FileNotFoundException(
                "The requested test runtime configuration file does not exist.",
                configurationFile);
        }

        var fileProvider = new PhysicalFileProvider(directory);
        var source = new JsonConfigurationSource
        {
            FileProvider = fileProvider,
            Path = fileName,
            Optional = !isTestConfiguration,
            ReloadOnChange = false
        };

        // Insert the runtime file after default JSON/in-memory sources but before the
        // unprefixed environment provider. A prefixed host provider such as DOTNET_ is
        // not the application override provider and must not be mistaken for it.
        var overrideIndex = -1;
        for (var index = 0; index < configuration.Sources.Count; index++)
        {
            if (configuration.Sources[index] is EnvironmentVariablesConfigurationSource environmentSource &&
                string.IsNullOrEmpty(environmentSource.Prefix))
            {
                overrideIndex = index;
                break;
            }
        }

        // If a host supplies no unprefixed environment provider, preserve command-line
        // precedence by inserting before that provider rather than appending at the end.
        if (overrideIndex < 0)
        {
            for (var index = 0; index < configuration.Sources.Count; index++)
            {
                if (configuration.Sources[index] is CommandLineConfigurationSource)
                {
                    overrideIndex = index;
                    break;
                }
            }
        }

        if (overrideIndex < 0)
            overrideIndex = configuration.Sources.Count;

        configuration.Sources.Insert(overrideIndex, source);
        services.AddSingleton<PhysicalFileProvider>(_ => fileProvider);
    }
}
