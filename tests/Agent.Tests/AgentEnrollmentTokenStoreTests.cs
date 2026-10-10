using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GameNet.Agent.Identity;
using Microsoft.Extensions.Options;
using Xunit;

namespace GameNet.Agent.Tests;

public sealed class AgentEnrollmentTokenStoreTests
{
    [Fact]
    public async Task Loads_machine_protected_token_for_the_matching_device_then_deletes_it()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var root = Path.Combine(Path.GetTempPath(), "gamenet-agent-enrollment-token-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        const string deviceId = "station-pc-01";
        const string token = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ-_abcde";
        var path = Path.Combine(root, AgentEnrollmentTokenStore.FileName);
        var clear = JsonSerializer.SerializeToUtf8Bytes(new
        {
            formatVersion = 1,
            deviceId,
            token,
            expiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(10)
        });
        var entropy = Encoding.UTF8.GetBytes("GameNet.AgentEnrollmentToken.v1|" + deviceId);
        byte[]? protectedBytes = null;

        try
        {
            protectedBytes = ProtectedData.Protect(clear, entropy, DataProtectionScope.LocalMachine);
            await File.WriteAllBytesAsync(path, protectedBytes);
            var store = new AgentEnrollmentTokenStore(
                Options.Create(new AgentIdentityOptions { RootPath = root }), TimeProvider.System);

            var loaded = await store.TryLoadAsync(deviceId);

            Assert.NotNull(loaded);
            Assert.Equal(1, loaded!.FormatVersion);
            Assert.Equal(deviceId, loaded.DeviceId);
            Assert.Equal(token, loaded.Token);
            Assert.True(loaded.ExpiresAtUtc > DateTimeOffset.UtcNow);

            await store.DeleteAsync();
            Assert.False(File.Exists(path));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(clear);
            CryptographicOperations.ZeroMemory(entropy);
            if (protectedBytes is not null)
                CryptographicOperations.ZeroMemory(protectedBytes);
            try { Directory.Delete(root, recursive: true); } catch (DirectoryNotFoundException) { }
        }
    }

    [Fact]
    public async Task Rejects_token_when_device_id_does_not_match_the_dpapi_entropy()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var root = Path.Combine(Path.GetTempPath(), "gamenet-agent-enrollment-token-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        const string originalDeviceId = "station-pc-01";
        const string otherDeviceId = "station-pc-02";
        var path = Path.Combine(root, AgentEnrollmentTokenStore.FileName);
        var clear = JsonSerializer.SerializeToUtf8Bytes(new
        {
            formatVersion = 1,
            deviceId = originalDeviceId,
            token = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ-_abcde",
            expiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(10)
        });
        var entropy = Encoding.UTF8.GetBytes("GameNet.AgentEnrollmentToken.v1|" + originalDeviceId);
        byte[]? protectedBytes = null;

        try
        {
            protectedBytes = ProtectedData.Protect(clear, entropy, DataProtectionScope.LocalMachine);
            await File.WriteAllBytesAsync(path, protectedBytes);
            var store = new AgentEnrollmentTokenStore(
                Options.Create(new AgentIdentityOptions { RootPath = root }), TimeProvider.System);

            await Assert.ThrowsAsync<InvalidOperationException>(() => store.TryLoadAsync(otherDeviceId));
            Assert.True(File.Exists(path));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(clear);
            CryptographicOperations.ZeroMemory(entropy);
            if (protectedBytes is not null)
                CryptographicOperations.ZeroMemory(protectedBytes);
            try { Directory.Delete(root, recursive: true); } catch (DirectoryNotFoundException) { }
        }
    }

    [Fact]
    public async Task Rejects_an_expired_token_without_deleting_it_automatically()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var root = Path.Combine(Path.GetTempPath(), "gamenet-agent-enrollment-token-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        const string deviceId = "station-pc-01";
        var path = Path.Combine(root, AgentEnrollmentTokenStore.FileName);
        var clear = JsonSerializer.SerializeToUtf8Bytes(new
        {
            formatVersion = 1,
            deviceId,
            token = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ-_abcde",
            expiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1)
        });
        var entropy = Encoding.UTF8.GetBytes("GameNet.AgentEnrollmentToken.v1|" + deviceId);
        byte[]? protectedBytes = null;

        try
        {
            protectedBytes = ProtectedData.Protect(clear, entropy, DataProtectionScope.LocalMachine);
            await File.WriteAllBytesAsync(path, protectedBytes);
            var store = new AgentEnrollmentTokenStore(
                Options.Create(new AgentIdentityOptions { RootPath = root }), TimeProvider.System);

            await Assert.ThrowsAsync<InvalidOperationException>(() => store.TryLoadAsync(deviceId));
            Assert.True(File.Exists(path));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(clear);
            CryptographicOperations.ZeroMemory(entropy);
            if (protectedBytes is not null)
                CryptographicOperations.ZeroMemory(protectedBytes);
            try { Directory.Delete(root, recursive: true); } catch (DirectoryNotFoundException) { }
        }
    }
}
