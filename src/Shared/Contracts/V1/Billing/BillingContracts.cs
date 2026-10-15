using GameNet.Shared.Contracts.V1.Sessions;

namespace GameNet.Shared.Contracts.V1.Billing;

/// <summary>
/// Durable read model for reconciling a cash checkout after an HTTP failure or Desktop restart.
/// Status comes from the invoice; paid outcomes include the durable Payment and Ledger identities.
/// </summary>
public sealed record CashCheckoutOutcomeResponse(
    Guid InvoiceId,
    Guid SessionId,
    Guid StationId,
    string TariffCodeSnapshot,
    string TariffNameSnapshot,
    long TariffHourlyRateTomanSnapshot,
    long ElapsedTicks,
    long AmountToman,
    InvoiceStatusContract Status,
    Guid? PaymentId,
    Guid? LedgerEntryId,
    long? AmountReceivedToman,
    long? ChangeGivenToman,
    DateTimeOffset IssuedAtUtc,
    DateTimeOffset? PaidAtUtc);
