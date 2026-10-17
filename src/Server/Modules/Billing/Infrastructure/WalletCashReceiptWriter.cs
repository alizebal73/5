using GameNet.Server.Modules.Billing.Contracts;
using GameNet.Server.Modules.Billing.Domain;
using GameNet.Server.Persistence;

namespace GameNet.Server.Modules.Billing.Infrastructure;

public sealed class WalletCashReceiptWriter(GameNetDbContext db) : IWalletCashReceiptWriter
{
    public void AppendWalletTopUpReceipt(WalletTopUpCashReceiptDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        db.CashReceipts.Add(CashReceipt.CreateForWalletTopUp(
            draft.CashReceiptId, draft.CustomerId, draft.WalletLedgerEntryId,
            draft.AmountToCreditToman, draft.AmountReceivedToman, draft.ReceivedByOperatorId, draft.ReceivedAtUtc));
    }
}
