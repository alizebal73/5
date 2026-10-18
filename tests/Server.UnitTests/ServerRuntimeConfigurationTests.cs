using GameNet.Server.Infrastructure.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration.EnvironmentVariables;
using Microsoft.Extensions.Configuration.Memory;
using Xunit;

namespace GameNet.Server.UnitTests;

public sealed class ServerRuntimeConfigurationTests
{
    private static readonly object EnvironmentLock = new();

    [Fact]
    public void ProgramData_server_json_overrides_prefixed_host_provider_and_later_defaults()
    {
        AssertRuntimeMarker(
            environmentValue: null,
            commandLineValue: null,
            expectedValue: "programdata");
    }

    [Fact]
    public void Unprefixed_application_environment_overrides_ProgramData_server_json()
    {
        AssertRuntimeMarker(
            environmentValue: "application-environment",
            commandLineValue: null,
            expectedValue: "application-environment");
    }

    [Fact]
    public void Command_line_overrides_ProgramData_server_json_and_environment()
    {
        AssertRuntimeMarker(
            environmentValue: "application-environment",
            commandLineValue: "command-line",
            expectedValue: "command-line");
    }

    private static void AssertRuntimeMarker(
        string? environmentValue,
        string? commandLineValue,
        string expectedValue)
    {
        lock (EnvironmentLock)
        {
            var names = new[]
            {
                "GAMENET_TEST_RUNTIME_CONFIG_DIRECTORY",
                "ASPNETCORE_ENVIRONMENT",
                "DOTNET_ENVIRONMENT",
                "ASPNETCORE_URLS",
                "DOTNET_URLS",
                "DOTNET_GameNet__RuntimeMarker",
                "GameNet__RuntimeMarker"
            };

            var original = names.ToDictionary(
                name => name,
                name => Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.Process));

            var directory = Path.Combine(
                Path.GetTempPath(),
                "gamenet-server-runtime-config-" + Guid.NewGuid().ToString("N"));

            try
            {
                Directory.CreateDirectory(directory);
                File.WriteAllText(
                    Path.Combine(directory, "server.json"),
                    "{\"urls\":\"http://127.0.0.1:0\",\"GameNet\":{\"RuntimeMarker\":\"programdata\"}}");

                Environment.SetEnvironmentVariable(
                    "GAMENET_TEST_RUNTIME_CONFIG_DIRECTORY",
                    directory,
                    EnvironmentVariableTarget.Process);
                Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development", EnvironmentVariableTarget.Process);
                Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", "Development", EnvironmentVariableTarget.Process);
                Environment.SetEnvironmentVariable("ASPNETCORE_URLS", null, EnvironmentVariableTarget.Process);
                Environment.SetEnvironmentVariable("DOTNET_URLS", null, EnvironmentVariableTarget.Process);
                Environment.SetEnvironmentVariable("DOTNET_GameNet__RuntimeMarker", "prefixed-host-value", EnvironmentVariableTarget.Process);
                Environment.SetEnvironmentVariable("GameNet__RuntimeMarker", environmentValue, EnvironmentVariableTarget.Process);

                var args = commandLineValue is null
                    ? Array.Empty<string>()
                    : new[] { "--GameNet:RuntimeMarker=" + commandLineValue };

                var builder = WebApplication.CreateBuilder(new WebApplicationOptions
                {
                    Args = args,
                    EnvironmentName = "Development",
                    ContentRootPath = directory
                });

                // Reproduce the host/application provider ordering that the Server loader
                // must handle: a prefixed environment provider occurs before a later
                // default source. The installed JSON belongs after defaults, but before
                // unprefixed application environment values and command-line arguments.
                builder.Configuration.Sources.Insert(
                    0,
                    new EnvironmentVariablesConfigurationSource { Prefix = "DOTNET_" });
                builder.Configuration.Sources.Insert(
                    1,
                    new MemoryConfigurationSource
                    {
                        InitialData = new Dictionary<string, string?>
                        {
                            ["GameNet:RuntimeMarker"] = "default-source"
                        }
                    });

                ServerTlsHostConfiguration.Configure(builder);
                using var app = builder.Build();

                Assert.Equal(expectedValue, app.Configuration["GameNet:RuntimeMarker"]);
            }
            finally
            {
                foreach (var item in original)
                {
                    Environment.SetEnvironmentVariable(
                        item.Key,
                        item.Value,
                        EnvironmentVariableTarget.Process);
                }

                if (Directory.Exists(directory))
                    Directory.Delete(directory, recursive: true);
            }
        }
    }
}
