namespace GameNet.Shared.Contracts.V1.Wallet;

public sealed record WalletBalanceResponse(
    Guid CustomerId, string CustomerCode, string? CustomerName,
    long PurchasedBalanceToman, int Version, DateTimeOffset? UpdatedAtUtc, bool HasWallet);

public sealed record WalletCashTopUpRequest(long AmountToCreditToman, long AmountReceivedToman);

public sealed record WalletCashTopUpResponse(
    Guid CashReceiptId, Guid WalletLedgerEntryId, Guid CustomerId,
    string CustomerCode, string? CustomerName, long AmountCreditedToman,
    long AmountReceivedToman, long ChangeGivenToman, long PurchasedBalanceToman,
    int WalletVersion, DateTimeOffset ReceivedAtUtc);
