using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using GameNet.Agent.Identity;
using GameNet.Shared.Contracts.V1.Api;
using GameNet.Shared.Contracts.V1.Customers;
using GameNet.Shared.Contracts.V1.Protocol;
using GameNet.Agent.Identity;
using Microsoft.Extensions.Options;

namespace GameNet.Agent.Transport;

/// <summary>
/// Calls the Server with the per-device Agent bearer token. The PC identity is therefore
/// established by Agent credentials instead of a StationId supplied by the customer UI.
/// </summary>
public sealed class AgentCustomerSessionGateway(
    IHttpClientFactory clients,
    IAgentIdentityStore identityStore,
    IAgentAccessTokenProvider tokenProvider,
    IOptions<AgentTransportOptions> options) : IAgentCustomerSessionGateway
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<CustomerClientEntryResponse> StartAsync(
        CustomerClientEntryRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Username) ||
            string.IsNullOrWhiteSpace(request.Password) ||
            string.IsNullOrWhiteSpace(request.IdempotencyKey) ||
            request.IdempotencyKey.Length > 200)
            return Rejected("client.request_invalid", "Enter your customer username and password.");

        var identity = await identityStore.GetOrCreateAsync(cancellationToken);
        var accessToken = await tokenProvider.GetAccessTokenAsync(identity.DeviceId, cancellationToken);
        var client = clients.CreateClient("GameNetAgentCredentialClient");

        using var message = new HttpRequestMessage(
            HttpMethod.Post,
            $"{options.Value.ServerBaseUrl.TrimEnd('/')}/api/v1/agent/customer-session/start")
        {
            Content = JsonContent.Create(request, options: JsonOptions)
        };
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        message.Headers.TryAddWithoutValidation(ApiHeaders.ContractVersion, "v1");
        message.Headers.TryAddWithoutValidation(ApiHeaders.IdempotencyKey, request.IdempotencyKey);

        using var response = await client.SendAsync(message, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var failure = await response.Content.ReadFromJsonAsync<ApiFailure>(JsonOptions, cancellationToken);
            return Rejected(
                failure?.Error.Code ?? $"server.http_{(int)response.StatusCode}",
                failure?.Error.Message ?? "The Server rejected customer entry.");
        }

        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<CustomerClientEntryResponse>>(
            JsonOptions, cancellationToken);
        if (envelope?.Data is not { Succeeded: true, CustomerId: not null, SessionId: not null,
            CustomerAccessToken: not null, PrepaidReservedAmountToman: > 0, PrepaidBudgetTicks: > 0 } data)
            return Rejected("agent.server_response_invalid", "The Server returned an incomplete prepaid Session result.");

        return data;
    }

    private static CustomerClientEntryResponse Rejected(string code, string message) =>
        new(false, null, null, null, null, null, null, null, code, message);
}
