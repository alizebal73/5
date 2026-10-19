namespace GameNet.Shared.Contracts.V1.Wallet;

public sealed record WalletBalanceResponse(
    Guid CustomerId,
    long BalanceToman,
    int WalletVersion,
    DateTimeOffset? UpdatedAtUtc);

public sealed record WalletCashTopUpRequest(
    long AmountToCreditToman,
    long AmountReceivedToman,
    int ExpectedCustomerVersion);

public sealed record WalletCashTopUpResponse(
    Guid CustomerId,
    Guid CashReceiptId,
    Guid WalletLedgerEntryId,
    long AmountCreditedToman,
    long AmountReceivedToman,
    long ChangeGivenToman,
    long BalanceAfterToman,
    int WalletVersion,
    DateTimeOffset ReceivedAtUtc);