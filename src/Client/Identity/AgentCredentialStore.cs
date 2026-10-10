using System.Security.Cryptography;
using System.Text;
using GameNet.Agent.Transport;
using Microsoft.Extensions.Options;

namespace GameNet.Agent.Identity;

public sealed class AgentCredentialStore(
    IOptions<AgentIdentityOptions> identityOptions,
    IOptions<AgentTransportOptions> transportOptions) : IAgentCredentialStore
{
    public async Task<string?> TryLoadAsync(CancellationToken cancellationToken = default)
    {
        var root = identityOptions.Value.ResolveRootPath();
        var path = Path.Combine(root, "credential.bin");
        if (!File.Exists(path))
            return null;

        byte[]? protectedBytes = null;
        byte[]? clearBytes = null;
        try
        {
            protectedBytes = await File.ReadAllBytesAsync(path, cancellationToken);
            clearBytes = ProtectedData.Unprotect(
                protectedBytes, null, DataProtectionScope.CurrentUser);
            var stored = Encoding.UTF8.GetString(clearBytes);
            if (string.IsNullOrWhiteSpace(stored))
                throw new InvalidOperationException("Stored Agent credential is empty.");
            return stored;
        }
        catch (CryptographicException exception)
        {
            throw new InvalidOperationException(
                "Stored Agent credential cannot be decrypted; refusing to silently enroll a different identity.",
                exception);
        }
        finally
        {
            if (protectedBytes is not null)
                CryptographicOperations.ZeroMemory(protectedBytes);
            if (clearBytes is not null)
                CryptographicOperations.ZeroMemory(clearBytes);
        }
    }

    public async Task<string> GetOrBootstrapAsync(CancellationToken cancellationToken = default)
    {
        var stored = await TryLoadAsync(cancellationToken);
        if (stored is not null)
        {
            Environment.SetEnvironmentVariable(
                transportOptions.Value.BootstrapCredentialEnvironmentVariableName,
                null,
                EnvironmentVariableTarget.Process);
            return stored;
        }

        // Backward-compatible path for installations still using the previous
        // protected credential provisioning mechanism. New installations should
        // provide GAMENET_AGENT_ENROLLMENT_TOKEN instead.
        var variableName = transportOptions.Value.BootstrapCredentialEnvironmentVariableName;
        var bootstrap = Environment.GetEnvironmentVariable(variableName);
        if (string.IsNullOrWhiteSpace(bootstrap))
            throw new InvalidOperationException(
                $"No stored Agent credential is available. Complete one-time enrollment using {transportOptions.Value.EnrollmentTokenEnvironmentVariableName}.");

        await SaveAsync(bootstrap, cancellationToken);
        Environment.SetEnvironmentVariable(variableName, null, EnvironmentVariableTarget.Process);
        return bootstrap;
    }

    public async Task SaveAsync(string secret, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(secret) || secret.Length < 32)
            throw new ArgumentException(
                "Agent credential must contain at least 32 characters.", nameof(secret));

        var root = identityOptions.Value.ResolveRootPath();
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "credential.bin");
        var temp = path + ".tmp";

        byte[]? clearBytes = null;
        byte[]? protectedBytes = null;
        try
        {
            clearBytes = Encoding.UTF8.GetBytes(secret);
            protectedBytes = ProtectedData.Protect(
                clearBytes, null, DataProtectionScope.CurrentUser);
            await File.WriteAllBytesAsync(temp, protectedBytes, cancellationToken);
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            if (clearBytes is not null)
                CryptographicOperations.ZeroMemory(clearBytes);
            if (protectedBytes is not null)
                CryptographicOperations.ZeroMemory(protectedBytes);
            try { File.Delete(temp); } catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
