using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GameNet.Server.Infrastructure.Security;

internal enum ServerSecretProvisioningFailureCode
{
    ProvisioningRejected,
    ProvisioningFailed
}

internal sealed record ServerSecretProvisioningAuditRecord(
    [property: JsonPropertyName("schemaVersion")] int SchemaVersion,
    [property: JsonPropertyName("eventType")] string EventType,
    [property: JsonPropertyName("operationId")] string OperationId,
    [property: JsonPropertyName("occurredAtUtc")] DateTimeOffset OccurredAtUtc,
    [property: JsonPropertyName("outcome")] string Outcome,
    [property: JsonPropertyName("actorSid")] string ActorSid,
    [property: JsonPropertyName("actorName")] string ActorName,
    [property: JsonPropertyName("failureCode")] string? FailureCode)
{
    internal string ToJsonLine() => JsonSerializer.Serialize(this, new JsonSerializerOptions(JsonSerializerDefaults.Web)) + "\n";
}

[SupportedOSPlatform("windows")]
internal static class ServerSecretProvisioningAuditLog
{
    private const string EventType = "ServerSecretStore.Provision";
    private const int SchemaVersion = 1;

    internal static void RecordStarted(string operationId) => WriteRecord(operationId, "Started", null);

    internal static void RecordSucceeded(string operationId) => WriteRecord(operationId, "Succeeded", null);

    internal static bool TryRecordFailed(string operationId, ServerSecretProvisioningFailureCode failureCode)
    {
        try
        {
            WriteRecord(operationId, "Failed", failureCode.ToString());
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void WriteRecord(string operationId, string outcome, string? failureCode)
    {
        if (!OperatingSystem.IsWindows())
            throw new ServerSecretStoreException("The restricted provisioning audit log requires Windows.");

        if (!Guid.TryParseExact(operationId, "N", out _))
            throw new ServerSecretStoreException("The provisioning audit operation identifier is invalid.");
        if (outcome is not ("Started" or "Succeeded" or "Failed") ||
            (outcome == "Failed" && failureCode is not ("ProvisioningRejected" or "ProvisioningFailed")) ||
            (outcome != "Failed" && failureCode is not null))
            throw new ServerSecretStoreException("The provisioning audit event type is invalid.");

        try
        {
            var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            if (string.IsNullOrWhiteSpace(programData))
                throw new ServerSecretStoreException("The Windows ProgramData directory could not be resolved.");

            var managerRoot = Path.Combine(programData, "GameNet Manager");
            Directory.CreateDirectory(managerRoot);
            RejectReparsePoint(managerRoot);

            var auditDirectory = Path.Combine(managerRoot, "Audit");
            var systemSid = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
            var administratorsSid = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);

            if (File.Exists(auditDirectory))
                throw new ServerSecretStoreException("The restricted provisioning audit path is invalid.");

            if (!Directory.Exists(auditDirectory))
                new DirectoryInfo(auditDirectory).Create(CreateDirectorySecurity(systemSid, administratorsSid));

            RejectReparsePoint(auditDirectory);
            ValidateAccessControl(auditDirectory, isDirectory: true, systemSid, administratorsSid);

            using var identity = WindowsIdentity.GetCurrent();
            var record = new ServerSecretProvisioningAuditRecord(
                SchemaVersion,
                EventType,
                operationId,
                TimeProvider.System.GetUtcNow(),
                outcome,
                identity.User?.Value ?? "unknown",
                identity.Name ?? "unknown",
                failureCode);

            var path = Path.Combine(
                auditDirectory,
                "server-secret-store-" + operationId + "-" + outcome.ToLowerInvariant() + ".json");
            var bytes = Encoding.UTF8.GetBytes(record.ToJsonLine());
            try
            {
                var security = CreateFileSecurity(systemSid, administratorsSid);
                using var stream = System.IO.FileSystemAclExtensions.Create(
                    new FileInfo(path),
                    FileMode.CreateNew,
                    FileSystemRights.WriteData | FileSystemRights.ReadAttributes,
                    FileShare.Read,
                    bufferSize: 4096,
                    FileOptions.WriteThrough,
                    security);
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(bytes);
            }

            ValidateAccessControl(path, isDirectory: false, systemSid, administratorsSid);
        }
        catch (ServerSecretStoreException)
        {
            throw;
        }
        catch
        {
            throw new ServerSecretStoreException(
                "A restricted provisioning audit record could not be written. No secret values were recorded.");
        }
    }

    private static void RejectReparsePoint(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new ServerSecretStoreException("The restricted provisioning audit path is invalid.");
    }

    private static void ValidateAccessControl(
        string path,
        bool isDirectory,
        SecurityIdentifier systemSid,
        SecurityIdentifier administratorsSid)
    {
        FileSystemSecurity security = isDirectory
            ? new DirectoryInfo(path).GetAccessControl(AccessControlSections.Access)
            : new FileInfo(path).GetAccessControl(AccessControlSections.Access);

        if (!security.AreAccessRulesProtected)
            throw new ServerSecretStoreException("The provisioning audit ACL is not restrictive.");

        var rules = security.GetAccessRules(true, false, typeof(SecurityIdentifier))
            .Cast<FileSystemAccessRule>()
            .ToArray();
        var allowed = new HashSet<string>(StringComparer.Ordinal)
        {
            systemSid.Value,
            administratorsSid.Value
        };

        if (rules.Length == 0 || rules.Any(rule =>
                rule.AccessControlType != AccessControlType.Allow ||
                rule.IdentityReference is not SecurityIdentifier sid ||
                !allowed.Contains(sid.Value)))
            throw new ServerSecretStoreException("The provisioning audit ACL grants access to an unapproved principal.");

        if ((CombineRights(rules, systemSid) & FileSystemRights.FullControl) != FileSystemRights.FullControl ||
            (CombineRights(rules, administratorsSid) & FileSystemRights.FullControl) != FileSystemRights.FullControl)
            throw new ServerSecretStoreException("The provisioning audit ACL is missing a required access grant.");
    }

    private static FileSystemRights CombineRights(IEnumerable<FileSystemAccessRule> rules, SecurityIdentifier sid) =>
        rules.Where(rule => rule.IdentityReference.Equals(sid))
            .Aggregate((FileSystemRights)0, (rights, rule) => rights | rule.FileSystemRights);

    private static DirectorySecurity CreateDirectorySecurity(
        SecurityIdentifier systemSid,
        SecurityIdentifier administratorsSid)
    {
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(true, false);
        var inheritable = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
        security.AddAccessRule(new FileSystemAccessRule(
            systemSid, FileSystemRights.FullControl, inheritable, PropagationFlags.None, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(
            administratorsSid, FileSystemRights.FullControl, inheritable, PropagationFlags.None, AccessControlType.Allow));
        return security;
    }

    private static FileSecurity CreateFileSecurity(
        SecurityIdentifier systemSid,
        SecurityIdentifier administratorsSid)
    {
        var security = new FileSecurity();
        security.SetAccessRuleProtection(true, false);
        security.AddAccessRule(new FileSystemAccessRule(
            systemSid, FileSystemRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(
            administratorsSid, FileSystemRights.FullControl, AccessControlType.Allow));
        return security;
    }
}
