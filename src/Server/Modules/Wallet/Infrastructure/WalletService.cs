using System.Text.Json;
using GameNet.Server.Infrastructure.Audit;
using GameNet.Server.Infrastructure.Idempotency;
using GameNet.Server.Infrastructure.Transactions;
using GameNet.Server.Modules.Billing.Contracts;
using GameNet.Server.Modules.Customers.Contracts;
using GameNet.Server.Modules.Wallet.Application;
using GameNet.Server.Modules.Wallet.Domain;
using GameNet.Server.Persistence;
using GameNet.Shared.Contracts.V1.Security;
using GameNet.Shared.Contracts.V1.Wallet;
using GameNet.Shared.Primitives;
using Microsoft.EntityFrameworkCore;

namespace GameNet.Server.Modules.Wallet.Infrastructure;

public sealed class WalletService(
    GameNetDbContext db,
    ICustomerWalletEligibility customerEligibility,
    CommandMutationPipeline mutationPipeline,
    IRequestFingerprint requestFingerprint,
    ICashReceiptWriter cashReceiptWriter,
    IAuditWriter audit,
    IGameClock clock) : IWalletService
{
    private static readonly TimeSpan Lease = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan Retention = TimeSpan.FromDays(1);

    public async Task<Result<WalletBalanceResponse>> GetBalanceAsync(
        Guid customerId, WalletActorContext actor, CancellationToken cancellationToken)
    {
        if (!actor.Permissions.Contains(Permissions.WalletWrite))
            return Fail<WalletBalanceResponse>("wallet.forbidden", "Not allowed to read Wallet balances.");
        if (customerId == Guid.Empty)
            return Fail<WalletBalanceResponse>("wallet.customer_not_found", "Customer was not found.");

        var registeredCustomer = await customerEligibility.GetRegisteredCustomerAsync(customerId, cancellationToken);
        if (!registeredCustomer.IsSuccess)
            return Fail<WalletBalanceResponse>("wallet.customer_not_found", "Registered customer was not found.");

        var account = await db.WalletAccounts.AsNoTracking()
            .SingleOrDefaultAsync(x => x.CustomerId == customerId, cancellationToken);
        return Result<WalletBalanceResponse>.Success(account is null
            ? new WalletBalanceResponse(customerId, 0, 0, null)
            : new WalletBalanceResponse(account.CustomerId, account.BalanceToman, account.Version, account.UpdatedAtUtc));
    }

    public Task<Result<WalletCashTopUpResponse>> CashTopUpAsync(
        CashTopUpWalletCommand command, CancellationToken cancellationToken)
    {
        if (!command.Actor.Permissions.Contains(Permissions.WalletWrite))
            return Task.FromResult(Fail<WalletCashTopUpResponse>("wallet.forbidden", "Not allowed to add Wallet credit."));
        if (command.CustomerId == Guid.Empty || command.ExpectedCustomerVersion < 1 ||
            command.AmountToCreditToman <= 0 || command.AmountReceivedToman < command.AmountToCreditToman)
            return Task.FromResult(Fail<WalletCashTopUpResponse>("wallet.invalid", "Customer, expected version, credited amount or received cash is invalid."));

        var scope = $"wallet.cash-top-up:{command.Actor.OperatorId:D}";
        var payload = new
        {
            command.CustomerId,
            command.AmountToCreditToman,
            command.AmountReceivedToman,
            command.ExpectedCustomerVersion
        };

        return mutationPipeline.ExecuteAsync(
            scope, "wallet.cash-top-up", command.IdempotencyKey,
            requestFingerprint.Compute(payload), 200, Lease, Retention,
            async token =>
            {
                // Customer module owns registration/status/version rules and locks the row
                // inside this mutation pipeline's transaction before financial writes.
                var customerResult = await customerEligibility.LockActiveRegisteredCustomerForWalletTopUpAsync(
                    command.CustomerId, command.ExpectedCustomerVersion, token);
                if (!customerResult.IsSuccess)
                {
                    var error = customerResult.Error;
                    return error.Code switch
                    {
                        "customers.not_found" => Fail<WalletCashTopUpResponse>("wallet.customer_not_found", error.Message),
                        "customers.inactive" => Fail<WalletCashTopUpResponse>("wallet.customer_inactive", error.Message),
                        "customers.version_conflict" => Fail<WalletCashTopUpResponse>("wallet.customer_version_conflict", error.Message),
                        _ => Fail<WalletCashTopUpResponse>("wallet.invalid", error.Message)
                    };
                }
                var customer = customerResult.Value;

                var now = clock.UtcNow;
                var account = await db.WalletAccounts.SingleOrDefaultAsync(x => x.CustomerId == command.CustomerId, token);
                if (account is null)
                {
                    account = WalletAccount.Open(command.CustomerId, now);
                    await db.WalletAccounts.AddAsync(account, token);
                }

                long balanceAfter;
                try { balanceAfter = account.Credit(command.AmountToCreditToman, now); }
                catch (OverflowException)
                {
                    return Fail<WalletCashTopUpResponse>("wallet.balance_overflow", "The Wallet balance would exceed the supported range.");
                }
                catch (ArgumentOutOfRangeException)
                {
                    return Fail<WalletCashTopUpResponse>("wallet.invalid", "Credited amount must be a positive whole-Toman value.");
                }

                var receiptId = Guid.NewGuid();
                var receiptResult = await cashReceiptWriter.RecordWalletTopUpAsync(new RecordWalletTopUpCashReceiptCommand(
                    receiptId, customer.CustomerId, command.AmountToCreditToman, command.AmountReceivedToman,
                    command.Actor.OperatorId, now, command.IdempotencyKey.Trim(),
                    command.Actor.CorrelationId, command.Actor.Source), token);
                if (!receiptResult.IsSuccess)
                    return Fail<WalletCashTopUpResponse>(receiptResult.Error.Code, receiptResult.Error.Message);

                var ledgerId = Guid.NewGuid();
                await db.WalletLedgerEntries.AddAsync(WalletLedgerEntry.RecordCashTopUp(
                    ledgerId, customer.CustomerId, receiptId, command.AmountToCreditToman,
                    balanceAfter, command.Actor.OperatorId, now), token);

                var response = new WalletCashTopUpResponse(
                    customer.CustomerId, receiptId, ledgerId, command.AmountToCreditToman,
                    command.AmountReceivedToman, receiptResult.Value.ChangeGivenToman,
                    balanceAfter, account.Version, receiptResult.Value.ReceivedAtUtc);

                audit.Append(new AuditRecord(
                    now, "Operator", command.Actor.OperatorId.ToString("D"),
                    "wallet.cash_top_up", "Customer", customer.CustomerId.ToString("D"), null,
                    command.Actor.CorrelationId, command.Actor.Source, "Succeeded", command.IdempotencyKey,
                    AfterJson: JsonSerializer.Serialize(new
                    {
                        response.CustomerId, response.CashReceiptId, response.WalletLedgerEntryId,
                        response.AmountCreditedToman, response.AmountReceivedToman, response.ChangeGivenToman,
                        response.BalanceAfterToman, response.WalletVersion
                    })));

                return Result<WalletCashTopUpResponse>.Success(response);
            },
            cancellationToken);
    }

    private static Result<T> Fail<T>(string code, string message) => Result<T>.Failure(new Error(code, message));
}