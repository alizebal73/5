namespace GameNet.Server.Modules.Sessions.Domain;

public sealed class Session
{
    private Session() { }

    private Session(
        Guid id,
        Guid stationId,
        Guid customerId,
        Guid tariffId,
        string tariffCodeSnapshot,
        string tariffNameSnapshot,
        long tariffHourlyRateTomanSnapshot,
        Guid startedByOperatorId,
        Guid? startedByCustomerId,
        DateTimeOffset startedAtUtc,
        long startedMonotonicTimestamp,
        long startedMonotonicFrequency,
        long startedSystemUptimeMilliseconds,
        Guid startedBootIdentifier)
    {
        Id = id;
        StationId = stationId;
        CustomerId = customerId;
        TariffId = tariffId;
        TariffCodeSnapshot = NormalizeSnapshot(tariffCodeSnapshot, nameof(tariffCodeSnapshot));
        TariffNameSnapshot = NormalizeSnapshot(tariffNameSnapshot, nameof(tariffNameSnapshot));
        TariffHourlyRateTomanSnapshot = tariffHourlyRateTomanSnapshot;
        StartedByOperatorId = startedByOperatorId;
        StartedByCustomerId = startedByCustomerId;
        StartedAtUtc = startedAtUtc.ToUniversalTime();
        StartedMonotonicTimestamp = startedMonotonicTimestamp;
        StartedMonotonicFrequency = startedMonotonicFrequency;
        StartedSystemUptimeMilliseconds = startedSystemUptimeMilliseconds;
        StartedBootIdentifier = startedBootIdentifier;
        Status = SessionStatus.Active;
        Version = 1;
    }

    public Guid Id { get; private set; }
    public Guid StationId { get; private set; }
    public Guid CustomerId { get; private set; }
    public Guid TariffId { get; private set; }
    public string TariffCodeSnapshot { get; private set; } = null!;
    public string TariffNameSnapshot { get; private set; } = null!;
    public long TariffHourlyRateTomanSnapshot { get; private set; }
    public Guid StartedByOperatorId { get; private set; }
    public Guid? StartedByCustomerId { get; private set; }
    public DateTimeOffset StartedAtUtc { get; private set; }
    public long StartedMonotonicTimestamp { get; private set; }
    public long StartedMonotonicFrequency { get; private set; }
    public long StartedSystemUptimeMilliseconds { get; private set; }
    public Guid StartedBootIdentifier { get; private set; }
    public long? ElapsedTicks { get; private set; }
    public Guid? EndedByOperatorId { get; private set; }
    public bool EndedBySystem { get; private set; }
    public DateTimeOffset? EndedAtUtc { get; private set; }
    public Guid? PaidByOperatorId { get; private set; }
    public Guid? PaidByCustomerId { get; private set; }
    public DateTimeOffset? PaidAtUtc { get; private set; }
    public SessionStatus Status { get; private set; }
    public int Version { get; private set; }

    public static Session Start(
        Guid id,
        Guid stationId,
        Guid customerId,
        Guid tariffId,
        string tariffCodeSnapshot,
        string tariffNameSnapshot,
        long tariffHourlyRateTomanSnapshot,
        Guid startedByOperatorId,
        DateTimeOffset startedAtUtc,
        long startedMonotonicTimestamp,
        long startedMonotonicFrequency,
        long startedSystemUptimeMilliseconds,
        Guid startedBootIdentifier,
        Guid? startedByCustomerId = null)
    {
        if (id == Guid.Empty) throw new ArgumentException("Session id is required.", nameof(id));
        if (stationId == Guid.Empty) throw new ArgumentException("Station id is required.", nameof(stationId));
        if (customerId == Guid.Empty) throw new ArgumentException("Customer id is required.", nameof(customerId));
        if (tariffId == Guid.Empty) throw new ArgumentException("Tariff id is required.", nameof(tariffId));
        if (startedByCustomerId is Guid customerActorId)
        {
            if (customerActorId == Guid.Empty || startedByOperatorId != Guid.Empty)
                throw new ArgumentException("A customer-started Session must have a Customer actor and no Operator actor.");
        }
        else if (startedByOperatorId == Guid.Empty)
        {
            throw new ArgumentException("Operator id is required unless the Session was started by a Customer.", nameof(startedByOperatorId));
        }
        if (tariffHourlyRateTomanSnapshot <= 0)
            throw new ArgumentOutOfRangeException(nameof(tariffHourlyRateTomanSnapshot));
        if (startedMonotonicFrequency <= 0)
            throw new ArgumentOutOfRangeException(nameof(startedMonotonicFrequency));
        if (startedSystemUptimeMilliseconds < 0)
            throw new ArgumentOutOfRangeException(nameof(startedSystemUptimeMilliseconds));
        if (startedBootIdentifier == Guid.Empty)
            throw new ArgumentOutOfRangeException(nameof(startedBootIdentifier));

        return new Session(id, stationId, customerId, tariffId, tariffCodeSnapshot, tariffNameSnapshot,
            tariffHourlyRateTomanSnapshot, startedByOperatorId, startedByCustomerId, startedAtUtc,
            startedMonotonicTimestamp, startedMonotonicFrequency, startedSystemUptimeMilliseconds, startedBootIdentifier);
    }

    public void End(Guid endedByOperatorId, DateTimeOffset endedAtUtc, long elapsedTicks, bool endedBySystem = false)
    {
        if (Status != SessionStatus.Active)
            throw new InvalidOperationException("SESSION_NOT_ACTIVE");
        if (endedBySystem ? endedByOperatorId != Guid.Empty : endedByOperatorId == Guid.Empty)
            throw new ArgumentException("A Session end must identify either an Operator or the System, not both.", nameof(endedByOperatorId));
        if (elapsedTicks <= 0)
            throw new ArgumentOutOfRangeException(nameof(elapsedTicks), "Session elapsed time must be positive.");

        // UTC is an event timestamp, not the duration authority; it may move backwards.
        EndedByOperatorId = endedByOperatorId;
        EndedBySystem = endedBySystem;
        EndedAtUtc = endedAtUtc.ToUniversalTime();
        ElapsedTicks = elapsedTicks;
        Status = SessionStatus.PendingSettlement;
        Version = checked(Version + 1);
    }

    public void MarkPaid(Guid paidByOperatorId, DateTimeOffset paidAtUtc, Guid? paidByCustomerId = null)
    {
        if (Status != SessionStatus.PendingSettlement || EndedAtUtc is null || ElapsedTicks is null)
            throw new InvalidOperationException("SESSION_NOT_READY_FOR_SETTLEMENT");
        if (paidByCustomerId is Guid customerPayerId)
        {
            if (customerPayerId == Guid.Empty || paidByOperatorId != Guid.Empty)
                throw new ArgumentException("A Wallet-funded Session must identify the Customer payer and no Operator payer.");
        }
        else if (paidByOperatorId == Guid.Empty)
        {
            throw new ArgumentException("Operator id is required unless the Session is paid from Customer Wallet credit.", nameof(paidByOperatorId));
        }

        PaidByOperatorId = paidByOperatorId;
        PaidByCustomerId = paidByCustomerId;
        PaidAtUtc = paidAtUtc.ToUniversalTime();
        Status = SessionStatus.Paid;
        Version = checked(Version + 1);
    }

    private static string NormalizeSnapshot(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Tariff snapshot is required.", parameterName);
        var normalized = value.Trim();
        if (normalized.Length > 120 || normalized.Any(char.IsControl))
            throw new ArgumentException("Tariff snapshot is too long or contains control characters.", parameterName);
        return normalized;
    }
}
