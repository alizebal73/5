namespace GameNet.Agent.Identity;

public sealed class AgentIdentityOptions
{
    public const string SectionName = "GameNet:AgentIdentity";

    public string RootPath { get; init; } =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "GameNet Manager",
            "Agent");
}
