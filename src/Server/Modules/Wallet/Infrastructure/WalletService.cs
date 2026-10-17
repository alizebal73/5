using System.Text.Json;
using GameNet.Server.Infrastructure.Audit;
using GameNet.Server.Infrastructure.Idempotency;
using GameNet.Server.Infrastructure.Transactions;
using GameNet.Server.Modules.Billing.Contracts;
using GameNet.Server.Modules.Wallet.Application;
using GameNet.Server.Modules.Wallet.Contracts;
using GameNet.Server.Modules.Wallet.Domain;
using GameNet.Server.Persistence;
using GameNet.Shared.Contracts.V1.Customers;
using GameNet.Shared.Contracts.V1.Security;
using GameNet.Shared.Contracts.V1.Wallet;
using GameNet.Shared.Primitives;
using Microsoft.EntityFrameworkCore;

namespace GameNet.Server.Modules.Wallet.Infrastructure;

public sealed class WalletService(
    GameNetDbContext db,
    CommandMutationPipeline mutations,
    IRequestFingerprint fingerprint,
    IWalletCustomerAccess customers,
    IWalletCashReceiptWriter cashReceipts,
    IAuditWriter audit,
    IGameClock clock) : IWalletService
{
    private static readonly TimeSpan Lease = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan Retention = TimeSpan.FromDays(1);

    public async Task<Result<WalletBalanceResponse>> GetBalanceAsync(
        WalletActorContext actor, Guid customerId, CancellationToken cancellationToken)
    {
        if (!actor.Permissions.Contains(Permissions.WalletWrite))
            return Fail<WalletBalanceResponse>("wallet.forbidden", "Not allowed to read Wallet balances.");
        var customer = await customers.FindForWalletAsync(customerId, cancellationToken);
        var validation = ValidateCustomer(customer);
        if (validation is not null)
            return Result<WalletBalanceResponse>.Failure(validation);
        var account = await db.WalletAccounts.AsNoTracking()
            .SingleOrDefaultAsync(x => x.CustomerId == customerId, cancellationToken);
        return Result<WalletBalanceResponse>.Success(new WalletBalanceResponse(
            customer!.Id, customer.Code, customer.DisplayName,
            account?.PurchasedBalanceToman ?? 0, account?.Version ?? 0,
            account?.UpdatedAtUtc, account is not null));
    }

    public Task<Result<WalletCashTopUpResponse>> TopUpCashAsync(
        WalletCashTopUpCommand command, CancellationToken cancellationToken)
    {
        if (!command.Actor.Permissions.Contains(Permissions.WalletWrite))
            return Task.FromResult(Fail<WalletCashTopUpResponse>("wallet.forbidden", "Not allowed to top up Wallets."));
        if (command.CustomerId == Guid.Empty)
            return Task.FromResult(Fail<WalletCashTopUpResponse>("wallet.customer_not_found", "Customer was not found."));
        if (command.AmountToCreditToman <= 0)
            return Task.FromResult(Fail<WalletCashTopUpResponse>("wallet.amount_invalid", "Amount to credit must be positive."));
        if (command.AmountReceivedToman < command.AmountToCreditToman)
            return Task.FromResult(Fail<WalletCashTopUpResponse>("wallet.cash_insufficient", "Received cash must be at least the amount to credit."));
        try { _ = checked(command.AmountReceivedToman - command.AmountToCreditToman); }
        catch (OverflowException)
        {
            return Task.FromResult(Fail<WalletCashTopUpResponse>("wallet.amount_overflow", "Cash/change amount is outside the supported range."));
        }

        var payload = new { command.CustomerId, command.AmountToCreditToman, command.AmountReceivedToman };
        var scope = $"wallet.cash-top-up:{command.Actor.OperatorId:D}";
        return mutations.ExecuteAsync(scope, "wallet.cash-top-up", command.IdempotencyKey,
            fingerprint.Compute(payload), 201, Lease, Retention,
            async token =>
            {
                var customer = await customers.FindForWalletAsync(command.CustomerId, token);
                var invalidCustomer = ValidateCustomer(customer);
                if (invalidCustomer is not null)
                    return Result<WalletCashTopUpResponse>.Failure(invalidCustomer);

                var now = clock.UtcNow;
                // First top-ups use an upsert so concurrent account creation is race-safe.
                // All following read/credit/receipt/ledger/Audit operations share the existing
                // Serializable mutation transaction and its bounded retry behavior.
                await db.Database.ExecuteSqlInterpolatedAsync(
                    $@"INSERT INTO wallet_accounts (customer_id, purchased_balance_toman, version, updated_at_utc)
                       VALUES ({command.CustomerId}, 0, 0, {now})
                       ON CONFLICT (customer_id) DO NOTHING", token);

                var account = await db.WalletAccounts.SingleAsync(x => x.CustomerId == command.CustomerId, token);
                var previousBalance = account.PurchasedBalanceToman;
                var previousVersion = account.Version;
                try { account.CreditPurchasedValue(command.AmountToCreditToman, now); }
                catch (OverflowException)
                {
                    return Fail<WalletCashTopUpResponse>("wallet.balance_overflow", "Wallet balance would exceed the supported range.");
                }

                var receiptId = Guid.NewGuid();
                var ledgerId = Guid.NewGuid();
                cashReceipts.AppendWalletTopUpReceipt(new WalletTopUpCashReceiptDraft(
                    receiptId, command.CustomerId, ledgerId, command.AmountToCreditToman,
                    command.AmountReceivedToman, command.Actor.OperatorId, now));
                db.WalletLedgerEntries.Add(WalletLedgerEntry.RecordCashTopUp(
                    ledgerId, command.CustomerId, receiptId, command.AmountToCreditToman,
                    command.Actor.OperatorId, now));
                audit.Append(new AuditRecord(
                    OccurredAtUtc: now,
                    ActorType: "Operator",
                    ActorId: command.Actor.OperatorId.ToString("D"),
                    Operation: "wallet.cash-top-up",
                    ReferenceType: "WalletLedgerEntry",
                    ReferenceId: ledgerId.ToString("D"),
                    Reason: "Customer Wallet cash top-up",
                    CorrelationId: command.Actor.CorrelationId,
                    Source: command.Actor.Source,
                    Outcome: "Succeeded",
                    IdempotencyKey: command.IdempotencyKey.Trim(),
                    BeforeJson: JsonSerializer.Serialize(new { command.CustomerId, PurchasedBalanceToman = previousBalance, Version = previousVersion }),
                    AfterJson: JsonSerializer.Serialize(new
                    {
                        command.CustomerId,
                        CashReceiptId = receiptId,
                        WalletLedgerEntryId = ledgerId,
                        AmountCreditedToman = command.AmountToCreditToman,
                        AmountReceivedToman = command.AmountReceivedToman,
                        ChangeGivenToman = command.AmountReceivedToman - command.AmountToCreditToman,
                        account.PurchasedBalanceToman,
                        account.Version
                    })));

                return Result<WalletCashTopUpResponse>.Success(new WalletCashTopUpResponse(
                    receiptId, ledgerId, customer!.Id, customer.Code, customer.DisplayName,
                    command.AmountToCreditToman, command.AmountReceivedToman,
                    command.AmountReceivedToman - command.AmountToCreditToman,
                    account.PurchasedBalanceToman, account.Version, now));
            }, cancellationToken);
    }

    private static Error? ValidateCustomer(WalletCustomerFacts? customer)
    {
        if (customer is null)
            return new Error("wallet.customer_not_found", "Customer was not found.");
        if (customer.Kind != CustomerKindContract.Registered)
            return new Error("wallet.registered_customer_required", "Wallet top-up requires a registered customer.");
        if (customer.Status == CustomerStatusContract.Blocked)
            return new Error("wallet.customer_blocked", "Blocked customers cannot use Wallet.");
        if (customer.Status != CustomerStatusContract.Active)
            return new Error("wallet.customer_inactive", "Inactive customers cannot use Wallet.");
        return null;
    }

    private static Result<T> Fail<T>(string code, string message) => Result<T>.Failure(new Error(code, message));
}
