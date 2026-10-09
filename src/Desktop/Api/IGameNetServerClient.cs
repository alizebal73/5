using GameNet.Shared.Contracts.V1.Api;
using GameNet.Shared.Contracts.V1.System;
using GameNet.Shared.Contracts.V1.Identity;
using GameNet.Shared.Contracts.V1.Stations;

namespace GameNet.Desktop.Api;

public interface IGameNetServerClient
{
    Task<ApiEnvelope<HealthResponse>> GetHealthAsync(CancellationToken cancellationToken = default);
    Task<LoginResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default);
    Task<CurrentOperatorResponse> GetCurrentOperatorAsync(CancellationToken cancellationToken = default);
    Task<ChangeOwnPasswordResponse> ChangeOwnPasswordAsync(ChangeOwnPasswordRequest request, string idempotencyKey, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<StationResponse>> GetStationsAsync(CancellationToken cancellationToken = default);
    Task<StationResponse> CreateStationAsync(CreateStationRequest request, string idempotencyKey, CancellationToken cancellationToken = default);
    Task<StationResponse> RenameStationAsync(Guid id, RenameStationRequest request, string idempotencyKey, CancellationToken cancellationToken = default);
    Task<StationResponse> BindStationAgentAsync(Guid id, BindStationAgentRequest request, string idempotencyKey, CancellationToken cancellationToken = default);
    Task<StationResponse> SetStationStatusAsync(Guid id, SetStationStatusRequest request, string idempotencyKey, CancellationToken cancellationToken = default);
    Task LogoutAsync(CancellationToken cancellationToken = default);
    void ClearSession();
}
