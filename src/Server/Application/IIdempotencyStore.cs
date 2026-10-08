namespace GameNet.Server.Application;

public enum IdempotencyClaimState
{
    Claimed = 1,
    Completed = 2,
    InFlight = 3,
    Conflict = 4
}

public sealed record IdempotencyClaim(
    IdempotencyClaimState State,
    string LeaseToken,
    int? StatusCode,
    string? ResponseJson);

public interface IIdempotencyStore
{
    Task<IdempotencyClaim> TryClaimAsync(
        string scope,
        string key,
        string operation,
        string requestHash,
        CancellationToken cancellationToken = default);

    Task CompleteAsync(
        string scope,
        string key,
        string leaseToken,
        int statusCode,
        string responseJson,
        CancellationToken cancellationToken = default);
}
