using GameNet.Shared.Contracts.V1.Api;
using GameNet.Shared.Contracts.V1.Billing;
using GameNet.Shared.Contracts.V1.Customers;
using GameNet.Shared.Contracts.V1.System;
using GameNet.Shared.Contracts.V1.Identity;
using GameNet.Shared.Contracts.V1.Stations;
using GameNet.Shared.Contracts.V1.Protocol;
using GameNet.Shared.Contracts.V1.Sessions;
using GameNet.Shared.Contracts.V1.Tariffs;
using GameNet.Shared.Contracts.V1.Wallet;

namespace GameNet.Desktop.Api;

public interface IGameNetServerClient
{
    Task<ApiEnvelope<HealthResponse>> GetHealthAsync(CancellationToken cancellationToken = default);
    Task<LoginResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default);
    Task<CurrentOperatorResponse> GetCurrentOperatorAsync(CancellationToken cancellationToken = default);
    Task<ChangeOwnPasswordResponse> ChangeOwnPasswordAsync(ChangeOwnPasswordRequest request, string idempotencyKey, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<StationResponse>> GetStationsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CustomerResponse>> GetCustomersAsync(string? query = null, bool includeInactive = false, CancellationToken cancellationToken = default);
    Task<WalletBalanceResponse> GetWalletBalanceAsync(Guid customerId, CancellationToken cancellationToken = default);
    Task<WalletCashTopUpResponse> CashTopUpWalletAsync(Guid customerId, WalletCashTopUpRequest request, string idempotencyKey, CancellationToken cancellationToken = default);
    Task<CustomerResponse> CreateCustomerAsync(CreateCustomerRequest request, string idempotencyKey, CancellationToken cancellationToken = default);
    Task<CustomerResponse> UpdateCustomerProfileAsync(Guid id, UpdateCustomerRequest request, string idempotencyKey, CancellationToken cancellationToken = default);
    Task<CustomerResponse> SetCustomerStatusAsync(Guid id, SetCustomerStatusRequest request, string idempotencyKey, CancellationToken cancellationToken = default);
    Task<SetCustomerCredentialsResponse> SetCustomerCredentialsAsync(Guid id, SetCustomerCredentialsRequest request, string idempotencyKey, CancellationToken cancellationToken = default);
    Task<StationResponse> CreateStationAsync(CreateStationRequest request, string idempotencyKey, CancellationToken cancellationToken = default);
    Task<StationResponse> RenameStationAsync(Guid id, RenameStationRequest request, string idempotencyKey, CancellationToken cancellationToken = default);
    Task<StationResponse> BindStationAgentAsync(Guid id, BindStationAgentRequest request, string idempotencyKey, CancellationToken cancellationToken = default);
    Task<StationResponse> SetStationStatusAsync(Guid id, SetStationStatusRequest request, string idempotencyKey, CancellationToken cancellationToken = default);
    Task<AgentCommandAcknowledgement> ProbeAgentHealthAsync(Guid stationId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TariffResponse>> GetTariffsAsync(GameNet.Shared.Contracts.V1.Stations.StationTypeContract? stationType = null, CancellationToken cancellationToken = default);
    Task<TariffResponse> CreateTariffAsync(CreateTariffRequest request, string idempotencyKey, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SessionResponse>> GetActiveSessionsAsync(Guid? stationId = null, CancellationToken cancellationToken = default);
    Task<SessionResponse> StartSessionAsync(StartSessionRequest request, string idempotencyKey, CancellationToken cancellationToken = default);
    Task<SessionResponse> StartPrepaidSessionAsync(
        StartPrepaidSessionRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken = default) =>
        StartSessionAsync(new StartSessionRequest(request.StationId, Guid.Empty, request.CustomerId),
            idempotencyKey, cancellationToken);
    Task<SessionResponse> PauseSessionAsync(Guid id, SessionLifecycleRequest request, string idempotencyKey, CancellationToken cancellationToken = default) =>
        Task.FromException<SessionResponse>(new NotSupportedException("Session pause is not supported by this client."));
    Task<SessionResponse> ResumeSessionAsync(Guid id, SessionLifecycleRequest request, string idempotencyKey, CancellationToken cancellationToken = default) =>
        Task.FromException<SessionResponse>(new NotSupportedException("Session resume is not supported by this client."));
    Task<InvoicePreviewResponse> EndSessionAsync(Guid id, EndSessionRequest request, string idempotencyKey, CancellationToken cancellationToken = default);
    Task<InvoicePreviewResponse> ReconcileSessionTimingAsync(Guid id, ReconcileSessionTimingRequest request, string idempotencyKey, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<InvoicePreviewResponse>> GetPendingInvoicesAsync(CancellationToken cancellationToken = default);
    Task<CashCheckoutOutcomeResponse> GetCashCheckoutOutcomeAsync(Guid invoiceId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CashCheckoutOutcomeResponse>> GetRecentCashCheckoutsAsync(CancellationToken cancellationToken = default);
    Task<CashCheckoutResponse> CheckoutCashAsync(Guid invoiceId, CashCheckoutRequest request, string idempotencyKey, CancellationToken cancellationToken = default);
    Task<CashSaleReversalResponse> ReverseCashSaleAsync(Guid invoiceId, CashSaleReversalRequest request, string idempotencyKey, CancellationToken cancellationToken = default);
    Task<CashTenderCorrectionResponse> CorrectCashTenderAsync(Guid invoiceId, CashTenderCorrectionRequest request, string idempotencyKey, CancellationToken cancellationToken = default);
    Task LogoutAsync(CancellationToken cancellationToken = default);
    void ClearSession();
}
