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
        DateTimeOffset startedAtUtc)
    {
        Id = id;
        StationId = stationId;
        CustomerId = customerId;
        TariffId = tariffId;
        TariffCodeSnapshot = NormalizeSnapshot(tariffCodeSnapshot, nameof(tariffCodeSnapshot));
        TariffNameSnapshot = NormalizeSnapshot(tariffNameSnapshot, nameof(tariffNameSnapshot));
        TariffHourlyRateTomanSnapshot = tariffHourlyRateTomanSnapshot;
        StartedByOperatorId = startedByOperatorId;
        StartedAtUtc = startedAtUtc.ToUniversalTime();
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
    public DateTimeOffset StartedAtUtc { get; private set; }
    public Guid? EndedByOperatorId { get; private set; }
    public DateTimeOffset? EndedAtUtc { get; private set; }
    public Guid? PaidByOperatorId { get; private set; }
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
        DateTimeOffset startedAtUtc)
    {
        if (id == Guid.Empty) throw new ArgumentException("Session id is required.", nameof(id));
        if (stationId == Guid.Empty) throw new ArgumentException("Station id is required.", nameof(stationId));
        if (customerId == Guid.Empty) throw new ArgumentException("Customer id is required.", nameof(customerId));
        if (tariffId == Guid.Empty) throw new ArgumentException("Tariff id is required.", nameof(tariffId));
        if (startedByOperatorId == Guid.Empty) throw new ArgumentException("Operator id is required.", nameof(startedByOperatorId));
        if (tariffHourlyRateTomanSnapshot <= 0)
            throw new ArgumentOutOfRangeException(nameof(tariffHourlyRateTomanSnapshot));
        return new Session(id, stationId, customerId, tariffId, tariffCodeSnapshot, tariffNameSnapshot,
            tariffHourlyRateTomanSnapshot, startedByOperatorId, startedAtUtc);
    }

    public void End(Guid endedByOperatorId, DateTimeOffset endedAtUtc)
    {
        if (Status != SessionStatus.Active)
            throw new InvalidOperationException("SESSION_NOT_ACTIVE");
        if (endedByOperatorId == Guid.Empty)
            throw new ArgumentException("Operator id is required.", nameof(endedByOperatorId));

        var ended = endedAtUtc.ToUniversalTime();
        if (ended <= StartedAtUtc)
            throw new ArgumentOutOfRangeException(nameof(endedAtUtc), "Session end must be after session start.");

        EndedByOperatorId = endedByOperatorId;
        EndedAtUtc = ended;
        Status = SessionStatus.PendingSettlement;
        Version = checked(Version + 1);
    }

    public void MarkPaid(Guid paidByOperatorId, DateTimeOffset paidAtUtc)
    {
        if (Status != SessionStatus.PendingSettlement || EndedAtUtc is null)
            throw new InvalidOperationException("SESSION_NOT_READY_FOR_SETTLEMENT");
        if (paidByOperatorId == Guid.Empty)
            throw new ArgumentException("Operator id is required.", nameof(paidByOperatorId));

        var paid = paidAtUtc.ToUniversalTime();
        if (paid < EndedAtUtc.Value)
            throw new ArgumentOutOfRangeException(nameof(paidAtUtc), "Settlement cannot precede session end.");

        PaidByOperatorId = paidByOperatorId;
        PaidAtUtc = paid;
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
