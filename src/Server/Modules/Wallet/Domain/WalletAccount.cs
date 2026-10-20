namespace GameNet.Server.Modules.Wallet.Domain;

/// <summary>Mutable available/reserved balance projection. Immutable money movements live in WalletLedgerEntry.</summary>
public sealed class WalletAccount
{
    private WalletAccount() { }
    private WalletAccount(Guid customerId, DateTimeOffset createdAtUtc)
    {
        if (customerId == Guid.Empty) throw new ArgumentException("Customer id is required.", nameof(customerId));
        CustomerId = customerId;
        BalanceToman = 0;
        ReservedBalanceToman = 0;
        Version = 0;
        UpdatedAtUtc = createdAtUtc.ToUniversalTime();
    }

    public Guid CustomerId { get; private set; }
    public long BalanceToman { get; private set; }
    public long ReservedBalanceToman { get; private set; }
    public int Version { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public static WalletAccount Open(Guid customerId, DateTimeOffset nowUtc) => new(customerId, nowUtc);

    public long Credit(long amountToman, DateTimeOffset nowUtc)
    {
        if (amountToman <= 0) throw new ArgumentOutOfRangeException(nameof(amountToman));
        if (ReservedBalanceToman > 0)
            throw new InvalidOperationException("WALLET_PREPAID_SESSION_RESERVATION_ACTIVE");
        var newBalance = checked(BalanceToman + amountToman);
        var newVersion = checked(Version + 1);
        BalanceToman = newBalance;
        Version = newVersion;
        UpdatedAtUtc = nowUtc.ToUniversalTime();
        return newBalance;
    }

    /// <summary>Moves all currently available purchased value into a single active Session reservation.</summary>
    public long ReserveAllAvailable(long amountToman, DateTimeOffset nowUtc)
    {
        if (amountToman <= 0 || amountToman != BalanceToman)
            throw new ArgumentOutOfRangeException(nameof(amountToman), "Reservation must equal the current positive available balance.");
        if (ReservedBalanceToman != 0)
            throw new InvalidOperationException("WALLET_PREPAID_RESERVATION_ALREADY_ACTIVE");

        var version = checked(Version + 1);
        BalanceToman = 0;
        ReservedBalanceToman = amountToman;
        Version = version;
        UpdatedAtUtc = nowUtc.ToUniversalTime();
        return BalanceToman;
    }

    /// <summary>
    /// Applies a settled Session charge from reserved value and releases the unused part back to available credit.
    /// Version/projection snapshots represent the immutable Charge then optional Release ledger rows.
    /// </summary>
    public WalletSessionSettlementProjection SettlePrepaidReservation(
        long reservedAmountToman, long actualChargeToman, DateTimeOffset nowUtc)
    {
        if (reservedAmountToman <= 0 || actualChargeToman <= 0 || actualChargeToman > reservedAmountToman)
            throw new ArgumentOutOfRangeException(nameof(actualChargeToman));
        if (ReservedBalanceToman < reservedAmountToman)
            throw new InvalidOperationException("WALLET_PREPAID_RESERVATION_MISSING");

        var chargeVersion = checked(Version + 1);
        var reservedAfterCharge = checked(ReservedBalanceToman - actualChargeToman);
        var released = reservedAmountToman - actualChargeToman;
        var finalReserved = checked(ReservedBalanceToman - reservedAmountToman);
        var finalBalance = checked(BalanceToman + released);
        var finalVersion = released > 0 ? checked(chargeVersion + 1) : chargeVersion;

        BalanceToman = finalBalance;
        ReservedBalanceToman = finalReserved;
        Version = finalVersion;
        UpdatedAtUtc = nowUtc.ToUniversalTime();

        return new WalletSessionSettlementProjection(
            ChargeBalanceAfterToman: finalBalance - released,
            ChargeReservedBalanceAfterToman: reservedAfterCharge,
            ChargeWalletVersionAfter: chargeVersion,
            ReleaseAmountToman: released,
            ReleaseBalanceAfterToman: finalBalance,
            ReleaseReservedBalanceAfterToman: finalReserved,
            ReleaseWalletVersionAfter: released > 0 ? finalVersion : null,
            FinalBalanceToman: finalBalance,
            FinalReservedBalanceToman: finalReserved,
            FinalWalletVersion: finalVersion);
    }
}

public sealed record WalletSessionSettlementProjection(
    long ChargeBalanceAfterToman,
    long ChargeReservedBalanceAfterToman,
    int ChargeWalletVersionAfter,
    long ReleaseAmountToman,
    long ReleaseBalanceAfterToman,
    long ReleaseReservedBalanceAfterToman,
    int? ReleaseWalletVersionAfter,
    long FinalBalanceToman,
    long FinalReservedBalanceToman,
    int FinalWalletVersion);
