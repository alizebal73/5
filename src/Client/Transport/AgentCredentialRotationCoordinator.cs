using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using GameNet.Agent.Identity;
using GameNet.Shared.Contracts.V1.Security;
using Microsoft.Extensions.Options;

namespace GameNet.Agent.Transport;

public interface IAgentCredentialRotationCoordinator
{
    Task RotateForOperationAsync(string deviceId, Guid operationId, CancellationToken cancellationToken = default);
    Task<bool> ResumePendingIfAnyAsync(string deviceId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Executes a server-requested staged rotation. The current and replacement values
/// remain together in protected local state until the Server confirms the commit and
/// the replacement is atomically installed as the current credential.
/// </summary>
public sealed class AgentCredentialRotationCoordinator(
    IHttpClientFactory httpClientFactory,
    IAgentCredentialStore credentialStore,
    IOptions<AgentTransportOptions> options) : IAgentCredentialRotationCoordinator
{
    public async Task RotateForOperationAsync(
        string deviceId, Guid operationId, CancellationToken cancellationToken = default)
    {
        if (operationId == Guid.Empty)
            throw new InvalidOperationException("Agent credential rotation operation id is invalid.");

        var pending = await credentialStore.TryLoadPendingRotationAsync(deviceId, cancellationToken);
        if (pending is null)
        {
            var current = await credentialStore.TryLoadAsync(cancellationToken)
                ?? throw new InvalidOperationException("Agent credential is not available for staged rotation.");
            var replacement = GenerateSecret();
            await credentialStore.SavePendingRotationAsync(operationId, deviceId, current, replacement, cancellationToken);
            pending = await credentialStore.TryLoadPendingRotationAsync(deviceId, cancellationToken)
                ?? throw new InvalidOperationException("Protected pending Agent credential was not readable after write.");
        }

        if (pending.OperationId != operationId)
            throw new InvalidOperationException("A different protected Agent credential rotation is pending; controlled recovery is required.");
        await CompletePendingAsync(pending, cancellationToken);
    }

    public async Task<bool> ResumePendingIfAnyAsync(
        string deviceId, CancellationToken cancellationToken = default)
    {
        var pending = await credentialStore.TryLoadPendingRotationAsync(deviceId, cancellationToken);
        if (pending is null)
            return false;
        await CompletePendingAsync(pending, cancellationToken);
        return true;
    }

    private async Task CompletePendingAsync(
        AgentPendingCredentialRotation pending, CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient("GameNetAgentCredentialClient");
        var root = options.Value.ServerBaseUrl.TrimEnd('/');
        var stage = new AgentCredentialRotationStageRequest(
            pending.OperationId, pending.DeviceId, pending.CurrentSecret, pending.ReplacementSecret);
        var stageResponse = await SendAsync<AgentCredentialRotationStageRequest, AgentCredentialRotationStageResponse>(
            client, $"{root}/api/v1/agent/credentials/rotate/stage", stage, cancellationToken);
        if (stageResponse.OperationId != pending.OperationId ||
            !string.Equals(stageResponse.DeviceId, pending.DeviceId, StringComparison.Ordinal) ||
            stageResponse.State is not ("staged" or "committed" or "expired"))
            throw new InvalidOperationException("Server returned inconsistent Agent credential-rotation stage state.");

        if (stageResponse.State == "expired")
        {
            await VerifyCurrentCredentialStillActiveAsync(client, root, pending, cancellationToken);
            await credentialStore.DiscardExpiredPendingRotationAsync(
                pending.OperationId, pending.DeviceId, cancellationToken);
            return;
        }

        var confirmation = new AgentCredentialRotationConfirmationRequest(
            pending.OperationId, pending.DeviceId, pending.CurrentSecret, pending.ReplacementSecret);
        var committed = await SendAsync<AgentCredentialRotationConfirmationRequest, AgentCredentialRotationConfirmationResponse>(
            client, $"{root}/api/v1/agent/credentials/rotate/confirm", confirmation, cancellationToken);
        if (committed.OperationId != pending.OperationId ||
            !string.Equals(committed.DeviceId, pending.DeviceId, StringComparison.Ordinal) ||
            committed.CredentialId == Guid.Empty ||
            committed.State is not ("committed" or "already_committed"))
            throw new InvalidOperationException("Server did not confirm the Agent credential-rotation commit.");

        await credentialStore.PromotePendingRotationAsync(pending.OperationId, pending.DeviceId, cancellationToken);
    }

    private static async Task VerifyCurrentCredentialStillActiveAsync(
        HttpClient client,
        string root,
        AgentPendingCredentialRotation pending,
        CancellationToken cancellationToken)
    {
        var endpoint = $"{root}/api/v1/agent/auth/token";
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = JsonContent.Create(new AgentTokenRequest(pending.DeviceId, pending.CurrentSecret))
        };
        request.Headers.TryAddWithoutValidation("X-GameNet-Contract", "v1");

        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(request, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new InvalidOperationException(
                "Could not verify the prior Agent credential after rotation expiry; protected pending state was retained.");
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException(
                    "The prior Agent credential is not confirmed active after rotation expiry; protected pending state was retained.");
            try
            {
                var token = await response.Content.ReadFromJsonAsync<AgentTokenResponse>(
                    new JsonSerializerOptions(JsonSerializerDefaults.Web), cancellationToken);
                if (token is null || string.IsNullOrWhiteSpace(token.AccessToken))
                    throw new InvalidOperationException(
                        "Server returned no proof of the prior Agent credential after rotation expiry; protected pending state was retained.");
            }
            catch (JsonException)
            {
                throw new InvalidOperationException(
                    "Server returned invalid proof of the prior Agent credential after rotation expiry; protected pending state was retained.");
            }
        }
    }

    private static async Task<TResponse> SendAsync<TRequest, TResponse>(
        HttpClient client, string endpoint, TRequest body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint) { Content = JsonContent.Create(body) };
        request.Headers.TryAddWithoutValidation("X-GameNet-Contract", "v1");
        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(request, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // Do not attach low-level exception/request diagnostics that might contain request data.
            throw new InvalidOperationException("Agent credential rotation transport failed; protected pending state was retained.");
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException(
                    $"Agent credential rotation request failed with HTTP {(int)response.StatusCode}; protected pending state was retained.");
            try
            {
                var result = await response.Content.ReadFromJsonAsync<TResponse>(
                    new JsonSerializerOptions(JsonSerializerDefaults.Web), cancellationToken);
                return result ?? throw new InvalidOperationException("Server returned an empty Agent credential-rotation response.");
            }
            catch (JsonException)
            {
                throw new InvalidOperationException("Server returned an invalid Agent credential-rotation response; protected pending state was retained.");
            }
        }
    }

    private static string GenerateSecret()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        try
        {
            return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }
}
