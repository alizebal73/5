using GameNet.Agent.Transport;
using GameNet.Shared.Runtime;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Xunit;

namespace GameNet.Agent.Tests;

public sealed class AgentRuntimeConfigurationTests
{
    [Fact]
    public void Agent_json_is_loaded_and_explicit_environment_and_command_line_overrides_remain_later()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "gamenet-agent-config-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        var names = new[]
        {
            GameNetRuntimePaths.TestConfigurationDirectoryEnvironmentVariableName,
            "DOTNET_ENVIRONMENT",
            "ASPNETCORE_ENVIRONMENT",
            "GameNet__AgentTransport__ServerBaseUrl"
        };
        var previous = names.ToDictionary(
            name => name,
            Environment.GetEnvironmentVariable,
            StringComparer.OrdinalIgnoreCase);

        try
        {
            Environment.SetEnvironmentVariable(
                GameNetRuntimePaths.TestConfigurationDirectoryEnvironmentVariableName,
                directory);
            Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", "Development");
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");
            Environment.SetEnvironmentVariable("GameNet__AgentTransport__ServerBaseUrl", null);

            File.WriteAllText(
                Path.Combine(directory, GameNetRuntimePaths.AgentConfigurationFileName),
                "{\"GameNet\":{\"AgentTransport\":{\"ServerBaseUrl\":\"http://127.0.0.1:5095\"}}}");

            // The prefixed host provider and normal defaults precede agent.json. The
            // actual application environment provider and command-line provider follow it.
            var fromFile = ResolveEndpoint(Array.Empty<string>());
            Assert.Equal("http://127.0.0.1:5095", fromFile);
            Assert.True(ServerEndpointPolicy.IsValidBaseUrl(fromFile));
            Assert.False(ServerEndpointPolicy.IsValidBaseUrl("http://192.168.0.9:5095"));

            Environment.SetEnvironmentVariable(
                "GameNet__AgentTransport__ServerBaseUrl",
                "https://environment.example:5080");
            Assert.Equal(
                "https://environment.example:5080",
                ResolveEndpoint(Array.Empty<string>()));

            Environment.SetEnvironmentVariable("GameNet__AgentTransport__ServerBaseUrl", null);
            Assert.Equal(
                "https://command-line.example:5080",
                ResolveEndpoint(new[]
                {
                    "--GameNet:AgentTransport:ServerBaseUrl=https://command-line.example:5080"
                }));
        }
        finally
        {
            foreach (var name in names)
                Environment.SetEnvironmentVariable(name, previous[name]);

            try { Directory.Delete(directory, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static string ResolveEndpoint(string[] arguments)
    {
        using var configuration = new ConfigurationManager();
        configuration.AddEnvironmentVariables("DOTNET_");
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["GameNet:AgentTransport:ServerBaseUrl"] = "http://192.168.0.9:5095"
        });
        configuration.AddEnvironmentVariables();
        configuration.AddCommandLine(arguments);

        var services = new ServiceCollection();
        AgentRuntimeConfiguration.AddProgramDataConfiguration(
            configuration,
            services,
            GameNetRuntimePaths.AgentConfigurationFileName);

        using var serviceProvider = services.BuildServiceProvider();
        _ = serviceProvider.GetRequiredService<PhysicalFileProvider>();
        return configuration["GameNet:AgentTransport:ServerBaseUrl"] ?? string.Empty;
    }
}
