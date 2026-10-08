using System.Net.Http;
using System.Net.Http.Json;
using GameNet.Shared.Contracts.V1.Api;
using GameNet.Shared.Contracts.V1.System;

namespace GameNet.Desktop.Api;

public sealed class GameNetServerClient(
    HttpClient httpClient): IGameNetServerClient
{
    public async Task<ApiEnvelope<HealthResponse>> GetHealthAsync(CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.GetAsync("/health", cancellationToken);
        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<HealthResponse>>(
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web),
            cancellationToken);

        if (!response.IsSuccessStatusCode || envelope is null)
            throw new InvalidOperationException("SERVER_HEALTH_REQUEST_FAILED");

        return envelope;
    }
}
