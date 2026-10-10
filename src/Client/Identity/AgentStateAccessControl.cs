using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

namespace GameNet.Agent.Identity;

/// <summary>
/// Validates the filesystem trust boundary shared by Agent identity, credential,
/// and one-time enrollment-token state in a Production Windows service.
/// </summary>
internal static class AgentStateAccessControl
{
    private const string ServiceName = "GameNet 5 Agent";

    [SupportedOSPlatform("windows")]
    public static void ValidateProductionStateRoot(string configuredRoot)
    {
        if (!OperatingSystem.IsWindows())
            throw new InvalidOperationException("Production Agent state requires Windows ACLs.");

        var common = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        var managerRoot = Path.GetFullPath(Path.Combine(common, "GameNet Manager"));
        var configDirectory = Path.Combine(managerRoot, "Config");
        var canonicalAgentRoot = Path.Combine(managerRoot, "Agent");
        if (!string.Equals(Path.GetFullPath(configuredRoot), Path.GetFullPath(canonicalAgentRoot), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Production Agent state must use the canonical ProgramData Agent directory.");

        foreach (var directory in new[] { managerRoot, configDirectory, canonicalAgentRoot })
        {
            if (!Directory.Exists(directory))
                throw new DirectoryNotFoundException("A protected Agent state directory is missing; run the reviewed service-state provisioning helper first.");
            RejectReparsePoint(directory, expectedDirectory: true);
        }

        var agentSid = GetAgentServiceSid();
        ValidateExactAcl(managerRoot, isDirectory: true, ParentDirectoryRights(agentSid.Value));
        ValidateExactAcl(configDirectory, isDirectory: true, ParentDirectoryRights(agentSid.Value));
        ValidateExactAcl(canonicalAgentRoot, isDirectory: true, AgentStateDirectoryRights(agentSid.Value));
    }

    [SupportedOSPlatform("windows")]
    public static void ValidateEnrollmentTokenFile(string configuredRoot, string tokenPath)
    {
        ValidateProductionStateRoot(configuredRoot);
        var expectedPath = Path.GetFullPath(Path.Combine(configuredRoot, AgentEnrollmentTokenStore.FileName));
        if (!string.Equals(Path.GetFullPath(tokenPath), expectedPath, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Production enrollment-token storage must use its canonical ProgramData filename.");
        if (!File.Exists(expectedPath))
            return;

        RejectReparsePoint(expectedPath, expectedDirectory: false);
        var agentSid = GetAgentServiceSid();
        ValidateExactAcl(expectedPath, isDirectory: false, EnrollmentTokenFileRights(agentSid.Value));
    }

    [SupportedOSPlatform("windows")]
    private static SecurityIdentifier GetAgentServiceSid() =>
        (SecurityIdentifier)new NTAccount("NT SERVICE", ServiceName).Translate(typeof(SecurityIdentifier));

    [SupportedOSPlatform("windows")]
    private static IReadOnlyDictionary<string, FileSystemRights> ParentDirectoryRights(string agentSid) =>
        new Dictionary<string, FileSystemRights>(StringComparer.Ordinal)
        {
            ["S-1-5-18"] = FileSystemRights.FullControl,
            ["S-1-5-32-544"] = FileSystemRights.FullControl,
            ["S-1-5-32-545"] = FileSystemRights.ReadAndExecute,
            [agentSid] = FileSystemRights.ReadAndExecute
        };

    [SupportedOSPlatform("windows")]
    private static IReadOnlyDictionary<string, FileSystemRights> AgentStateDirectoryRights(string agentSid) =>
        new Dictionary<string, FileSystemRights>(StringComparer.Ordinal)
        {
            ["S-1-5-18"] = FileSystemRights.FullControl,
            ["S-1-5-32-544"] = FileSystemRights.FullControl,
            [agentSid] = FileSystemRights.Modify
        };

    [SupportedOSPlatform("windows")]
    private static IReadOnlyDictionary<string, FileSystemRights> EnrollmentTokenFileRights(string agentSid) =>
        new Dictionary<string, FileSystemRights>(StringComparer.Ordinal)
        {
            ["S-1-5-18"] = FileSystemRights.FullControl,
            ["S-1-5-32-544"] = FileSystemRights.FullControl,
            [agentSid] = FileSystemRights.Read
        };

    [SupportedOSPlatform("windows")]
    private static void ValidateExactAcl(
        string path,
        bool isDirectory,
        IReadOnlyDictionary<string, FileSystemRights> expectedRights)
    {
        FileSystemSecurity acl = isDirectory
            ? new DirectoryInfo(path).GetAccessControl(AccessControlSections.Access)
            : new FileInfo(path).GetAccessControl(AccessControlSections.Access);
        if (!acl.AreAccessRulesProtected)
            throw new InvalidOperationException("Production Agent state ACL inheritance must be disabled.");

        var rules = acl.GetAccessRules(true, false, typeof(SecurityIdentifier))
            .Cast<FileSystemAccessRule>().ToArray();
        if (rules.Length != expectedRights.Count ||
            rules.Any(rule =>
                rule.AccessControlType != AccessControlType.Allow ||
                rule.IdentityReference is not SecurityIdentifier sid ||
                !expectedRights.TryGetValue(sid.Value, out var rights) ||
                rule.FileSystemRights != rights))
        {
            throw new InvalidOperationException("Production Agent state ACL contains an unapproved principal or permission.");
        }

        foreach (var sid in expectedRights.Keys)
        {
            if (rules.Count(rule => rule.IdentityReference.Value == sid) != 1)
                throw new InvalidOperationException("Production Agent state ACL is missing a required principal.");
        }
    }

    private static void RejectReparsePoint(string path, bool expectedDirectory)
    {
        var attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.ReparsePoint) != 0 ||
            expectedDirectory != ((attributes & FileAttributes.Directory) != 0))
        {
            throw new InvalidOperationException("Production Agent state path contains an invalid filesystem object.");
        }
    }
}
