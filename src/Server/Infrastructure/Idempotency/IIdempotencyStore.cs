namespace GameNet.Server.Infrastructure.Idempotency;

public sealed record IdempotencyClaim(
    bool Claimed,
    bool Completed,
    bool InFlight,
    string LeaseToken,
    int? StatusCode,
    string? ResponseJson);

public interface IIdempotencyStore
{
    Task<IdempotencyClaim> TryClaimAsync(
        string scope,
        string key,
        string operation,
        TimeSpan leaseDuration,
        TimeSpan retention,
        CancellationToken cancellationToken = default);

    Task CompleteAsync(
        string scope,
        string key,
        string leaseToken,
        int statusCode,
        string responseJson,
        CancellationToken cancellationToken = default);
}
