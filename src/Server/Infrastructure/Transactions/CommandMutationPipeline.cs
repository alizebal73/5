using System.Text.Json;
using GameNet.Server.Infrastructure.Idempotency;
using GameNet.Shared.Primitives;

namespace GameNet.Server.Infrastructure.Transactions;

/// <summary>
/// Shared execution boundary for mutating Server commands. The caller retains the
/// operation-specific authorization, validation, and Audit record; this pipeline owns
/// stable idempotency claim/replay/completion and the surrounding database transaction.
/// A caller supplies its existing request fingerprint so extraction does not silently
/// change the replay contract of legacy operations.
/// </summary>
public sealed class CommandMutationPipeline(
    ITransactionCoordinator transactions,
    IIdempotencyStore idempotency)
{
    public async Task<Result<T>> ExecuteAsync<T>(
        string scope,
        string operation,
        string key,
        string requestHash,
        int statusCode,
        TimeSpan leaseDuration,
        TimeSpan retention,
        Func<CancellationToken, Task<Result<T>>> action,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > 200)
            return Fail<T>("idempotency.required", "A valid Idempotency-Key is required.");

        try
        {
            return await transactions.ExecuteAsync(async token =>
            {
                var normalizedKey = key.Trim();
                var claim = await idempotency.TryClaimAsync(
                    scope, normalizedKey, operation, requestHash, leaseDuration, retention, token);

                if (claim.Completed && claim.ResponseJson is not null)
                {
                    var replay = JsonSerializer.Deserialize<T>(claim.ResponseJson);
                    return replay is null
                        ? Fail<T>("idempotency.invalid_replay", "Saved operation response is invalid.")
                        : Result<T>.Success(replay);
                }

                if (claim.InFlight)
                    throw new AbortMutation(new Error("idempotency.in_flight", "Operation is already in progress."));

                if (!claim.Claimed || string.IsNullOrWhiteSpace(claim.LeaseToken))
                    throw new AbortMutation(new Error("idempotency.unavailable", "Operation could not be claimed."));

                var result = await action(token);
                if (!result.IsSuccess)
                    throw new AbortMutation(result.Error);

                await idempotency.CompleteAsync(
                    scope, normalizedKey, claim.LeaseToken, statusCode, JsonSerializer.Serialize(result.Value), token);

                return result;
            }, cancellationToken);
        }
        catch (AbortMutation ex)
        {
            return Fail<T>(ex.Error.Code, ex.Error.Message);
        }
        catch (IdempotencyKeyConflictException)
        {
            return Fail<T>("idempotency.key_reused", "Idempotency-Key was used for a different request.");
        }
    }

    private static Result<T> Fail<T>(string code, string message) =>
        Result<T>.Failure(new Error(code, message));

    private sealed class AbortMutation(Error error) : Exception(error.Code)
    {
        public Error Error { get; } = error;
    }
}
