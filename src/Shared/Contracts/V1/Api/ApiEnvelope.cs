namespace GameNet.Shared.Contracts.V1.Api;

public sealed record ApiEnvelope<T>(T Data, string CorrelationId);
