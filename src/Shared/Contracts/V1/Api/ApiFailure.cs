namespace GameNet.Shared.Contracts.V1.Api;

public sealed record ApiFailure(ApiError Error, string CorrelationId);
public sealed record ApiError(string Code, string Message);
