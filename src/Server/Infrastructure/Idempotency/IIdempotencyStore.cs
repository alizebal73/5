namespace GameNet.Server.Infrastructure.Idempotency;

public sealed record IdempotencyClaim(bool Claimed,bool Completed,bool InFlight,string LeaseToken);

public interface IIdempotencyStore
{
    Task<IdempotencyClaim> TryClaimAsync(string scope,string key,string operation,CancellationToken cancellationToken = default);
    Task CompleteAsync(string scope,string key,string leaseToken,int statusCode,string responseJson,CancellationToken cancellationToken = default);
}
