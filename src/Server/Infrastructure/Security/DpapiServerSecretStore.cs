using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GameNet.Server.Infrastructure.Security;

[SupportedOSPlatform("windows")]
internal static class DpapiServerSecretStore
{
    private const int SchemaVersion = 1;
    private const int MaximumProtectedPayloadBytes = 65_536;
    private const string ServiceName = "GameNet 5 Server";
    private static readonly byte[] Entropy = "GameNet5.ServerSecrets.v1"u8.ToArray();

    internal static ServerSecretMaterial LoadDefault() => LoadFromPath(GetDefaultPath());

    internal static ServerSecretMaterial LoadFromPath(string path)
    {
        if (!OperatingSystem.IsWindows())
            throw new ServerSecretStoreException("The protected Server secret store requires Windows DPAPI.");

        byte[]? protectedBytes = null;
        try
        {
            var fullPath = Path.GetFullPath(path);
            var directory = Path.GetDirectoryName(fullPath);
            if (string.IsNullOrWhiteSpace(directory) ||
                !Directory.Exists(directory) ||
                !File.Exists(fullPath))
                throw new ServerSecretStoreException("The protected Server secret store is unavailable.");

            RejectReparsePoint(directory, isDirectory: true);
            RejectReparsePoint(fullPath, isDirectory: false);
            var serviceSid = ResolveServerServiceSid();
            ValidateAccessControl(directory, isDirectory: true, serviceSid);
            ValidateAccessControl(fullPath, isDirectory: false, serviceSid);

            var info = new FileInfo(fullPath);
            if (info.Length is <= 0 or > MaximumProtectedPayloadBytes)
                throw new ServerSecretStoreException("The protected Server secret store is invalid.");

            protectedBytes = File.ReadAllBytes(fullPath);
            return UnprotectPayload(protectedBytes);
        }
        catch (ServerSecretStoreException)
        {
            throw;
        }
        catch
        {
            throw new ServerSecretStoreException(
                "The protected Server secret store could not be read or decrypted. Administrative re-provisioning is required.");
        }
        finally
        {
            if (protectedBytes is not null) CryptographicOperations.ZeroMemory(protectedBytes);
        }
    }

