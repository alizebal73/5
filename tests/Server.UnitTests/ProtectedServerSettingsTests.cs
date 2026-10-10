using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GameNet.Server.Infrastructure.Configuration;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace GameNet.Server.UnitTests;

public sealed class ProtectedServerSettingsTests
{
    [Fact]
    public void Protected_settings_are_machine_bound_and_loaded_without_plaintext_storage()
    {
        var path = Path.Combine(Path.GetTempPath(), "gamenet-server-secrets-" + Guid.NewGuid().ToString("N") + ".bin");
        var settings = CreateValidSettings();
        var clear = JsonSerializer.SerializeToUtf8Bytes(settings);
        var encrypted = Protect(clear);
        try
        {
            var signingKeyBytes = Encoding.UTF8.GetBytes(settings["GameNet:Authentication:SigningKey"]!);
            var certificateThumbprintBytes = Encoding.UTF8.GetBytes(settings["GameNet:ServerTls:CertificateThumbprint"]!);
            Assert.False(ContainsSequence(encrypted, signingKeyBytes));
            Assert.False(ContainsSequence(encrypted, certificateThumbprintBytes));
            CryptographicOperations.ZeroMemory(signingKeyBytes);
            CryptographicOperations.ZeroMemory(certificateThumbprintBytes);
            File.WriteAllBytes(path, encrypted);

            var loaded = ProtectedServerSettings.Read(path);
            Assert.Equal(settings["GameNet:DatabaseConnectionString"], loaded["GameNet:DatabaseConnectionString"]);
            Assert.Equal(settings["GameNet:Authentication:SigningKey"], loaded["GameNet:Authentication:SigningKey"]);
            Assert.Equal("true", loaded["GameNet:Authentication:Enabled"]);

            var configuration = new ConfigurationManager();
            ProtectedServerSettings.LoadInto(configuration, enableDefaultProtectedFile: false, explicitFilePath: path);
            Assert.Null(configuration["GameNet:DatabaseConnectionString"]);
            Assert.Null(configuration["GameNet:Authentication:SigningKey"]);
            Assert.Null(configuration["GameNet:Agent:ProvisioningKey"]);
            Assert.Equal(settings["GameNet:Setup:BootstrapSecret"], configuration["GameNet:Setup:BootstrapSecret"]);
            Assert.Equal(settings["GameNet:Authentication:Issuer"], configuration["GameNet:Authentication:Issuer"]);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(clear);
            CryptographicOperations.ZeroMemory(encrypted);
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void Protected_setup_settings_can_omit_runtime_deployment_secrets()
    {
        var path = Path.Combine(Path.GetTempPath(), "gamenet-server-setup-" + Guid.NewGuid().ToString("N") + ".bin");
        var settings = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["GameNet:Authentication:Enabled"] = "true",
            ["GameNet:Authentication:Issuer"] = "GameNet.Tests",
            ["GameNet:Authentication:Audience"] = "GameNet.Tests.Client",
            ["GameNet:Setup:BootstrapSecret"] = new string('b', 48),
            ["GameNet:ServerTls:CertificateThumbprint"] = new string('a', 40)
        };
        var clear = JsonSerializer.SerializeToUtf8Bytes(settings);
        var encrypted = Protect(clear);
        try
        {
            File.WriteAllBytes(path, encrypted);
            var configuration = new ConfigurationManager();
            ProtectedServerSettings.LoadInto(configuration, enableDefaultProtectedFile: false, explicitFilePath: path);

            Assert.Equal("true", configuration["GameNet:Authentication:Enabled"]);
            Assert.Equal("GameNet.Tests", configuration["GameNet:Authentication:Issuer"]);
            Assert.Equal(settings["GameNet:Setup:BootstrapSecret"], configuration["GameNet:Setup:BootstrapSecret"]);
            Assert.Null(configuration["GameNet:DatabaseConnectionString"]);
            Assert.Null(configuration["GameNet:Authentication:SigningKey"]);
            Assert.Null(configuration["GameNet:Agent:ProvisioningKey"]);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(clear);
            CryptographicOperations.ZeroMemory(encrypted);
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void Protected_settings_reject_invalid_tls_certificate_thumbprints()
    {
        var path = Path.Combine(Path.GetTempPath(), "gamenet-server-secrets-" + Guid.NewGuid().ToString("N") + ".bin");
        var settings = CreateValidSettings();
        settings["GameNet:ServerTls:CertificateThumbprint"] = "not-a-thumbprint";
        var clear = JsonSerializer.SerializeToUtf8Bytes(settings);
        var encrypted = Protect(clear);
        try
        {
            File.WriteAllBytes(path, encrypted);
            Assert.Throws<InvalidOperationException>(() => ProtectedServerSettings.Read(path));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(clear);
            CryptographicOperations.ZeroMemory(encrypted);
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void Protected_settings_reject_unknown_keys()
    {
        var path = Path.Combine(Path.GetTempPath(), "gamenet-server-secrets-" + Guid.NewGuid().ToString("N") + ".bin");
        var settings = CreateValidSettings();
        settings["GameNet:Unexpected:Value"] = "no";
        var clear = JsonSerializer.SerializeToUtf8Bytes(settings);
        var encrypted = Protect(clear);
        try
        {
            File.WriteAllBytes(path, encrypted);
            Assert.Throws<InvalidOperationException>(() => ProtectedServerSettings.Read(path));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(clear);
            CryptographicOperations.ZeroMemory(encrypted);
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void Protected_settings_reject_tampered_ciphertext()
    {
        var path = Path.Combine(Path.GetTempPath(), "gamenet-server-secrets-" + Guid.NewGuid().ToString("N") + ".bin");
        var clear = JsonSerializer.SerializeToUtf8Bytes(CreateValidSettings());
        var encrypted = Protect(clear);
        encrypted[0] ^= 0x80;
        try
        {
            File.WriteAllBytes(path, encrypted);
            Assert.Throws<InvalidOperationException>(() => ProtectedServerSettings.Read(path));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(clear);
            CryptographicOperations.ZeroMemory(encrypted);
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void Enabling_protected_settings_fails_closed_if_the_file_is_missing()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "missing.bin");
        var configuration = new ConfigurationManager();
        Assert.Throws<FileNotFoundException>(() =>
            ProtectedServerSettings.LoadInto(configuration, enableDefaultProtectedFile: true, explicitFilePath: path));
    }

    private static byte[] Protect(byte[] clear)
    {
        if (!OperatingSystem.IsWindows())
            throw new InvalidOperationException("This DPAPI test requires Windows.");
        return ProtectedData.Protect(clear, optionalEntropy: null, DataProtectionScope.LocalMachine);
    }

    private static bool ContainsSequence(byte[] haystack, byte[] needle)
    {
        if (needle.Length == 0) return true;
        for (var i = 0; i <= haystack.Length - needle.Length; i++)
        {
            var match = true;
            for (var j = 0; j < needle.Length; j++)
            {
                if (haystack[i + j] == needle[j]) continue;
                match = false;
                break;
            }
            if (match) return true;
        }
        return false;
    }

    private static Dictionary<string, string?> CreateValidSettings() => new(StringComparer.Ordinal)
    {
        ["GameNet:DatabaseConnectionString"] = "Host=127.0.0.1;Database=gamenet_test;Username=gamenet_test;Password=not-a-real-secret",
        ["GameNet:Authentication:Enabled"] = "true",
        ["GameNet:Authentication:Issuer"] = "GameNet.Tests",
        ["GameNet:Authentication:Audience"] = "GameNet.Tests.Client",
        ["GameNet:Authentication:SigningKey"] = new string('s', 48),
        ["GameNet:Agent:ProvisioningKey"] = new string('p', 48),
        ["GameNet:Setup:BootstrapSecret"] = new string('b', 48),
        ["GameNet:ServerTls:CertificateThumbprint"] = new string('a', 40)
    };
}
