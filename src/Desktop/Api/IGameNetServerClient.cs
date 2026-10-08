using GameNet.Shared.Contracts.V1.Api;
using GameNet.Shared.Contracts.V1.System;

namespace GameNet.Desktop.Api;

public interface IGameNetServerClient
{
    Task<ApiEnvelope<HealthResponse>> GetHealthAsync(CancellationToken cancellationToken = default);
}
