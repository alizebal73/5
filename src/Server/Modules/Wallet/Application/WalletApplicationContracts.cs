using GameNet.Shared.Contracts.V1.Wallet;
using GameNet.Shared.Primitives;

namespace GameNet.Server.Modules.Wallet.Application;

public sealed record WalletActorContext(Guid OperatorId, string CorrelationId, string Source, IReadOnlySet<string> Permissions);
public sealed record WalletCashTopUpCommand(Guid CustomerId, long AmountToCreditToman,
    long AmountReceivedToman, string IdempotencyKey, WalletActorContext Actor);

public interface IWalletService
{
    Task<Result<WalletBalanceResponse>> GetBalanceAsync(WalletActorContext actor, Guid customerId, CancellationToken cancellationToken);
    Task<Result<WalletCashTopUpResponse>> TopUpCashAsync(WalletCashTopUpCommand command, CancellationToken cancellationToken);
}
