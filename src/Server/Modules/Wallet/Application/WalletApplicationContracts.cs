using GameNet.Shared.Contracts.V1.Wallet;
using GameNet.Shared.Primitives;

namespace GameNet.Server.Modules.Wallet.Application;

public sealed record WalletActorContext(
    Guid OperatorId,
    string CorrelationId,
    string Source,
    IReadOnlySet<string> Permissions);

public sealed record CashTopUpWalletCommand(
    Guid CustomerId,
    long AmountToCreditToman,
    long AmountReceivedToman,
    int ExpectedCustomerVersion,
    string IdempotencyKey,
    WalletActorContext Actor);

public interface IWalletService
{
    Task<Result<WalletBalanceResponse>> GetBalanceAsync(Guid customerId, WalletActorContext actor, CancellationToken cancellationToken);
    Task<Result<WalletCashTopUpResponse>> CashTopUpAsync(CashTopUpWalletCommand command, CancellationToken cancellationToken);
}