using System.Net.Http.Json;
using System.Text.Json;
using GameNet.Agent.Identity;
using GameNet.Shared.Contracts.V1.Security;
using Microsoft.Extensions.Options;

namespace GameNet.Agent.Transport;

public interface IAgentEnrollmentBootstrapper
{
    Task<string?> EnrollIfConfiguredAsync(
        string deviceId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Redeems the one-time operator-issued enrollment token on a first Agent start.
/// The token is read only from the Agent service process environment and is cleared
/// only after the issued per-device credential has been durably protected on disk.
/// </summary>
public sealed class AgentEnrollmentBootstrapper(
    IHttpClientFactory httpClientFactory,
    IAgentCredentialStore credentialStore,
    IOptions<AgentTransportOptions> options) : IAgentEnrollmentBootstrapper
{
    public async Task<string?> EnrollIfConfiguredAsync(
        string deviceId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
            throw new ArgumentException("Agent DeviceId is required.", nameof(deviceId));

        var variableName = options.Value.EnrollmentTokenEnvironmentVariableName;
        var enrollmentToken = Environment.GetEnvironmentVariable(variableName);
        if (string.IsNullOrWhiteSpace(enrollmentToken))
            return null;

        var endpoint = $"{options.Value.ServerBaseUrl.TrimEnd('/')}/api/v1/agent/enrollment/redeem";
        var client = httpClientFactory.CreateClient("GameNetAgentCredentialClient");
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = JsonContent.Create(new AgentEnrollmentRedeemRequest(deviceId, enrollmentToken))
        };
        request.Headers.TryAddWithoutValidation("X-GameNet-Contract", "v1");

        using var response = await client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(
                $"Agent enrollment failed with HTTP {(int)response.StatusCode}. Check that the enrollment token is valid, unexpired, and issued for this DeviceId.");

        var issued = await response.Content.ReadFromJsonAsync<AgentCredentialSecretResponse>(
            new JsonSerializerOptions(JsonSerializerDefaults.Web),
            cancellationToken);
        if (issued is null ||
            !string.Equals(issued.DeviceId, deviceId, StringComparison.Ordinal) ||
            !IsCredentialSecret(issued.Secret))
        {
            throw new InvalidOperationException(
                "Server returned an invalid Agent enrollment response; the enrollment token has been retained for safe recovery.");
        }

        // Persist before deleting the one-time token. A disk/DPAPI failure must not
        // consume the only bootstrap material needed to complete first enrollment.
        await credentialStore.SaveAsync(issued.Secret, cancellationToken);
        Environment.SetEnvironmentVariable(variableName, null, EnvironmentVariableTarget.Process);
        return issued.Secret;
    }

    private static bool IsCredentialSecret(string secret) =>
        secret.Length == 43 &&
        secret.All(character =>
            char.IsAsciiLetterOrDigit(character) || character is '-' or '_');
}
