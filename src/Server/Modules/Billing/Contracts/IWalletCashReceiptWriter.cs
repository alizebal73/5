namespace GameNet.Server.Modules.Billing.Contracts;

public sealed record WalletTopUpCashReceiptDraft(
    Guid CashReceiptId, Guid CustomerId, Guid WalletLedgerEntryId,
    long AmountToCreditToman, long AmountReceivedToman, Guid ReceivedByOperatorId, DateTimeOffset ReceivedAtUtc);

public interface IWalletCashReceiptWriter
{
    void AppendWalletTopUpReceipt(WalletTopUpCashReceiptDraft draft);
}
