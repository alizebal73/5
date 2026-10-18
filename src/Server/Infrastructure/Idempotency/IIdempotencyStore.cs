namespace GameNet.Server.Infrastructure.Idempotency;

public sealed record IdempotencyClaim(bool Claimed, bool Completed, bool InFlight, string LeaseToken, int? StatusCode, string? ResponseJson);
public sealed class IdempotencyKeyConflictException() : InvalidOperationException("IDEMPOTENCY_KEY_REUSED_FOR_DIFFERENT_REQUEST");

public interface IIdempotencyStore
{
    Task<IdempotencyClaim> TryClaimAsync(string scope, string key, string operation, string requestHash,
        TimeSpan leaseDuration, TimeSpan retention, CancellationToken cancellationToken = default);
    Task CompleteAsync(string scope, string key, string leaseToken, int statusCode, string responseJson,
        CancellationToken cancellationToken = default);
}
