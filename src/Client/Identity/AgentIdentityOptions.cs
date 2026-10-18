namespace GameNet.Agent.Identity;

public sealed class AgentIdentityOptions
{
    public const string SectionName = "GameNet:AgentIdentity";

    /// <summary>
    /// Optional preassigned identity. Set this before first start when an operator
    /// issues a one-time enrollment token for a known DeviceId. Once identity.json
    /// exists, that persisted identity remains authoritative.
    /// </summary>
    public string DeviceId { get; init; } = string.Empty;

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
