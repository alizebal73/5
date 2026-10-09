namespace GameNet.Agent.Identity;

public sealed class AgentIdentityOptions
{
    public const string SectionName = "GameNet:AgentIdentity";

    public string RootPath { get; init; } =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "GameNet Manager",
            "Agent");

    public string ResolveRootPath()
    {
        if (string.IsNullOrWhiteSpace(RootPath))
            throw new InvalidOperationException("Agent RootPath must be configured.");

        var expanded = Environment.ExpandEnvironmentVariables(RootPath);
        if (!Path.IsPathFullyQualified(expanded))
            throw new InvalidOperationException("Agent RootPath must resolve to an absolute path.");

        return Path.GetFullPath(expanded);
    }
}
