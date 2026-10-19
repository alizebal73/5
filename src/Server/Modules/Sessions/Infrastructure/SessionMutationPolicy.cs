namespace GameNet.Server.Modules.Sessions.Infrastructure;

/// <summary>
/// Idempotency timing shared by all mutating Session commands. Changing these values
/// changes replay behavior for every Session operation, so they live in one place.
/// </summary>
internal static class SessionMutationPolicy
{
    public static readonly TimeSpan IdempotencyLease = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan IdempotencyRetention = TimeSpan.FromDays(1);
}
