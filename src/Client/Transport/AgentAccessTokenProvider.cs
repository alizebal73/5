using System.Net.Http.Json;
using System.Text.Json;
using GameNet.Agent.Identity;
using GameNet.Shared.Contracts.V1.Security;
using Microsoft.Extensions.Options;

namespace GameNet.Agent.Transport;

public sealed class AgentAccessTokenProvider(
    IHttpClientFactory httpClientFactory,
    IAgentCredentialStore credentialStore,
    IAgentEnrollmentBootstrapper enrollmentBootstrapper,
    IOptions<AgentTransportOptions> options,
    TimeProvider timeProvider) : IAgentAccessTokenProvider, IDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private string? cachedDeviceId;
    private string? cachedToken;
    private DateTimeOffset expiresAtUtc;

    public async Task<string> GetAccessTokenAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var now = timeProvider.GetUtcNow();
            if (string.Equals(cachedDeviceId, deviceId, StringComparison.Ordinal) &&
                !string.IsNullOrWhiteSpace(cachedToken) &&
                expiresAtUtc > now.AddSeconds(30))
            {
                return cachedToken;
            }

            var secret = await credentialStore.TryLoadAsync(cancellationToken);
            if (secret is not null)
            {
                // A paired installation never needs an enrollment token again.
                Environment.SetEnvironmentVariable(
                    options.Value.EnrollmentTokenEnvironmentVariableName,
                    null,
                    EnvironmentVariableTarget.Process);
            }

            if (secret is null)
            {
                // New installations redeem the operator-issued one-time enrollment
                // token once, persist the returned per-device secret, then use the
                // normal token endpoint for all routine authentication thereafter.
                secret = await enrollmentBootstrapper.EnrollIfConfiguredAsync(
                    deviceId, cancellationToken);
                secret ??= await credentialStore.GetOrBootstrapAsync(cancellationToken);
            }

            var client = httpClientFactory.CreateClient("GameNetAgentCredentialClient");
            var endpoint = $"{options.Value.ServerBaseUrl.TrimEnd('/')}/api/v1/agent/auth/token";

            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = JsonContent.Create(new AgentTokenRequest(deviceId, secret))
            };
            request.Headers.TryAddWithoutValidation("X-GameNet-Contract", "v1");

            using var response = await client.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                // Do not echo response bodies into exceptions: error bodies may be
                // captured by service diagnostics and must never expose credentials.
                throw new InvalidOperationException(
                    $"Agent token request failed with HTTP {(int)response.StatusCode}.");
            }

            var token = await response.Content.ReadFromJsonAsync<AgentTokenResponse>(
                new JsonSerializerOptions(JsonSerializerDefaults.Web),
                cancellationToken);

            if (token is null || string.IsNullOrWhiteSpace(token.AccessToken))
                throw new InvalidOperationException("Server returned an empty Agent access token.");

            cachedDeviceId = deviceId;
            cachedToken = token.AccessToken;
            expiresAtUtc = token.ExpiresAtUtc;
            return token.AccessToken;
        }
        finally
        {
            gate.Release();
        }
    }

    public void Dispose() => gate.Dispose();
}
