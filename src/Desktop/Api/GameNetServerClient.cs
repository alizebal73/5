using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using GameNet.Shared.Contracts.V1.Api;
using GameNet.Shared.Contracts.V1.Billing;
using GameNet.Shared.Contracts.V1.Customers;
using GameNet.Shared.Contracts.V1.Identity;
using GameNet.Shared.Contracts.V1.System;
using GameNet.Shared.Contracts.V1.Stations;
using GameNet.Shared.Contracts.V1.Protocol;
using GameNet.Shared.Contracts.V1.Sessions;
using GameNet.Shared.Contracts.V1.Tariffs;
using GameNet.Shared.Contracts.V1.Wallet;

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

    public async Task<IReadOnlyList<CustomerResponse>> GetCustomersAsync(string? query = null, bool includeInactive = false, CancellationToken cancellationToken = default)
    {
        var queryParts = new List<string>();
        if (!string.IsNullOrWhiteSpace(query)) queryParts.Add("query=" + Uri.EscapeDataString(query.Trim()));
        queryParts.Add("includeInactive=" + includeInactive.ToString().ToLowerInvariant());
        queryParts.Add("take=100");
        using var request = CreateRequest(HttpMethod.Get, "/api/v1/customers?" + string.Join("&", queryParts), authenticated: true);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        var customers = await ReadPayloadAsync<CustomerResponse[]>(response, cancellationToken);
        return customers ?? Array.Empty<CustomerResponse>();
    }

    public Task<CustomerResponse> CreateCustomerAsync(CreateCustomerRequest body, string idempotencyKey, CancellationToken cancellationToken = default) =>
        SendMutationAsync<CreateCustomerRequest, CustomerResponse>(HttpMethod.Post, "/api/v1/customers", body, idempotencyKey, cancellationToken);

    public Task<CustomerResponse> UpdateCustomerProfileAsync(Guid id, UpdateCustomerRequest body, string idempotencyKey, CancellationToken cancellationToken = default) =>
        SendMutationAsync<UpdateCustomerRequest, CustomerResponse>(HttpMethod.Put, $"/api/v1/customers/{id:D}", body, idempotencyKey, cancellationToken);

    public Task<CustomerResponse> SetCustomerStatusAsync(Guid id, SetCustomerStatusRequest body, string idempotencyKey, CancellationToken cancellationToken = default) =>
        SendMutationAsync<SetCustomerStatusRequest, CustomerResponse>(HttpMethod.Put, $"/api/v1/customers/{id:D}/status", body, idempotencyKey, cancellationToken);

    public async Task<WalletBalanceResponse> GetWalletBalanceAsync(Guid customerId, CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(HttpMethod.Get, $"/api/v1/wallet/customers/{customerId:D}/balance", authenticated: true);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        return await ReadPayloadAsync<WalletBalanceResponse>(response, cancellationToken);
    }

    public Task<WalletCashTopUpResponse> TopUpWalletCashAsync(
        Guid customerId, WalletCashTopUpRequest body, string idempotencyKey, CancellationToken cancellationToken = default) =>
        SendMutationAsync<WalletCashTopUpRequest, WalletCashTopUpResponse>(
            HttpMethod.Post, $"/api/v1/wallet/customers/{customerId:D}/cash-top-ups", body, idempotencyKey, cancellationToken);

    public Task<StationResponse> CreateStationAsync(CreateStationRequest request, string idempotencyKey, CancellationToken cancellationToken = default) =>
        SendStationMutationAsync(HttpMethod.Post, "/api/v1/stations", request, idempotencyKey, cancellationToken);

    public Task<StationResponse> RenameStationAsync(Guid id, RenameStationRequest request, string idempotencyKey, CancellationToken cancellationToken = default) =>
        SendStationMutationAsync(HttpMethod.Put, $"/api/v1/stations/{id:D}", request, idempotencyKey, cancellationToken);

    public Task<StationResponse> BindStationAgentAsync(Guid id, BindStationAgentRequest request, string idempotencyKey, CancellationToken cancellationToken = default) =>
        SendStationMutationAsync(HttpMethod.Put, $"/api/v1/stations/{id:D}/agent", request, idempotencyKey, cancellationToken);

    public Task<StationResponse> SetStationStatusAsync(Guid id, SetStationStatusRequest request, string idempotencyKey, CancellationToken cancellationToken = default) =>
        SendStationMutationAsync(HttpMethod.Put, $"/api/v1/stations/{id:D}/status", request, idempotencyKey, cancellationToken);

    public async Task<AgentCommandAcknowledgement> ProbeAgentHealthAsync(Guid stationId, CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(HttpMethod.Post, $"/api/v1/stations/{stationId:D}/agent/health-probe", authenticated: true);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        return await ReadPayloadAsync<AgentCommandAcknowledgement>(response, cancellationToken);
    }

    public async Task<IReadOnlyList<TariffResponse>> GetTariffsAsync(
        GameNet.Shared.Contracts.V1.Stations.StationTypeContract? stationType = null,
        CancellationToken cancellationToken = default)
    {
        var path = stationType.HasValue
            ? $"/api/v1/tariffs?stationType={Uri.EscapeDataString(stationType.Value.ToString())}"
            : "/api/v1/tariffs";
        using var request = CreateRequest(HttpMethod.Get, path, authenticated: true);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        var tariffs = await ReadPayloadAsync<TariffResponse[]>(response, cancellationToken);
        return tariffs ?? Array.Empty<TariffResponse>();
    }

    public Task<TariffResponse> CreateTariffAsync(
        CreateTariffRequest body, string idempotencyKey, CancellationToken cancellationToken = default) =>
        SendMutationAsync<CreateTariffRequest, TariffResponse>(
            HttpMethod.Post, "/api/v1/tariffs", body, idempotencyKey, cancellationToken);

    public async Task<IReadOnlyList<SessionResponse>> GetActiveSessionsAsync(
        Guid? stationId = null, CancellationToken cancellationToken = default)
    {
        var path = stationId.HasValue
            ? $"/api/v1/sessions/active?stationId={stationId.Value:D}"
            : "/api/v1/sessions/active";
        using var request = CreateRequest(HttpMethod.Get, path, authenticated: true);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        var sessions = await ReadPayloadAsync<SessionResponse[]>(response, cancellationToken);
        return sessions ?? Array.Empty<SessionResponse>();
    }

    public Task<SessionResponse> StartSessionAsync(
        StartSessionRequest body, string idempotencyKey, CancellationToken cancellationToken = default) =>
        SendMutationAsync<StartSessionRequest, SessionResponse>(
            HttpMethod.Post, "/api/v1/sessions", body, idempotencyKey, cancellationToken);

    public Task<InvoicePreviewResponse> EndSessionAsync(
        Guid id, EndSessionRequest body, string idempotencyKey, CancellationToken cancellationToken = default) =>
        SendMutationAsync<EndSessionRequest, InvoicePreviewResponse>(
            HttpMethod.Post, $"/api/v1/sessions/{id:D}/end", body, idempotencyKey, cancellationToken);

    public Task<InvoicePreviewResponse> ReconcileSessionTimingAsync(
        Guid id, ReconcileSessionTimingRequest body, string idempotencyKey, CancellationToken cancellationToken = default) =>
        SendMutationAsync<ReconcileSessionTimingRequest, InvoicePreviewResponse>(
            HttpMethod.Post, $"/api/v1/sessions/{id:D}/timing-recovery", body, idempotencyKey, cancellationToken);

    public async Task<IReadOnlyList<InvoicePreviewResponse>> GetPendingInvoicesAsync(
        CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(HttpMethod.Get, "/api/v1/billing/pending", authenticated: true);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        var invoices = await ReadPayloadAsync<InvoicePreviewResponse[]>(response, cancellationToken);
        return invoices ?? Array.Empty<InvoicePreviewResponse>();
    }

    public async Task<CashCheckoutOutcomeResponse> GetCashCheckoutOutcomeAsync(
        Guid invoiceId, CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(HttpMethod.Get, $"/api/v1/billing/invoices/{invoiceId:D}/outcome", authenticated: true);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        return await ReadPayloadAsync<CashCheckoutOutcomeResponse>(response, cancellationToken);
    }

    public async Task<IReadOnlyList<CashCheckoutOutcomeResponse>> GetRecentCashCheckoutsAsync(
        CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(HttpMethod.Get, "/api/v1/billing/recent-cash-checkouts", authenticated: true);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        var outcomes = await ReadPayloadAsync<CashCheckoutOutcomeResponse[]>(response, cancellationToken);
        return outcomes ?? Array.Empty<CashCheckoutOutcomeResponse>();
    }

    public Task<CashCheckoutResponse> CheckoutCashAsync(
        Guid invoiceId, CashCheckoutRequest body, string idempotencyKey, CancellationToken cancellationToken = default) =>
        SendMutationAsync<CashCheckoutRequest, CashCheckoutResponse>(
            HttpMethod.Post, $"/api/v1/billing/invoices/{invoiceId:D}/cash", body, idempotencyKey, cancellationToken);

    public Task<CashSaleReversalResponse> ReverseCashSaleAsync(
        Guid invoiceId, CashSaleReversalRequest body, string idempotencyKey, CancellationToken cancellationToken = default) =>
        SendMutationAsync<CashSaleReversalRequest, CashSaleReversalResponse>(
            HttpMethod.Post, $"/api/v1/billing/invoices/{invoiceId:D}/cash-reversal", body, idempotencyKey, cancellationToken);

    public Task<CashTenderCorrectionResponse> CorrectCashTenderAsync(
        Guid invoiceId, CashTenderCorrectionRequest body, string idempotencyKey, CancellationToken cancellationToken = default) =>
        SendMutationAsync<CashTenderCorrectionRequest, CashTenderCorrectionResponse>(
            HttpMethod.Post, $"/api/v1/billing/invoices/{invoiceId:D}/cash-tender-correction", body, idempotencyKey, cancellationToken);

    private async Task<TResponse> SendMutationAsync<TRequest, TResponse>(
        HttpMethod method, string path, TRequest body, string idempotencyKey, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);
        if (idempotencyKey.Length > 200) throw new ArgumentOutOfRangeException(nameof(idempotencyKey));
        using var request = CreateRequest(method, path, authenticated: true);
        request.Headers.TryAddWithoutValidation(ApiHeaders.IdempotencyKey, idempotencyKey.Trim());
        request.Content = JsonContent.Create(body, options: JsonOptions);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        return await ReadPayloadAsync<TResponse>(response, cancellationToken);
    }

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
