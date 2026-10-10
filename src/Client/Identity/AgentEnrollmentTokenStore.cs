using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using GameNet.Agent.Transport;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace GameNet.Agent.Identity;

public interface IAgentEnrollmentTokenStore
{
    Task<AgentEnrollmentTokenPayload?> TryLoadAsync(
        string deviceId,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(CancellationToken cancellationToken = default);
}

public sealed record AgentEnrollmentTokenPayload(
    int FormatVersion,
    string DeviceId,
    string Token,
    DateTimeOffset ExpiresAtUtc);

/// <summary>
/// Loads the short-lived first-start token from a DPAPI LocalMachine file.
/// Production additionally requires the canonical ProgramData path and exact restricted DACLs.
/// </summary>
public sealed class AgentEnrollmentTokenStore(
    IOptions<AgentIdentityOptions> identityOptions,
    TimeProvider timeProvider,
    IHostEnvironment hostEnvironment) : IAgentEnrollmentTokenStore
{
    public const string FileName = "enrollment-token.dpapi";
    private const string EntropyPrefix = "GameNet.AgentEnrollmentToken.v1|";
    private const int MaximumProtectedFileSize = 8192;

    public async Task<AgentEnrollmentTokenPayload?> TryLoadAsync(
        string deviceId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);
        if (!OperatingSystem.IsWindows())
            throw new InvalidOperationException("Protected Agent enrollment tokens require Windows DPAPI.");

        var root = identityOptions.Value.ResolveRootPath();
        var path = Path.Combine(root, FileName);
        ValidateProductionStateAccess(root, path);

        if (!Directory.Exists(root))
            return null;
        RejectReparsePoint(root, expectedDirectory: true);
        if (!File.Exists(path))
            return null;
        RejectReparsePoint(path, expectedDirectory: false);
        if (hostEnvironment.IsProduction())
            ValidateExactAcl(path, isDirectory: false, ExpectedTokenFileRights());

        var info = new FileInfo(path);
        if (info.Length is <= 0 or > MaximumProtectedFileSize)
            throw new InvalidOperationException("Protected Agent enrollment token file size is invalid.");

        byte[]? protectedBytes = null;
        byte[]? clearBytes = null;
        byte[]? entropy = null;
        try
        {
            protectedBytes = await File.ReadAllBytesAsync(path, cancellationToken);
            entropy = CreateEntropy(deviceId);
            clearBytes = ProtectedData.Unprotect(
                protectedBytes, entropy, DataProtectionScope.LocalMachine);

            var payload = JsonSerializer.Deserialize<AgentEnrollmentTokenPayload>(
                clearBytes, new JsonSerializerOptions(JsonSerializerDefaults.Web)
                {
                    PropertyNameCaseInsensitive = true
                });
            if (payload is null ||
                payload.FormatVersion != 1 ||
                !string.Equals(payload.DeviceId, deviceId, StringComparison.Ordinal) ||
                !IsEnrollmentToken(payload.Token))
            {
                throw new InvalidOperationException(
                    "Protected Agent enrollment token does not match the configured DeviceId or schema.");
            }

            if (payload.ExpiresAtUtc <= timeProvider.GetUtcNow())
                throw new InvalidOperationException(
                    "Protected Agent enrollment token has expired; issue a fresh token through the reviewed recovery procedure.");

            return payload;
        }
        catch (CryptographicException exception)
        {
            throw new InvalidOperationException(
                "Protected Agent enrollment token could not be decrypted for this machine and DeviceId.", exception);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                "Protected Agent enrollment token file is invalid JSON.", exception);
        }
        finally
        {
            if (protectedBytes is not null) CryptographicOperations.ZeroMemory(protectedBytes);
            if (clearBytes is not null) CryptographicOperations.ZeroMemory(clearBytes);
            if (entropy is not null) CryptographicOperations.ZeroMemory(entropy);
        }
    }

    public Task DeleteAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var root = identityOptions.Value.ResolveRootPath();
        var path = Path.Combine(root, FileName);
        ValidateProductionStateAccess(root, path);
        if (!Directory.Exists(root) || !File.Exists(path))
            return Task.CompletedTask;

        RejectReparsePoint(root, expectedDirectory: true);
        RejectReparsePoint(path, expectedDirectory: false);
        if (hostEnvironment.IsProduction())
            ValidateExactAcl(path, isDirectory: false, ExpectedTokenFileRights());
        File.Delete(path);
        return Task.CompletedTask;
    }

    internal static byte[] CreateEntropy(string deviceId) =>
        Encoding.UTF8.GetBytes(EntropyPrefix + deviceId);

    private void ValidateProductionStateAccess(string root, string tokenPath)
    {
        if (!hostEnvironment.IsProduction())
            return;
        if (!OperatingSystem.IsWindows())
            throw new InvalidOperationException("Production Agent token storage is supported only on Windows.");

        var common = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        var managerRoot = Path.GetFullPath(Path.Combine(common, "GameNet Manager"));
        var configDirectory = Path.Combine(managerRoot, "Config");
        var canonicalRoot = Path.Combine(managerRoot, "Agent");
        if (!string.Equals(Path.GetFullPath(root), Path.GetFullPath(canonicalRoot), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Production Agent token storage must use the canonical ProgramData Agent directory.");

        foreach (var path in new[] { managerRoot, configDirectory, canonicalRoot })
        {
            if (!Directory.Exists(path))
                throw new DirectoryNotFoundException("The protected Agent state directory is missing; run the reviewed Agent state-provisioning helper first.");
            RejectReparsePoint(path, expectedDirectory: true);
        }

        var agentServiceSid = (SecurityIdentifier)new NTAccount("NT SERVICE", "GameNet 5 Agent")
            .Translate(typeof(SecurityIdentifier));
        ValidateExactAcl(managerRoot, isDirectory: true, ExpectedParentDirectoryRights(agentServiceSid.Value));
        ValidateExactAcl(configDirectory, isDirectory: true, ExpectedParentDirectoryRights(agentServiceSid.Value));
        ValidateExactAcl(canonicalRoot, isDirectory: true, ExpectedAgentStateDirectoryRights(agentServiceSid.Value));
    }

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
            throw new InvalidOperationException("Protected Agent token ACL inheritance must be disabled.");

        var rules = acl.GetAccessRules(true, false, typeof(SecurityIdentifier))
            .Cast<FileSystemAccessRule>().ToArray();
        if (rules.Length != expectedRights.Count ||
            rules.Any(rule =>
                rule.AccessControlType != AccessControlType.Allow ||
                rule.IdentityReference is not SecurityIdentifier sid ||
                !expectedRights.TryGetValue(sid.Value, out var rights) ||
                rule.FileSystemRights != rights))
        {
            throw new InvalidOperationException("Protected Agent token ACL contains an unapproved principal or permission.");
        }
        foreach (var sid in expectedRights.Keys)
        {
            if (rules.Count(rule => rule.IdentityReference.Value == sid) != 1)
                throw new InvalidOperationException("Protected Agent token ACL is missing a required principal.");
        }
    }

    [SupportedOSPlatform("windows")]
    private static IReadOnlyDictionary<string, FileSystemRights> ExpectedParentDirectoryRights(string agentSid) =>
        new Dictionary<string, FileSystemRights>(StringComparer.Ordinal)
        {
            ["S-1-5-18"] = FileSystemRights.FullControl,
            ["S-1-5-32-544"] = FileSystemRights.FullControl,
            ["S-1-5-32-545"] = FileSystemRights.ReadAndExecute,
            [agentSid] = FileSystemRights.ReadAndExecute
        };

    [SupportedOSPlatform("windows")]
    private static IReadOnlyDictionary<string, FileSystemRights> ExpectedAgentStateDirectoryRights(string agentSid) =>
        new Dictionary<string, FileSystemRights>(StringComparer.Ordinal)
        {
            ["S-1-5-18"] = FileSystemRights.FullControl,
            ["S-1-5-32-544"] = FileSystemRights.FullControl,
            [agentSid] = FileSystemRights.Modify
        };

    [SupportedOSPlatform("windows")]
    private static IReadOnlyDictionary<string, FileSystemRights> ExpectedTokenFileRights() =>
        new Dictionary<string, FileSystemRights>(StringComparer.Ordinal)
        {
            ["S-1-5-18"] = FileSystemRights.FullControl,
            ["S-1-5-32-544"] = FileSystemRights.FullControl,
            [((SecurityIdentifier)new NTAccount("NT SERVICE", "GameNet 5 Agent")
                .Translate(typeof(SecurityIdentifier))).Value] = FileSystemRights.Read
        };

    private static void RejectReparsePoint(string path, bool expectedDirectory)
    {
        var attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.ReparsePoint) != 0 ||
            expectedDirectory != ((attributes & FileAttributes.Directory) != 0))
        {
            throw new InvalidOperationException("Protected Agent enrollment token path contains an invalid filesystem object.");
        }
    }

    private static bool IsEnrollmentToken(string? token) =>
        !string.IsNullOrWhiteSpace(token) &&
        token.Length == 43 &&
        token.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_');
}
