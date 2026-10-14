using System.Security.Cryptography;
using GameNet.Agent.Identity;
using GameNet.Agent.Transport;
using Microsoft.Extensions.Options;
using Xunit;

namespace GameNet.Agent.Tests;

public sealed class AgentCredentialStoreTests
{
    [Fact]
    public async Task Credential_write_is_read_back_before_store_reports_success()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var root = Path.Combine(Path.GetTempPath(), "gamenet-agent-credential-store-" + Guid.NewGuid().ToString("N"));
        var secret = GenerateSecret();
        var store = CreateStore(root);

        try
        {
            await store.SaveAsync(secret);

            Assert.Equal(secret, await store.TryLoadAsync());

            var encrypted = await File.ReadAllBytesAsync(Path.Combine(root, "credential.bin"));
            Assert.False(ContainsSequence(encrypted, System.Text.Encoding.UTF8.GetBytes(secret)));
            CryptographicOperations.ZeroMemory(encrypted);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch (DirectoryNotFoundException) { }
        }
    }

    [Fact]
    public async Task Pending_rotation_is_read_back_and_promoted_without_losing_recovery_state()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var root = Path.Combine(Path.GetTempPath(), "gamenet-agent-rotation-store-" + Guid.NewGuid().ToString("N"));
        var store = CreateStore(root);
        const string deviceId = "store-test-device-01";
        var currentSecret = GenerateSecret();
        var replacementSecret = GenerateSecret();
        var operationId = Guid.NewGuid();
        var pendingPath = Path.Combine(root, "credential-rotation.pending.dpapi");

        try
        {
            await store.SaveAsync(currentSecret);
            await store.SavePendingRotationAsync(
                operationId, deviceId, currentSecret, replacementSecret);

            var pending = await store.TryLoadPendingRotationAsync(deviceId);
            Assert.NotNull(pending);
            Assert.Equal(operationId, pending!.OperationId);
            Assert.Equal(currentSecret, pending.CurrentSecret);
            Assert.Equal(replacementSecret, pending.ReplacementSecret);
            Assert.True(File.Exists(pendingPath));
            Assert.Equal(currentSecret, await store.TryLoadAsync());

            await store.PromotePendingRotationAsync(operationId, deviceId);

            Assert.Equal(replacementSecret, await store.TryLoadAsync());
            Assert.False(File.Exists(pendingPath));
            Assert.Null(await store.TryLoadPendingRotationAsync(deviceId));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch (DirectoryNotFoundException) { }
        }
    }

    private static AgentCredentialStore CreateStore(string root) =>
        new(
            Options.Create(new AgentIdentityOptions { RootPath = root }),
            Options.Create(new AgentTransportOptions()));

    private static string GenerateSecret()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        try
        {
            return Convert.ToBase64String(bytes)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    private static bool ContainsSequence(byte[] data, byte[] pattern)
    {
        try
        {
            if (pattern.Length == 0 || pattern.Length > data.Length)
                return false;
            for (var start = 0; start <= data.Length - pattern.Length; start++)
            {
                var matches = true;
                for (var offset = 0; offset < pattern.Length; offset++)
                {
                    if (data[start + offset] == pattern[offset])
                        continue;
                    matches = false;
                    break;
                }

                if (matches)
                    return true;
            }

            return false;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(pattern);
        }
    }
}
