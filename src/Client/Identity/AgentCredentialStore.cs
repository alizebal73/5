using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using GameNet.Agent.Transport;
using Microsoft.Extensions.Options;

namespace GameNet.Agent.Identity;

public sealed class AgentCredentialStore(
    IOptions<AgentIdentityOptions> identityOptions,
    IOptions<AgentTransportOptions> transportOptions,
    IHostEnvironment? hostEnvironment = null) : IAgentCredentialStore
{
    public async Task<string?> TryLoadAsync(CancellationToken cancellationToken = default)
    {
        var root = identityOptions.Value.ResolveRootPath();
        var path = Path.Combine(root, "credential.bin");
        if (!File.Exists(path))
            return null;
        RejectReparsePoint(path);
        if (IsProduction())
            AgentStateAccessControl.ValidateCredentialFile(root, path);

        byte[]? protectedBytes = null;
        byte[]? clearBytes = null;
        try
        {
            protectedBytes = await File.ReadAllBytesAsync(path, cancellationToken);
            clearBytes = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);
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

    public async Task<AgentPendingCredentialRotation?> TryLoadPendingRotationAsync(
        string deviceId, CancellationToken cancellationToken = default)
    {
        var root = identityOptions.Value.ResolveRootPath();
        if (!Directory.Exists(root))
            return null;
        ValidatePendingRotationRoot(root);
        var path = Path.Combine(root, PendingRotationFileName);
        if (!File.Exists(path))
            return null;
        RejectReparsePoint(path);
        if (IsProduction())
            AgentStateAccessControl.ValidatePendingCredentialRotationFile(root, path);

        byte[]? protectedBytes = null;
        byte[]? clearBytes = null;
        byte[]? entropy = null;
        try
        {
            var info = new FileInfo(path);
            if (info.Length is <= 0 or > MaximumPendingRotationBytes)
                throw new InvalidOperationException("Protected Agent credential-rotation state has an invalid size.");
            protectedBytes = await File.ReadAllBytesAsync(path, cancellationToken);
            entropy = CreatePendingRotationEntropy(deviceId);
            clearBytes = ProtectedData.Unprotect(protectedBytes, entropy, DataProtectionScope.CurrentUser);
            var state = JsonSerializer.Deserialize<AgentPendingCredentialRotation>(
                clearBytes, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            if (state is null || state.OperationId == Guid.Empty ||
                !string.Equals(state.DeviceId, deviceId, StringComparison.Ordinal) ||
                !IsCanonicalSecret(state.CurrentSecret) || !IsCanonicalSecret(state.ReplacementSecret))
                throw new InvalidOperationException("Protected Agent credential-rotation state is invalid or belongs to another DeviceId.");
            return state;
        }
        catch (CryptographicException)
        {
            throw new InvalidOperationException("Protected Agent credential-rotation state cannot be decrypted; refusing automatic recovery.");
        }
        catch (JsonException)
        {
            throw new InvalidOperationException("Protected Agent credential-rotation state is corrupt; refusing automatic recovery.");
        }
        finally
        {
            if (protectedBytes is not null) CryptographicOperations.ZeroMemory(protectedBytes);
            if (clearBytes is not null) CryptographicOperations.ZeroMemory(clearBytes);
            if (entropy is not null) CryptographicOperations.ZeroMemory(entropy);
        }
    }

    public async Task SavePendingRotationAsync(
        Guid operationId,
        string deviceId,
        string currentSecret,
        string replacementSecret,
        CancellationToken cancellationToken = default)
    {
        if (operationId == Guid.Empty || string.IsNullOrWhiteSpace(deviceId) ||
            !IsCanonicalSecret(currentSecret) || !IsCanonicalSecret(replacementSecret) ||
            string.Equals(currentSecret, replacementSecret, StringComparison.Ordinal))
            throw new ArgumentException("Agent credential-rotation state is invalid.");

        var root = identityOptions.Value.ResolveRootPath();
        if (!IsProduction() && !Directory.Exists(root))
            Directory.CreateDirectory(root);
        ValidatePendingRotationRoot(root);
        var path = Path.Combine(root, PendingRotationFileName);
        var existing = await TryLoadPendingRotationAsync(deviceId, cancellationToken);
        if (existing is not null)
        {
            if (existing.OperationId == operationId &&
                string.Equals(existing.CurrentSecret, currentSecret, StringComparison.Ordinal) &&
                string.Equals(existing.ReplacementSecret, replacementSecret, StringComparison.Ordinal))
                return;
            throw new InvalidOperationException("A different Agent credential rotation is already pending; refusing to overwrite recoverable state.");
        }

        var payload = new AgentPendingCredentialRotation(
            operationId, deviceId, currentSecret, replacementSecret, DateTimeOffset.UtcNow);
        byte[]? clearBytes = null;
        byte[]? protectedBytes = null;
        byte[]? entropy = null;
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            clearBytes = JsonSerializer.SerializeToUtf8Bytes(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            entropy = CreatePendingRotationEntropy(deviceId);
            protectedBytes = ProtectedData.Protect(clearBytes, entropy, DataProtectionScope.CurrentUser);
            await using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(protectedBytes, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }
            if (IsProduction())
                AgentStateAccessControl.ProtectStateFileForReplacement(root, temp, PendingRotationFileName);
            try
            {
                File.Move(temp, path, overwrite: false);
            }
            catch (IOException) when (File.Exists(path))
            {
                var winner = await TryLoadPendingRotationAsync(deviceId, cancellationToken);
                if (winner is null || winner.OperationId != operationId ||
                    !string.Equals(winner.CurrentSecret, currentSecret, StringComparison.Ordinal) ||
                    !string.Equals(winner.ReplacementSecret, replacementSecret, StringComparison.Ordinal))
                    throw new InvalidOperationException("Concurrent Agent credential-rotation state did not match; refusing to overwrite it.");
            }
        }
        finally
        {
            if (clearBytes is not null) CryptographicOperations.ZeroMemory(clearBytes);
            if (protectedBytes is not null) CryptographicOperations.ZeroMemory(protectedBytes);
            if (entropy is not null) CryptographicOperations.ZeroMemory(entropy);
            try { File.Delete(temp); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    public async Task DiscardExpiredPendingRotationAsync(
        Guid operationId, string deviceId, CancellationToken cancellationToken = default)
    {
        var pending = await TryLoadPendingRotationAsync(deviceId, cancellationToken);
        if (pending is null)
            return;
        if (pending.OperationId != operationId)
            throw new InvalidOperationException("Pending Agent credential-rotation operation does not match; refusing cleanup.");

        var root = identityOptions.Value.ResolveRootPath();
        var path = Path.Combine(root, PendingRotationFileName);
        RejectReparsePoint(path);
        if (IsProduction())
            AgentStateAccessControl.ValidatePendingCredentialRotationFile(root, path);
        File.Delete(path);
    }

    public async Task PromotePendingRotationAsync(
        Guid operationId, string deviceId, CancellationToken cancellationToken = default)
    {
        var pending = await TryLoadPendingRotationAsync(deviceId, cancellationToken)
            ?? throw new InvalidOperationException("No protected Agent credential-rotation state is available to promote.");
        if (pending.OperationId != operationId)
            throw new InvalidOperationException("Protected Agent credential-rotation operation id changed; refusing promotion.");

        // Keep the pending file until SaveAsync atomically persists the current value.
        // If cleanup is interrupted, both secrets remain available for idempotent replay.
        await SaveAsync(pending.ReplacementSecret, cancellationToken);
        var path = Path.Combine(identityOptions.Value.ResolveRootPath(), PendingRotationFileName);
        RejectReparsePoint(path);
        File.Delete(path);
    }

    private const string PendingRotationFileName = "credential-rotation.pending.dpapi";
    private const int MaximumPendingRotationBytes = 16_384;
    private const string PendingRotationEntropyPrefix = "GameNet.AgentCredentialRotation.v1|";

    private static byte[] CreatePendingRotationEntropy(string deviceId) =>
        Encoding.UTF8.GetBytes(PendingRotationEntropyPrefix + deviceId);

    private bool IsProduction() => hostEnvironment?.IsProduction() ?? false;

    private void ValidatePendingRotationRoot(string root)
    {
        if (IsProduction())
        {
            AgentStateAccessControl.ValidateProductionStateRoot(root);
            return;
        }
        if (Directory.Exists(root) &&
            ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0 ||
             (File.GetAttributes(root) & FileAttributes.Directory) == 0))
            throw new InvalidOperationException("Agent credential-rotation state path is not a regular directory.");
    }

    private static bool IsCanonicalSecret(string? secret) =>
        secret is { Length: 43 } &&
        secret.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_') &&
        "AEIMQUYcgkosw048".Contains(secret[^1]);

    private static void RejectReparsePoint(string path)
    {
        if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException("Agent credential-rotation state cannot be a reparse point.");
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
        if (IsProduction())
            AgentStateAccessControl.ValidateProductionStateRoot(root);
        else
            Directory.CreateDirectory(root);
        var path = Path.Combine(root, "credential.bin");
        if (File.Exists(path))
        {
            RejectReparsePoint(path);
            if (IsProduction())
                AgentStateAccessControl.ValidateCredentialFile(root, path);
        }
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";

        byte[]? clearBytes = null;
        byte[]? protectedBytes = null;
        try
        {
            clearBytes = Encoding.UTF8.GetBytes(secret);
            protectedBytes = ProtectedData.Protect(clearBytes, null, DataProtectionScope.CurrentUser);
            await using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(protectedBytes, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }
            if (IsProduction())
                AgentStateAccessControl.ProtectStateFileForReplacement(root, temp, "credential.bin");
            File.Move(temp, path, overwrite: true);
            RejectReparsePoint(path);
            if (IsProduction())
                AgentStateAccessControl.ValidateCredentialFile(root, path);
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
