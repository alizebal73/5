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
        var root = Path.GetFullPath(options.Value.RootPath);
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "identity.json");

        if (File.Exists(path))
        {
            await using var read = File.OpenRead(path);
            var stored = await JsonSerializer.DeserializeAsync<IdentityFile>(read, cancellationToken: cancellationToken);
            if (!string.IsNullOrWhiteSpace(stored?.DeviceId))
                return GameNet.Agent.AgentIdentity.FromDeviceId(stored.DeviceId);
        }

        var identity = GameNet.Agent.AgentIdentity.FromDeviceId(Guid.NewGuid().ToString("N"));
        var temp = path + ".tmp";
        await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(new IdentityFile(identity.DeviceId)), cancellationToken);
        File.Move(temp, path, overwrite: true);
        return identity;
    }

    private sealed record IdentityFile(string DeviceId);
}
