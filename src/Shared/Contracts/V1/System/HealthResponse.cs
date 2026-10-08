namespace GameNet.Shared.Contracts.V1.System;

public sealed record HealthResponse(string Service,string Version,string Status,string Readiness,string CorrelationId);
