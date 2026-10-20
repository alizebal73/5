using GameNet.Shared.Primitives;

namespace GameNet.Server.Modules.Wallet.Contracts;

public sealed record WalletSessionReservation(
    Guid CustomerId,
    Guid SessionId,
    long ReservedAmountToman,
    int WalletVersionAfter,
    DateTimeOffset ReservedAtUtc);

public sealed record WalletSessionSettlement(
    Guid CustomerId,
    Guid SessionId,
    long ChargedAmountToman,
    long ReleasedAmountToman,
    long BalanceAfterToman,
    int WalletVersionAfter,
    DateTimeOffset SettledAtUtc);

/// <summary>
/// Session funding operations participate in the caller's transaction. They never own or
/// commit a transaction; Session, Wallet projection and append-only ledger movements commit together.
/// </summary>
public interface IWalletSessionFunding
{
    Task<Result<WalletSessionReservation>> ReserveForSessionAsync(
        Guid customerId, Guid sessionId, Guid actorId, string actorType,
        string correlationId, string source, string idempotencyKey,
        CancellationToken cancellationToken);

    Task<WalletSessionReservation?> FindActiveReservationAsync(
        Guid sessionId, CancellationToken cancellationToken);

    Task<Result<WalletSessionSettlement>> SettleSessionAsync(
        Guid customerId, Guid sessionId, long actualChargeToman, Guid actorId, string actorType,
        string correlationId, string source, string idempotencyKey,
        CancellationToken cancellationToken);
}
