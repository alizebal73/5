using System.Security.Cryptography;
using System.Text;
using GameNet.Agent.Transport;
using Microsoft.Extensions.Options;

namespace GameNet.Agent.Identity;

public sealed class AgentCredentialStore(
    IOptions<AgentIdentityOptions> identityOptions,
    IOptions<AgentTransportOptions> transportOptions) : IAgentCredentialStore
{
    public async Task<string> GetOrBootstrapAsync(CancellationToken cancellationToken = default)
    {
        var root = Path.GetFullPath(identityOptions.Value.RootPath);
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "credential.bin");

        if (File.Exists(path))
        {
            var protectedBytes = await File.ReadAllBytesAsync(path, cancellationToken);
            try
            {
                var clear = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);
                var stored = Encoding.UTF8.GetString(clear);
                if (!string.IsNullOrWhiteSpace(stored))
                    return stored;
            }
            catch (CryptographicException exception)
            {
                throw new InvalidOperationException("Stored Agent credential cannot be decrypted.", exception);
            }
        }

        var variableName = transportOptions.Value.BootstrapCredentialEnvironmentVariableName;
        var bootstrap = Environment.GetEnvironmentVariable(variableName);
        if (string.IsNullOrWhiteSpace(bootstrap))
            throw new InvalidOperationException($"No Agent bootstrap credential is available. Set {variableName} for the Agent service identity.");

        await SaveAsync(bootstrap, cancellationToken);
        Environment.SetEnvironmentVariable(variableName, null, EnvironmentVariableTarget.Process);
        return bootstrap;
    }

    public async Task SaveAsync(string secret, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(secret) || secret.Length < 32)
            throw new ArgumentException("Agent credential must contain at least 32 characters.", nameof(secret));

        var root = Path.GetFullPath(identityOptions.Value.RootPath);
        Directory.CreateDirectory(root);
        var protectedBytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(secret), null, DataProtectionScope.CurrentUser);
        var path = Path.Combine(root, "credential.bin");
        var temp = path + ".tmp";
        await File.WriteAllBytesAsync(temp, protectedBytes, cancellationToken);
        File.Move(temp, path, overwrite: true);
    }
}
