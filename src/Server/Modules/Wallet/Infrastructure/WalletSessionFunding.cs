using System.Text.Json;
using GameNet.Server.Infrastructure.Audit;
using GameNet.Server.Modules.Customers.Contracts;
using GameNet.Server.Modules.Wallet.Contracts;
using GameNet.Server.Modules.Wallet.Domain;
using GameNet.Server.Persistence;
using GameNet.Shared.Primitives;
using Microsoft.EntityFrameworkCore;

namespace GameNet.Server.Modules.Wallet.Infrastructure;

/// <summary>
/// Reserves purchased Wallet credit for one Session, then appends the charge and optional
/// release when it settles. Every operation participates in the owning Session transaction.
/// </summary>
public sealed class WalletSessionFunding(
    GameNetDbContext db,
    ICustomerWalletEligibility customerEligibility,
    IAuditWriter audit,
    IGameClock clock) : IWalletSessionFunding
{
    public async Task<Result<WalletSessionReservation>> ReserveForSessionAsync(
        Guid customerId, Guid sessionId, Guid actorId, string actorType,
        string correlationId, string source, string idempotencyKey,
        CancellationToken cancellationToken)
    {
        if (db.Database.CurrentTransaction is null)
            return Fail<WalletSessionReservation>("wallet.transaction_required",
                "Wallet reservation requires the active Session transaction.");
        if (customerId == Guid.Empty || sessionId == Guid.Empty)
            return Fail<WalletSessionReservation>("wallet.invalid", "Customer and Session identities are required.");
        if (actorType is not ("Operator" or "Customer" or "System") ||
            (actorId == Guid.Empty && actorType != "System"))
            return Fail<WalletSessionReservation>("wallet.invalid_actor", "Wallet actor identity is invalid.");

        // Customer-before-Wallet lock order matches cash top-up and serializes starts with
        // top-ups, including when a Wallet account is first created.
        var customer = await customerEligibility.LockActiveRegisteredCustomerForSessionAsync(
            customerId, cancellationToken);
        if (!customer.IsSuccess)
        {
            var error = customer.Error;
            return error.Code switch
            {
                "customers.inactive" => Fail<WalletSessionReservation>("wallet.customer_inactive", error.Message),
                _ => Fail<WalletSessionReservation>("wallet.customer_not_found", error.Message)
            };
        }

        var account = await db.WalletAccounts
            .FromSqlInterpolated($"SELECT * FROM wallet_accounts WHERE customer_id = {customerId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);
        if (account is null || account.BalanceToman <= 0)
            return Fail<WalletSessionReservation>("wallet.insufficient_balance",
                "The customer has no available prepaid Wallet credit.");
        if (account.ReservedBalanceToman != 0)
            return Fail<WalletSessionReservation>("wallet.prepaid_reservation_active",
                "This customer's Wallet credit is already reserved by another Session.");

        var amount = account.BalanceToman;
        var now = clock.UtcNow;
        try
        {
            account.ReserveAllAvailable(amount, now);
        }
        catch (ArgumentOutOfRangeException)
        {
            return Fail<WalletSessionReservation>("wallet.insufficient_balance",
                "The customer has no available prepaid Wallet credit.");
        }
        catch (InvalidOperationException)
        {
            return Fail<WalletSessionReservation>("wallet.prepaid_reservation_active",
                "This customer's Wallet credit is already reserved by another Session.");
        }

        var ledger = WalletLedgerEntry.RecordSessionReserve(
            Guid.NewGuid(), customerId, sessionId, amount,
            account.BalanceToman, account.ReservedBalanceToman,
            actorId, actorType, now, account.Version);
        await db.WalletLedgerEntries.AddAsync(ledger, cancellationToken);

        audit.Append(new AuditRecord(
            now, actorType, actorId.ToString("D"), "wallet.session_reserve",
            "Session", sessionId.ToString("D"), null, correlationId, source, "Succeeded",
            idempotencyKey,
            AfterJson: JsonSerializer.Serialize(new
            {
                customerId, sessionId, amount,
                account.BalanceToman, account.ReservedBalanceToman, account.Version
            })));

        return Result<WalletSessionReservation>.Success(new WalletSessionReservation(
            customerId, sessionId, amount, account.Version, now));
    }

    public async Task<WalletSessionReservation?> FindActiveReservationAsync(
        Guid sessionId, CancellationToken cancellationToken)
    {
        if (sessionId == Guid.Empty)
            return null;

        var reserve = db.WalletLedgerEntries.Local.FirstOrDefault(x =>
            x.SessionId == sessionId && x.EntryType == WalletLedgerEntryType.SessionReserve);
        reserve ??= await db.WalletLedgerEntries.AsNoTracking().SingleOrDefaultAsync(
            x => x.SessionId == sessionId && x.EntryType == WalletLedgerEntryType.SessionReserve,
            cancellationToken);
        if (reserve is null)
            return null;

        var settled = await db.WalletLedgerEntries.AsNoTracking().AnyAsync(
            x => x.SessionId == sessionId &&
                 (x.EntryType == WalletLedgerEntryType.SessionCharge ||
                  x.EntryType == WalletLedgerEntryType.SessionRelease),
            cancellationToken);
        if (settled)
            return null;

        return new WalletSessionReservation(
            reserve.CustomerId, sessionId, reserve.AmountToman,
            reserve.WalletVersionAfter, reserve.OccurredAtUtc);
    }

    public async Task<Result<WalletSessionSettlement>> SettleSessionAsync(
        Guid customerId, Guid sessionId, long actualChargeToman, Guid actorId, string actorType,
        string correlationId, string source, string idempotencyKey,
        CancellationToken cancellationToken)
    {
        if (db.Database.CurrentTransaction is null)
            return Fail<WalletSessionSettlement>("wallet.transaction_required",
                "Wallet settlement requires the active Session transaction.");
        if (customerId == Guid.Empty || sessionId == Guid.Empty || actualChargeToman <= 0)
            return Fail<WalletSessionSettlement>("wallet.invalid", "Customer, Session and positive charge are required.");
        if (actorType is not ("Operator" or "Customer" or "System") ||
            (actorId == Guid.Empty && actorType != "System"))
            return Fail<WalletSessionSettlement>("wallet.invalid_actor", "Wallet actor identity is invalid.");

        var reserve = await FindActiveReservationAsync(sessionId, cancellationToken);
        if (reserve is null || reserve.CustomerId != customerId)
            return Fail<WalletSessionSettlement>("wallet.reservation_missing",
                "An active prepaid Wallet reservation for this Session was not found.");
        if (actualChargeToman > reserve.ReservedAmountToman)
            return Fail<WalletSessionSettlement>("wallet.prepaid_budget_exceeded",
                "The Session charge exceeds its reserved prepaid Wallet amount.");

        // The Customer row lock acquired at start remains held until end/settlement; lock
        // the account projection as a second guard before calculating the release.
        var account = await db.WalletAccounts
            .FromSqlInterpolated($"SELECT * FROM wallet_accounts WHERE customer_id = {customerId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);
        if (account is null || account.ReservedBalanceToman != reserve.ReservedAmountToman)
            return Fail<WalletSessionSettlement>("wallet.reservation_inconsistent",
                "The reserved Wallet projection does not reconcile with its immutable SessionReserve entry.");

        var now = clock.UtcNow;
        WalletSessionSettlementProjection projection;
        try
        {
            projection = account.SettlePrepaidReservation(reserve.ReservedAmountToman, actualChargeToman, now);
        }
        catch (ArgumentOutOfRangeException)
        {
            return Fail<WalletSessionSettlement>("wallet.prepaid_budget_exceeded",
                "The Session charge is outside its reserved prepaid Wallet amount.");
        }
        catch (InvalidOperationException)
        {
            return Fail<WalletSessionSettlement>("wallet.reservation_inconsistent",
                "The reserved Wallet projection changed; financial reconciliation is required.");
        }

        await db.WalletLedgerEntries.AddAsync(WalletLedgerEntry.RecordSessionCharge(
            Guid.NewGuid(), customerId, sessionId, actualChargeToman,
            projection.ChargeBalanceAfterToman, projection.ChargeReservedBalanceAfterToman,
            actorId, actorType, now, projection.ChargeWalletVersionAfter), cancellationToken);

        if (projection.ReleaseAmountToman > 0)
        {
            await db.WalletLedgerEntries.AddAsync(WalletLedgerEntry.RecordSessionRelease(
                Guid.NewGuid(), customerId, sessionId, projection.ReleaseAmountToman,
                projection.ReleaseBalanceAfterToman, projection.ReleaseReservedBalanceAfterToman,
                actorId, actorType, now, projection.ReleaseWalletVersionAfter!.Value), cancellationToken);
        }

        audit.Append(new AuditRecord(
            now, actorType, actorId.ToString("D"), "wallet.session_settle",
            "Session", sessionId.ToString("D"), null, correlationId, source, "Succeeded",
            idempotencyKey,
            AfterJson: JsonSerializer.Serialize(new
            {
                customerId, sessionId, reserve.ReservedAmountToman, actualChargeToman,
                projection.ReleaseAmountToman, projection.FinalBalanceToman,
                projection.FinalReservedBalanceToman, projection.FinalWalletVersion
            })));

        return Result<WalletSessionSettlement>.Success(new WalletSessionSettlement(
            customerId, sessionId, actualChargeToman, projection.ReleaseAmountToman,
            projection.FinalBalanceToman, projection.FinalWalletVersion, now));
    }

    private static Result<T> Fail<T>(string code, string message) =>
        Result<T>.Failure(new Error(code, message));
}
