namespace GameNet.Shared.Runtime;

public static class GameNetRuntimePaths
{
    public const string DesktopConfigurationFileName = "desktop.json";
    public const string AgentConfigurationFileName = "agent.json";
    public const string ServerConfigurationFileName = "server.json";
    public const string TestConfigurationDirectoryEnvironmentVariableName = "GAMENET_TEST_RUNTIME_CONFIG_DIRECTORY";

    public static string ProgramDataRoot =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "GameNet Manager");

    public static string ConfigurationDirectory =>
        ResolveConfigurationDirectory(
            Environment.GetEnvironmentVariable(TestConfigurationDirectoryEnvironmentVariableName),
            Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"),
            Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT"));

    public static string ResolveConfigurationDirectory(
        string? testConfigurationDirectory,
        string? aspNetCoreEnvironment,
        string? dotNetEnvironment)
    {
        if (string.IsNullOrWhiteSpace(testConfigurationDirectory))
            return Path.Combine(ProgramDataRoot, "Config");

        var isDevelopment =
            string.Equals(aspNetCoreEnvironment, "Development", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(dotNetEnvironment, "Development", StringComparison.OrdinalIgnoreCase);

        var explicitlyProduction =
            string.Equals(aspNetCoreEnvironment, "Production", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(dotNetEnvironment, "Production", StringComparison.OrdinalIgnoreCase);

        if (!isDevelopment || explicitlyProduction)
        {
            throw new InvalidOperationException(
                $"{TestConfigurationDirectoryEnvironmentVariableName} is test-only and is disabled outside Development.");
        }

        if (!Path.IsPathFullyQualified(testConfigurationDirectory))
        {
            throw new InvalidOperationException(
                $"{TestConfigurationDirectoryEnvironmentVariableName} must be an absolute path.");
        }

        return Path.GetFullPath(testConfigurationDirectory);
    }

    public static string DesktopConfigurationPath =>
        Path.Combine(ConfigurationDirectory, DesktopConfigurationFileName);

    public static string AgentConfigurationPath =>
        Path.Combine(ConfigurationDirectory, AgentConfigurationFileName);

    public static string ServerConfigurationPath =>
        Path.Combine(ConfigurationDirectory, ServerConfigurationFileName);

    public static string AgentStateDirectory =>
        Path.Combine(ProgramDataRoot, "Agent");
}