    internal static byte[] ProtectPayload(ServerSecretMaterial secrets)
    {
        byte[]? plaintext = null;
        try
        {
            plaintext = JsonSerializer.SerializeToUtf8Bytes(new ServerSecretPayload
            {
                SchemaVersion = SchemaVersion,
                GenerationId = Guid.NewGuid().ToString("N"),
                DatabaseConnectionString = secrets.ConnectionString,
                JwtSigningKeyBase64 = secrets.SigningKey,
                AgentProvisioningKeyBase64 = secrets.ProvisioningKey
            });
            return ProtectedData.Protect(plaintext, Entropy, DataProtectionScope.LocalMachine);
        }
        catch (ServerSecretStoreException)
        {
            throw;
        }
        catch
        {
            throw new ServerSecretStoreException("The Server secret payload could not be protected.");
        }
        finally
        {
            if (plaintext is not null) CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    internal static ServerSecretMaterial UnprotectPayload(byte[] protectedBytes)
    {
        byte[]? plaintext = null;
        try
        {
            plaintext = ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.LocalMachine);
            return ParsePayload(plaintext);
        }
        catch (ServerSecretStoreException)
        {
            throw;
        }
        catch
        {
            throw new ServerSecretStoreException("The protected Server secret payload could not be decrypted.");
        }
        finally
        {
            if (plaintext is not null) CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    internal static ServerSecretMaterial ParsePayload(ReadOnlySpan<byte> plaintext)
    {
        try
        {
            var payload = JsonSerializer.Deserialize<ServerSecretPayload>(plaintext);
            if (payload is null ||
                payload.SchemaVersion != SchemaVersion ||
                !Guid.TryParseExact(payload.GenerationId, "N", out _))
                throw new ServerSecretStoreException("The protected Server secret store format is unsupported or invalid.");

            return ServerSecretMaterial.Create(
                payload.DatabaseConnectionString,
                payload.JwtSigningKeyBase64,
                payload.AgentProvisioningKeyBase64);
        }
        catch (ServerSecretStoreException)
        {
            throw;
        }
        catch
        {
            throw new ServerSecretStoreException("The protected Server secret store format is unsupported or invalid.");
        }
    }

    internal static void Provision(string connectionString, string jwtSigningKeyBase64, string agentProvisioningKeyBase64)
    {
        if (!OperatingSystem.IsWindows())
            throw new ServerSecretStoreException("The protected Server secret store requires Windows DPAPI.");
        EnsureElevatedAdministrator();

        var secrets = ServerSecretMaterial.Create(connectionString, jwtSigningKeyBase64, agentProvisioningKeyBase64);
        var directory = GetDefaultDirectory();
        var path = GetDefaultPath();
        if (File.Exists(path) || Directory.Exists(directory))
            throw new ServerSecretStoreException(
                "The Server secret-store path already exists. Inspect existing state before provisioning; overwrite is disabled.");

        var parent = Path.GetDirectoryName(directory)
            ?? throw new ServerSecretStoreException("The Windows ProgramData directory could not be resolved.");
        Directory.CreateDirectory(parent);
        RejectReparsePoint(parent, isDirectory: true);

        var serviceSid = ResolveServerServiceSid();
        var directorySecurity = CreateDirectorySecurity(serviceSid);
        var fileSecurity = CreateFileSecurity(serviceSid);
        var tempPath = Path.Combine(directory, ".server-secrets-" + Guid.NewGuid().ToString("N") + ".tmp");
        byte[]? plaintext = null;
        byte[]? protectedBytes = null;

        try
        {
            new DirectoryInfo(directory).Create(directorySecurity);
            ValidateAccessControl(directory, isDirectory: true, serviceSid);

            protectedBytes = ProtectPayload(secrets);

            // Create the temporary file with its final DACL from the first filesystem operation.
            using (var stream = System.IO.FileSystemAclExtensions.Create(
                new FileInfo(tempPath),
                FileMode.CreateNew,
                FileSystemRights.ReadAndExecute | FileSystemRights.WriteData,
                FileShare.None,
                bufferSize: 4096,
                FileOptions.WriteThrough,
                fileSecurity))
            {
                stream.Write(protectedBytes);
                stream.Flush(flushToDisk: true);
            }

            RejectReparsePoint(tempPath, isDirectory: false);
            ValidateAccessControl(tempPath, isDirectory: false, serviceSid);
            File.Move(tempPath, path);
            ValidateAccessControl(path, isDirectory: false, serviceSid);
        }
        catch (ServerSecretStoreException)
        {
            throw;
        }
        catch
        {
            throw new ServerSecretStoreException(
                "Server secret-store provisioning failed. Inspect the target directory before retrying.");
        }
        finally
        {
            if (protectedBytes is not null) CryptographicOperations.ZeroMemory(protectedBytes);
            try
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }
            catch
            {
                // Cleanup failure is reported by the provisioning host; no plaintext temp file is created.
            }
        }
    }

    internal static string GetDefaultDirectory()
    {
        var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        if (string.IsNullOrWhiteSpace(programData))
            throw new ServerSecretStoreException("The Windows ProgramData directory could not be resolved.");
        return Path.Combine(programData, "GameNet Manager", "Secrets");
    }

    internal static string GetDefaultPath() => Path.Combine(GetDefaultDirectory(), "server-secrets.v1.dpapi");

    private static void EnsureElevatedAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        if (identity.User?.Equals(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null)) == true)
            return;
        if (!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
            throw new ServerSecretStoreException("Server secret-store provisioning requires an elevated local administrator.");
    }

    private static SecurityIdentifier ResolveServerServiceSid()
    {
        try
        {
            return (SecurityIdentifier)new NTAccount("NT SERVICE", ServiceName).Translate(typeof(SecurityIdentifier));
        }
        catch
        {
            throw new ServerSecretStoreException(
                "The GameNet Server service SID is not resolvable. Register the service before secret provisioning.");
        }
    }

    private static void RejectReparsePoint(string path, bool isDirectory)
    {
        var attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.ReparsePoint) != 0 ||
            (isDirectory && (attributes & FileAttributes.Directory) == 0) ||
            (!isDirectory && (attributes & FileAttributes.Directory) != 0))
            throw new ServerSecretStoreException("The protected Server secret-store path is invalid.");
    }

    private static void ValidateAccessControl(string path, bool isDirectory, SecurityIdentifier serviceSid)
    {
        FileSystemSecurity security = isDirectory
            ? new DirectoryInfo(path).GetAccessControl(AccessControlSections.Access)
            : new FileInfo(path).GetAccessControl(AccessControlSections.Access);

        if (!security.AreAccessRulesProtected)
            throw new ServerSecretStoreException("The protected Server secret-store ACL is not restrictive.");

        var rules = security.GetAccessRules(true, false, typeof(SecurityIdentifier))
            .Cast<FileSystemAccessRule>()
            .ToArray();
        var systemSid = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        var administratorsSid = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        var approved = new HashSet<string>(StringComparer.Ordinal)
        {
            systemSid.Value, administratorsSid.Value, serviceSid.Value
        };

        if (rules.Length == 0 || rules.Any(rule =>
                rule.AccessControlType != AccessControlType.Allow ||
                rule.IdentityReference is not SecurityIdentifier sid ||
                !approved.Contains(sid.Value)))
            throw new ServerSecretStoreException("The protected Server secret-store ACL grants access to an unapproved principal.");

        var systemRights = CombineRights(rules, systemSid);
        var adminRights = CombineRights(rules, administratorsSid);
        var serviceRights = CombineRights(rules, serviceSid);
        if ((systemRights & FileSystemRights.FullControl) != FileSystemRights.FullControl ||
            (adminRights & FileSystemRights.FullControl) != FileSystemRights.FullControl ||
            (serviceRights & FileSystemRights.ReadAndExecute) != FileSystemRights.ReadAndExecute)
            throw new ServerSecretStoreException("The protected Server secret-store ACL is missing a required access grant.");

        const FileSystemRights writeRights =
            FileSystemRights.WriteData | FileSystemRights.AppendData | FileSystemRights.WriteAttributes |
            FileSystemRights.WriteExtendedAttributes | FileSystemRights.Delete | FileSystemRights.ChangePermissions |
            FileSystemRights.TakeOwnership | FileSystemRights.CreateFiles | FileSystemRights.CreateDirectories |
            FileSystemRights.DeleteSubdirectoriesAndFiles;
        if ((serviceRights & writeRights) != 0)
            throw new ServerSecretStoreException("The GameNet Server service identity has excessive secret-store permissions.");
    }

    private static FileSystemRights CombineRights(IEnumerable<FileSystemAccessRule> rules, SecurityIdentifier sid) =>
        rules.Where(rule => rule.IdentityReference.Equals(sid))
            .Aggregate((FileSystemRights)0, (rights, rule) => rights | rule.FileSystemRights);

    private static DirectorySecurity CreateDirectorySecurity(SecurityIdentifier serviceSid)
    {
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(true, false);
        var inheritable = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
        security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            FileSystemRights.FullControl, inheritable, PropagationFlags.None, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            FileSystemRights.FullControl, inheritable, PropagationFlags.None, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(serviceSid, FileSystemRights.ReadAndExecute,
            InheritanceFlags.None, PropagationFlags.None, AccessControlType.Allow));
        return security;
    }

    private static FileSecurity CreateFileSecurity(SecurityIdentifier serviceSid)
    {
        var security = new FileSecurity();
        security.SetAccessRuleProtection(true, false);
        security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            FileSystemRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            FileSystemRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(serviceSid, FileSystemRights.ReadAndExecute, AccessControlType.Allow));
        return security;
    }

    private sealed class ServerSecretPayload
    {
        [JsonPropertyName("schemaVersion")] public int SchemaVersion { get; init; }
        [JsonPropertyName("generationId")] public string? GenerationId { get; init; }
        [JsonPropertyName("databaseConnectionString")] public string? DatabaseConnectionString { get; init; }
        [JsonPropertyName("jwtSigningKeyBase64")] public string? JwtSigningKeyBase64 { get; init; }
        [JsonPropertyName("agentProvisioningKeyBase64")] public string? AgentProvisioningKeyBase64 { get; init; }
    }
}
