namespace GameNet.Shared.Runtime;

public static class GameNetRuntimePaths
{
    public const string DesktopConfigurationFileName = "desktop.json";
    public const string AgentConfigurationFileName = "agent.json";
    public const string ServerConfigurationFileName = "server.json";

    public static string ProgramDataRoot =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "GameNet Manager");

    public static string ConfigurationDirectory =>
        Path.Combine(ProgramDataRoot, "Config");

    public static string DesktopConfigurationPath =>
        Path.Combine(ConfigurationDirectory, DesktopConfigurationFileName);

    public static string AgentConfigurationPath =>
        Path.Combine(ConfigurationDirectory, AgentConfigurationFileName);

    public static string ServerConfigurationPath =>
        Path.Combine(ConfigurationDirectory, ServerConfigurationFileName);

    public static string AgentStateDirectory =>
        Path.Combine(ProgramDataRoot, "Agent");
}
