namespace GameNet.Shared.Contracts.V1.Sessions;

public enum SessionStatusContract
{
    Active = 1,
    PendingSettlement = 2,
    Paid = 3,
    Paused = 4
}

public enum InvoiceStatusContract
{
    PendingPayment = 1,
    Paid = 2,
    Reversed = 3
}

public sealed record StartSessionRequest(Guid StationId, Guid TariffId, Guid? CustomerId);
public sealed record EndSessionRequest(int ExpectedVersion);
public sealed record SessionLifecycleRequest(int ExpectedVersion);
public sealed record ReconcileSessionTimingRequest(int ExpectedSessionVersion, long ReconciledElapsedMinutes, string Reason, bool Confirmed);
public sealed record CashCheckoutRequest(long AmountReceivedToman, int ExpectedSessionVersion);

public sealed record SessionResponse(
    Guid Id,
    Guid StationId,
    Guid CustomerId,
    Guid TariffId,
    string TariffCodeSnapshot,
    string TariffNameSnapshot,
    long TariffHourlyRateTomanSnapshot,
    Guid StartedByOperatorId,
    DateTimeOffset StartedAtUtc,
    Guid? EndedByOperatorId,
    DateTimeOffset? EndedAtUtc,
    Guid? PaidByOperatorId,
    DateTimeOffset? PaidAtUtc,
    SessionStatusContract Status,
    int Version);

public sealed record InvoicePreviewResponse(
    Guid InvoiceId,
    Guid SessionId,
    int SessionVersion,
    Guid StationId,
    Guid CustomerId,
    Guid TariffId,
    string TariffCodeSnapshot,
    string TariffNameSnapshot,
    long TariffHourlyRateTomanSnapshot,
    long ElapsedTicks,
    long AmountToman,
    InvoiceStatusContract Status,
    DateTimeOffset IssuedAtUtc,
    DateTimeOffset? PaidAtUtc);

public sealed record CashCheckoutResponse(
    Guid InvoiceId,
    Guid PaymentId,
    Guid LedgerEntryId,
    long InvoiceAmountToman,
    long AmountReceivedToman,
    long ChangeGivenToman,
    DateTimeOffset PaidAtUtc);
