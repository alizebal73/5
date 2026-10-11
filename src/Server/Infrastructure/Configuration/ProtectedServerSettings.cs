using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Runtime.Versioning;
using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace GameNet.Server.Infrastructure.Configuration;

public static class ProtectedServerSettings
{
    public const string PathEnvironmentVariableName = "GAMENET_PROTECTED_SETTINGS_FILE";
    public const string DefaultFileName = "server-secrets.bin";

    private static readonly HashSet<string> RuntimeExcludedSecretKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "GameNet:DatabaseConnectionString",
        "GameNet:Authentication:SigningKey",
        "GameNet:Agent:ProvisioningKey"
    };

    private static readonly HashSet<string> AllowedKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "GameNet:DatabaseConnectionString",
        "GameNet:Authentication:Enabled",
        "GameNet:Authentication:Issuer",
        "GameNet:Authentication:Audience",
        "GameNet:Authentication:SigningKey",
        "GameNet:Agent:ProvisioningKey",
        "GameNet:Setup:BootstrapSecret",
        "GameNet:ServerTls:CertificateThumbprint"
    };

    public static string GetDefaultPath() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "GameNet Manager",
            "Config",
            DefaultFileName);

    public static void LoadInto(
        IConfigurationManager configuration,
        bool enableDefaultProtectedFile,
        string? explicitFilePath = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var overridePath = explicitFilePath;
        if (string.IsNullOrWhiteSpace(overridePath))
            overridePath = Environment.GetEnvironmentVariable(PathEnvironmentVariableName);

        string path;
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            path = Path.GetFullPath(overridePath);
            if (!File.Exists(path))
                throw new FileNotFoundException("The explicitly configured protected Server settings file was not found.", path);
        }
        else
        {
            if (!enableDefaultProtectedFile)
                return;

            path = GetDefaultPath();
            if (!File.Exists(path))
                throw new FileNotFoundException("Protected Server settings are enabled, but the protected settings file is missing.", path);
        }

        if (enableDefaultProtectedFile)
        {
            if (!string.Equals(
                    Path.GetFullPath(path),
                    Path.GetFullPath(GetDefaultPath()),
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "Production protected setup settings must use the canonical ProgramData path.");
            }

            ValidateCanonicalRuntimeAccessControl(path);
        }

        var settings = Read(path);
        // Legacy encrypted settings may still contain these values, but they are no longer runtime providers.
        // ServerSecretBootstrap loads the canonical DACL-restricted secret store instead.
        var runtimeSettings = settings
            .Where(pair => !RuntimeExcludedSecretKeys.Contains(pair.Key))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
        configuration.AddInMemoryCollection(runtimeSettings);
    }

    [SupportedOSPlatform("windows")]
    internal static void ValidateAccessRules(
        IEnumerable<FileSystemAccessRule> accessRules,
        bool isDirectory,
        SecurityIdentifier serviceSid)
    {
        ArgumentNullException.ThrowIfNull(accessRules);
        ArgumentNullException.ThrowIfNull(serviceSid);

        var rules = accessRules.ToArray();
        var systemSid = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        var administratorsSid = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        var usersSid = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);
        var networkServiceSid = new SecurityIdentifier(WellKnownSidType.NetworkServiceSid, null);
        var approved = isDirectory
            ? new HashSet<string>(StringComparer.Ordinal)
            {
                systemSid.Value, administratorsSid.Value, usersSid.Value,
                networkServiceSid.Value, serviceSid.Value
            }
            : new HashSet<string>(StringComparer.Ordinal)
            {
                systemSid.Value, administratorsSid.Value, serviceSid.Value
            };

        if (rules.Length == 0 || rules.Any(rule =>
                rule.AccessControlType != AccessControlType.Allow ||
                rule.IdentityReference is not SecurityIdentifier sid ||
                !approved.Contains(sid.Value)))
        {
            throw new InvalidOperationException(
                "Protected Server settings ACL grants access to an unapproved principal.");
        }

        var systemRights = CombineRights(rules, systemSid);
        var administratorRights = CombineRights(rules, administratorsSid);
        var serviceRights = CombineRights(rules, serviceSid);
        var requiredServiceRights = isDirectory
            ? FileSystemRights.ReadAndExecute
            : FileSystemRights.Read;
        if ((systemRights & FileSystemRights.FullControl) != FileSystemRights.FullControl ||
            (administratorRights & FileSystemRights.FullControl) != FileSystemRights.FullControl ||
            (serviceRights & requiredServiceRights) != requiredServiceRights)
        {
            throw new InvalidOperationException(
                "Protected Server settings ACL is missing a required access grant.");
        }

        const FileSystemRights writeRights =
            FileSystemRights.WriteData | FileSystemRights.AppendData |
            FileSystemRights.WriteAttributes | FileSystemRights.WriteExtendedAttributes |
            FileSystemRights.Delete | FileSystemRights.ChangePermissions |
            FileSystemRights.TakeOwnership | FileSystemRights.CreateFiles |
            FileSystemRights.CreateDirectories | FileSystemRights.DeleteSubdirectoriesAndFiles;
        if ((serviceRights & writeRights) != 0)
        {
            throw new InvalidOperationException(
                "The GameNet Server service identity has excessive protected-settings permissions.");
        }

        if (isDirectory)
        {
            var usersRights = CombineRights(rules, usersSid);
            var networkServiceRights = CombineRights(rules, networkServiceSid);
            if ((usersRights & writeRights) != 0 ||
                (networkServiceRights & writeRights) != 0)
            {
                throw new InvalidOperationException(
                    "Protected Server configuration directory grants write access to a shared service or ordinary users.");
            }
        }
    }

    private static void ValidateCanonicalRuntimeAccessControl(string path)
    {
        if (!OperatingSystem.IsWindows())
            throw new InvalidOperationException("Protected Server settings ACL validation requires Windows.");

        try
        {
            var fullPath = Path.GetFullPath(path);
            var directory = Path.GetDirectoryName(fullPath);
            if (string.IsNullOrWhiteSpace(directory) ||
                !Directory.Exists(directory) ||
                !File.Exists(fullPath))
            {
                throw new InvalidOperationException("Protected Server settings path is unavailable.");
            }

            RejectReparsePoint(directory, isDirectory: true);
            RejectReparsePoint(fullPath, isDirectory: false);
            var managerRoot = Path.GetDirectoryName(directory);
            if (string.IsNullOrWhiteSpace(managerRoot))
                throw new InvalidOperationException("Protected Server settings parent path is invalid.");
            RejectReparsePoint(managerRoot, isDirectory: true);

            var serviceSid = (SecurityIdentifier)new NTAccount("NT SERVICE", "GameNet 5 Server")
                .Translate(typeof(SecurityIdentifier));

            var managerRootSecurity = new DirectoryInfo(managerRoot)
                .GetAccessControl(AccessControlSections.Access);
            if (!managerRootSecurity.AreAccessRulesProtected)
                throw new InvalidOperationException("GameNet Manager root ACL inheritance must be disabled.");
            ValidateAccessRules(
                managerRootSecurity.GetAccessRules(true, false, typeof(SecurityIdentifier))
                    .Cast<FileSystemAccessRule>(),
                isDirectory: true,
                serviceSid);

            var directorySecurity = new DirectoryInfo(directory)
                .GetAccessControl(AccessControlSections.Access);
            if (!directorySecurity.AreAccessRulesProtected)
                throw new InvalidOperationException("Protected Server configuration directory ACL inheritance must be disabled.");
            ValidateAccessRules(
                directorySecurity.GetAccessRules(true, false, typeof(SecurityIdentifier))
                    .Cast<FileSystemAccessRule>(),
                isDirectory: true,
                serviceSid);

            var fileSecurity = new FileInfo(fullPath)
                .GetAccessControl(AccessControlSections.Access);
            if (!fileSecurity.AreAccessRulesProtected)
                throw new InvalidOperationException("Protected Server settings file ACL inheritance must be disabled.");
            ValidateAccessRules(
                fileSecurity.GetAccessRules(true, false, typeof(SecurityIdentifier))
                    .Cast<FileSystemAccessRule>(),
                isDirectory: false,
                serviceSid);

            var info = new FileInfo(fullPath);
            if (info.Length is <= 0 or > 65_536)
                throw new InvalidOperationException("Protected Server settings file size is invalid.");
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch
        {
            throw new InvalidOperationException(
                "Protected Server settings ACL or path validation failed; startup was stopped.");
        }
    }

    private static void RejectReparsePoint(string path, bool isDirectory)
    {
        var attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.ReparsePoint) != 0 ||
            (isDirectory && (attributes & FileAttributes.Directory) == 0) ||
            (!isDirectory && (attributes & FileAttributes.Directory) != 0))
        {
            throw new InvalidOperationException("Protected Server settings path contains an invalid filesystem object.");
        }
    }

    [SupportedOSPlatform("windows")]
    private static FileSystemRights CombineRights(
        IEnumerable<FileSystemAccessRule> rules,
        SecurityIdentifier sid) =>
        rules.Where(rule => rule.IdentityReference.Equals(sid))
            .Aggregate((FileSystemRights)0, (rights, rule) => rights | rule.FileSystemRights);

    public static IReadOnlyDictionary<string, string?> Read(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!File.Exists(path))
            throw new FileNotFoundException("Protected Server settings file was not found.", path);

        if (!OperatingSystem.IsWindows())
            throw new InvalidOperationException("Protected Server DPAPI settings are supported only on Windows.");

        byte[] clearBytes;
        try
        {
            var protectedBytes = File.ReadAllBytes(path);
            clearBytes = ProtectedData.Unprotect(protectedBytes, optionalEntropy: null, DataProtectionScope.LocalMachine);
            CryptographicOperations.ZeroMemory(protectedBytes);
        }
        catch (CryptographicException exception)
        {
            throw new InvalidOperationException("Protected Server settings could not be decrypted on this machine.", exception);
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<Dictionary<string, string?>>(clearBytes);
            if (parsed is null || parsed.Count == 0)
                throw new InvalidOperationException("Protected Server settings are empty or invalid JSON.");

            var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in parsed)
            {
                if (!AllowedKeys.Contains(pair.Key))
                    throw new InvalidOperationException($"Protected Server settings contain an unsupported key: {pair.Key}.");
                if (pair.Value is null)
                    throw new InvalidOperationException($"Protected Server setting '{pair.Key}' cannot be null.");
                if (!result.TryAdd(pair.Key, pair.Value))
                    throw new InvalidOperationException($"Protected Server settings contain a duplicate key: {pair.Key}.");
            }

            RequireNonBlank(result, "GameNet:Authentication:Issuer");
            RequireNonBlank(result, "GameNet:Authentication:Audience");
            ValidateOptionalBootstrapSecret(result);
            RequireNonBlank(result, "GameNet:ServerTls:CertificateThumbprint");

            // Legacy files may still have these duplicated secret values; the runtime loader filters them out.
            // New setup files must not store the deployment secrets here.
            ValidateLegacySecretIfPresent(result, "GameNet:DatabaseConnectionString", minimumLength: 1);
            ValidateLegacySecretIfPresent(result, "GameNet:Authentication:SigningKey", minimumLength: 32);
            ValidateLegacySecretIfPresent(result, "GameNet:Agent:ProvisioningKey", minimumLength: 32);
            var thumbprint = result["GameNet:ServerTls:CertificateThumbprint"]!.Replace(" ", string.Empty, StringComparison.Ordinal);
            if (thumbprint.Length != 40 || thumbprint.Any(character => !Uri.IsHexDigit(character)))
                throw new InvalidOperationException("Protected Server TLS certificate thumbprint must be exactly 40 hexadecimal characters.");
            result["GameNet:ServerTls:CertificateThumbprint"] = thumbprint.ToUpperInvariant();

            if (!bool.TryParse(result["GameNet:Authentication:Enabled"], out var enabled) || !enabled)
                throw new InvalidOperationException("Protected Server settings must enable authentication.");

            return result;
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("Protected Server settings are not valid JSON.", exception);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(clearBytes);
        }
    }

    private static void ValidateOptionalBootstrapSecret(IReadOnlyDictionary<string, string?> settings)
    {
        const string key = "GameNet:Setup:BootstrapSecret";
        if (!settings.TryGetValue(key, out var value))
            return;

        var byteCount = System.Text.Encoding.UTF8.GetByteCount(value ?? string.Empty);
        if (string.IsNullOrWhiteSpace(value) || byteCount is < 32 or > 2048)
            throw new InvalidOperationException("Protected Server bootstrap secret is invalid.");
    }

    private static void ValidateLegacySecretIfPresent(
        IReadOnlyDictionary<string, string?> settings,
        string key,
        int minimumLength)
    {
        if (!settings.TryGetValue(key, out var value))
            return;
        if (string.IsNullOrWhiteSpace(value) || value.Length < minimumLength)
            throw new InvalidOperationException($"Legacy protected Server setting '{key}' is invalid.");
    }

    private static void RequireNonBlank(
        IReadOnlyDictionary<string, string?> settings,
        string key)
    {
        if (!settings.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException($"Required protected Server setting '{key}' is missing.");
    }
}
