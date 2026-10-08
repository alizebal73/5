using GameNet.Shared.Contracts.V1.Api;
using GameNet.Shared.Contracts.V1.System;
using GameNet.Shared.Contracts.V1.Identity;

namespace GameNet.Desktop.Api;

public interface IGameNetServerClient
{
    Task<ApiEnvelope<HealthResponse>> GetHealthAsync(CancellationToken cancellationToken = default);
    Task<LoginResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default);
    Task<CurrentOperatorResponse> GetCurrentOperatorAsync(CancellationToken cancellationToken = default);
    Task LogoutAsync(CancellationToken cancellationToken = default);
    void ClearSession();
}
