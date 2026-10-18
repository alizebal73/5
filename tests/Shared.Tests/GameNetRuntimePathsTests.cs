using GameNet.Shared.Runtime;
using Xunit;

namespace GameNet.Shared.Tests;

public sealed class GameNetRuntimePathsTests
{
    [Fact]
    public void Default_configuration_directory_is_under_common_application_data()
    {
        var actual = GameNetRuntimePaths.ResolveConfigurationDirectory(
            testConfigurationDirectory: null,
            aspNetCoreEnvironment: "Production",
            dotNetEnvironment: "Production");

        var expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "GameNet Manager",
            "Config");

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Test_configuration_directory_is_resolved_only_in_development()
    {
        var requested = Path.Combine(Path.GetTempPath(), "gamenet-test-config");
        var actual = GameNetRuntimePaths.ResolveConfigurationDirectory(
            requested,
            aspNetCoreEnvironment: "Development",
            dotNetEnvironment: "Development");

        Assert.Equal(Path.GetFullPath(requested), actual);
    }

    [Theory]
    [InlineData("Production", null)]
    [InlineData(null, "Production")]
    [InlineData("Production", "Development")]
    [InlineData(null, null)]
    public void Test_configuration_directory_is_rejected_outside_development(
        string? aspNetCoreEnvironment,
        string? dotNetEnvironment)
    {
        var requested = Path.Combine(Path.GetTempPath(), "gamenet-test-config");

        Assert.Throws<InvalidOperationException>(() =>
            GameNetRuntimePaths.ResolveConfigurationDirectory(
                requested,
                aspNetCoreEnvironment,
                dotNetEnvironment));
    }

    [Fact]
    public void Test_configuration_directory_must_be_absolute()
    {
        Assert.Throws<InvalidOperationException>(() =>
            GameNetRuntimePaths.ResolveConfigurationDirectory(
                "relative-config",
                aspNetCoreEnvironment: "Development",
                dotNetEnvironment: "Development"));
    }
}
