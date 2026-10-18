using GameNet.Shared.Primitives;

namespace GameNet.Server.Modules.Billing.Contracts;

public sealed record RecordWalletTopUpCashReceiptCommand(
    Guid CashReceiptId,
    Guid CustomerId,
    long AmountAppliedToman,
    long AmountReceivedToman,
    Guid ReceivedByOperatorId,
    DateTimeOffset ReceivedAtUtc,
    string IdempotencyKey,
    string CorrelationId,
    string Source);

public sealed record WalletCashReceiptResult(
    Guid CashReceiptId,
    Guid CustomerId,
    long AmountAppliedToman,
    long AmountReceivedToman,
    long ChangeGivenToman,
    DateTimeOffset ReceivedAtUtc);

/// <summary>Billing records the physical tender in the Wallet use case's transaction.</summary>
public interface ICashReceiptWriter
{
    Task<Result<WalletCashReceiptResult>> RecordWalletTopUpAsync(
        RecordWalletTopUpCashReceiptCommand command,
        CancellationToken cancellationToken);
}