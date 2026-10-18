namespace GameNet.Shared.Contracts.V1.System;

public sealed record HealthResponse(
    string Service,
    string Version,
    string Status,
    string Readiness,
    string CorrelationId);

public static class HealthStatuses
{
    public const string Healthy = "Healthy";
    public const string Unhealthy = "Unhealthy";
    public const string Ready = "Ready";
    public const string NotReady = "NotReady";
}
