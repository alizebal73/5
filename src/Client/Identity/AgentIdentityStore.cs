using System.Text.Json;
using Microsoft.Extensions.Options;

namespace GameNet.Agent.Identity;

public interface IAgentIdentityStore
{
    Task<GameNet.Agent.AgentIdentity> GetOrCreateAsync(CancellationToken cancellationToken = default);
}

public sealed class AgentIdentityStore(IOptions<AgentIdentityOptions> options) : IAgentIdentityStore
{
    public async Task<GameNet.Agent.AgentIdentity> GetOrCreateAsync(CancellationToken cancellationToken = default)
    {
        var configuredDeviceId = string.IsNullOrWhiteSpace(options.Value.DeviceId)
            ? null
            : GameNet.Agent.AgentIdentity.FromDeviceId(options.Value.DeviceId).DeviceId;
        var root = options.Value.ResolveRootPath();
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "identity.json");

        if (File.Exists(path))
        {
            var existing = await ReadExistingAsync(path, cancellationToken);
            EnsureConfiguredIdentityMatches(existing, configuredDeviceId);
            return existing;
        }

        var identity = configuredDeviceId is null
            ? GameNet.Agent.AgentIdentity.FromDeviceId(Guid.NewGuid().ToString("N"))
            : GameNet.Agent.AgentIdentity.FromDeviceId(configuredDeviceId);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(
                temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(
                    stream, new IdentityFile(identity.DeviceId), cancellationToken: cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            try
            {
                File.Move(temp, path, overwrite: false);
                return identity;
            }
            catch (IOException) when (File.Exists(path))
            {
                // Another Agent process won the first-start race. Keep its stable identity,
                // but fail closed if a configured enrollment identity disagrees.
                var existing = await ReadExistingAsync(path, cancellationToken);
                EnsureConfiguredIdentityMatches(existing, configuredDeviceId);
                return existing;
            }
        }
        finally
        {
            try { File.Delete(temp); } catch (IOException) { }
        }
    }

    private static void EnsureConfiguredIdentityMatches(
        GameNet.Agent.AgentIdentity existing,
        string? configuredDeviceId)
    {
        if (configuredDeviceId is not null &&
            !string.Equals(existing.DeviceId, configuredDeviceId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Configured Agent DeviceId does not match the persisted identity. Refusing to change identity automatically.");
        }
    }

    private static async Task<GameNet.Agent.AgentIdentity> ReadExistingAsync(
        string path,
        CancellationToken cancellationToken)
    {
        await using var read = File.OpenRead(path);
        var stored = await JsonSerializer.DeserializeAsync<IdentityFile>(
            read, cancellationToken: cancellationToken);
        if (string.IsNullOrWhiteSpace(stored?.DeviceId))
            throw new InvalidOperationException("Stored Agent identity is empty or invalid.");

        try
        {
            return GameNet.Agent.AgentIdentity.FromDeviceId(stored.DeviceId);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidOperationException("Stored Agent identity is invalid; refusing to silently create a new identity.", exception);
        }
    }

    private sealed record IdentityFile(string DeviceId);
}
