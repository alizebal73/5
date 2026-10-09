using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using GameNet.Shared.Contracts.V1.Api;
using GameNet.Shared.Contracts.V1.Identity;
using GameNet.Shared.Contracts.V1.System;
using GameNet.Shared.Contracts.V1.Stations;

namespace GameNet.Desktop.Api;

public sealed class GameNetServerClient(HttpClient httpClient) : IGameNetServerClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private string? accessToken;

    public async Task<ApiEnvelope<HealthResponse>> GetHealthAsync(CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(HttpMethod.Get, "/health", authenticated: false);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<HealthResponse>>(JsonOptions, cancellationToken);
        if (!response.IsSuccessStatusCode || envelope is null)
            throw new GameNetApiException("server.health_request_failed", (int)response.StatusCode);
        return envelope;
    }

    public async Task<LoginResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        using var message = CreateRequest(HttpMethod.Post, "/api/v1/auth/login", authenticated: false);
        message.Content = JsonContent.Create(request, options: JsonOptions);
        using var response = await httpClient.SendAsync(message, cancellationToken);
        var result = await ReadPayloadAsync<LoginResponse>(response, cancellationToken);
        accessToken = result.AccessToken;
        return result;
    }

    public async Task<CurrentOperatorResponse> GetCurrentOperatorAsync(CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(HttpMethod.Get, "/api/v1/auth/me", authenticated: true);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        return await ReadPayloadAsync<CurrentOperatorResponse>(response, cancellationToken);
    }

    public async Task<ChangeOwnPasswordResponse> ChangeOwnPasswordAsync(
        ChangeOwnPasswordRequest body, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);
        if (idempotencyKey.Length > 200) throw new ArgumentOutOfRangeException(nameof(idempotencyKey));
        using var request = CreateRequest(HttpMethod.Put, "/api/v1/identity/me/password", authenticated: true);
        request.Headers.TryAddWithoutValidation(ApiHeaders.IdempotencyKey, idempotencyKey.Trim());
        request.Content = JsonContent.Create(body, options: JsonOptions);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        return await ReadPayloadAsync<ChangeOwnPasswordResponse>(response, cancellationToken);
    }

    public async Task<IReadOnlyList<StationResponse>> GetStationsAsync(CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(HttpMethod.Get, "/api/v1/stations", authenticated: true);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        var stations = await ReadPayloadAsync<StationResponse[]>(response, cancellationToken);
        return stations ?? Array.Empty<StationResponse>();
    }

    public Task<StationResponse> CreateStationAsync(CreateStationRequest request, string idempotencyKey, CancellationToken cancellationToken = default) =>
        SendStationMutationAsync(HttpMethod.Post, "/api/v1/stations", request, idempotencyKey, cancellationToken);

    public Task<StationResponse> RenameStationAsync(Guid id, RenameStationRequest request, string idempotencyKey, CancellationToken cancellationToken = default) =>
        SendStationMutationAsync(HttpMethod.Put, $"/api/v1/stations/{id:D}", request, idempotencyKey, cancellationToken);

    public Task<StationResponse> BindStationAgentAsync(Guid id, BindStationAgentRequest request, string idempotencyKey, CancellationToken cancellationToken = default) =>
        SendStationMutationAsync(HttpMethod.Put, $"/api/v1/stations/{id:D}/agent", request, idempotencyKey, cancellationToken);

    public Task<StationResponse> SetStationStatusAsync(Guid id, SetStationStatusRequest request, string idempotencyKey, CancellationToken cancellationToken = default) =>
        SendStationMutationAsync(HttpMethod.Put, $"/api/v1/stations/{id:D}/status", request, idempotencyKey, cancellationToken);

    private async Task<StationResponse> SendStationMutationAsync<TRequest>(
        HttpMethod method, string path, TRequest body, string idempotencyKey, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);
        if (idempotencyKey.Length > 200) throw new ArgumentOutOfRangeException(nameof(idempotencyKey));
        using var request = CreateRequest(method, path, authenticated: true);
        request.Headers.TryAddWithoutValidation(ApiHeaders.IdempotencyKey, idempotencyKey.Trim());
        request.Content = JsonContent.Create(body, options: JsonOptions);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        return await ReadPayloadAsync<StationResponse>(response, cancellationToken);
    }

    public async Task LogoutAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var request = CreateRequest(HttpMethod.Post, "/api/v1/auth/logout", authenticated: true);
            using var response = await httpClient.SendAsync(request, cancellationToken);
            _ = await ReadPayloadAsync<LogoutResponse>(response, cancellationToken);
        }
        finally
        {
            ClearSession();
        }
    }

    public void ClearSession() => accessToken = null;

    private HttpRequestMessage CreateRequest(HttpMethod method, string uri, bool authenticated)
    {
        var requiresSecureTransport = authenticated ||
                                      uri.StartsWith("/api/v1/auth", StringComparison.Ordinal) ||
                                      uri.StartsWith("/api/v1/bootstrap/admin", StringComparison.Ordinal);
        if (requiresSecureTransport && httpClient.BaseAddress is { } baseAddress &&
            baseAddress.Scheme != Uri.UriSchemeHttps && !baseAddress.IsLoopback)
        {
            throw new GameNetApiException("security.https_required", (int)System.Net.HttpStatusCode.UpgradeRequired);
        }

        var request = new HttpRequestMessage(method, uri);
        if (!httpClient.DefaultRequestHeaders.Contains(ApiHeaders.ContractVersion))
            request.Headers.TryAddWithoutValidation(ApiHeaders.ContractVersion, ContractVersions.V1);
        if (authenticated && !string.IsNullOrWhiteSpace(accessToken))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return request;
    }

    private static async Task<T> ReadPayloadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            ApiFailure? failure = null;
            try
            {
                failure = await response.Content.ReadFromJsonAsync<ApiFailure>(JsonOptions, cancellationToken);
            }
            catch (JsonException)
            {
                // Use a stable fallback error when the Server returned a non-contract response.
            }

            throw new GameNetApiException(failure?.Error.Code ?? "server.request_failed", (int)response.StatusCode);
        }

        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<T>>(JsonOptions, cancellationToken);
        if (envelope is null)
            throw new GameNetApiException("server.response_invalid", (int)response.StatusCode);
        return envelope.Data;
    }
}

public sealed class GameNetApiException(string code, int statusCode) : Exception(code)
{
    public string Code { get; } = code;
    public int StatusCode { get; } = statusCode;
}
