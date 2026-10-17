using GameNet.Shared.Primitives;

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
        StartedAtUtc = startedAtUtc.ToUniversalTime();
        StartedMonotonicTimestamp = startedMonotonicTimestamp;
        StartedMonotonicFrequency = startedMonotonicFrequency;
        StartedSystemUptimeMilliseconds = startedSystemUptimeMilliseconds;
        StartedBootIdentifier = startedBootIdentifier;
        CurrentSegmentStartedMonotonicTimestamp = startedMonotonicTimestamp;
        CurrentSegmentStartedMonotonicFrequency = startedMonotonicFrequency;
        CurrentSegmentStartedSystemUptimeMilliseconds = startedSystemUptimeMilliseconds;
        CurrentSegmentStartedBootIdentifier = startedBootIdentifier;
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
    public long StartedMonotonicTimestamp { get; private set; }
    public long StartedMonotonicFrequency { get; private set; }
    public long StartedSystemUptimeMilliseconds { get; private set; }
    public Guid StartedBootIdentifier { get; private set; }
    public long AccumulatedElapsedTicks { get; private set; }
    public long? CurrentSegmentStartedMonotonicTimestamp { get; private set; }
    public long? CurrentSegmentStartedMonotonicFrequency { get; private set; }
    public long? CurrentSegmentStartedSystemUptimeMilliseconds { get; private set; }
    public Guid? CurrentSegmentStartedBootIdentifier { get; private set; }
    public long? ElapsedTicks { get; private set; }
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
        DateTimeOffset startedAtUtc,
        long startedMonotonicTimestamp,
        long startedMonotonicFrequency,
        long startedSystemUptimeMilliseconds,
        Guid startedBootIdentifier)
    {
        if (id == Guid.Empty) throw new ArgumentException("Session id is required.", nameof(id));
        if (stationId == Guid.Empty) throw new ArgumentException("Station id is required.", nameof(stationId));
        if (customerId == Guid.Empty) throw new ArgumentException("Customer id is required.", nameof(customerId));
        if (tariffId == Guid.Empty) throw new ArgumentException("Tariff id is required.", nameof(tariffId));
        if (startedByOperatorId == Guid.Empty) throw new ArgumentException("Operator id is required.", nameof(startedByOperatorId));
        if (tariffHourlyRateTomanSnapshot <= 0)
            throw new ArgumentOutOfRangeException(nameof(tariffHourlyRateTomanSnapshot));
        if (startedMonotonicFrequency <= 0)
            throw new ArgumentOutOfRangeException(nameof(startedMonotonicFrequency));
        if (startedSystemUptimeMilliseconds < 0)
            throw new ArgumentOutOfRangeException(nameof(startedSystemUptimeMilliseconds));
        if (startedBootIdentifier == Guid.Empty)
            throw new ArgumentOutOfRangeException(nameof(startedBootIdentifier));

        return new Session(id, stationId, customerId, tariffId, tariffCodeSnapshot, tariffNameSnapshot,
            tariffHourlyRateTomanSnapshot, startedByOperatorId, startedAtUtc,
            startedMonotonicTimestamp, startedMonotonicFrequency, startedSystemUptimeMilliseconds, startedBootIdentifier);
    }

    /// <summary>
    /// Rebinds an unsettled Session to another station without changing tariff, customer,
    /// lifecycle state or monotonic timing evidence. Compatibility and destination availability
    /// are validated by the Server use case before this domain transition.
    /// </summary>
    public void TransferTo(Guid targetStationId, Guid transferredByOperatorId)
    {
        if (Status is not (SessionStatus.Active or SessionStatus.Paused))
            throw new InvalidOperationException("SESSION_NOT_ACTIVE_OR_PAUSED");
        if (targetStationId == Guid.Empty)
            throw new ArgumentException("Target station id is required.", nameof(targetStationId));
        if (targetStationId == StationId)
            throw new InvalidOperationException("SESSION_TRANSFER_SAME_STATION");
        if (transferredByOperatorId == Guid.Empty)
            throw new ArgumentException("Operator id is required.", nameof(transferredByOperatorId));

        StationId = targetStationId;
        Version = checked(Version + 1);
    }

    public void Pause(Guid pausedByOperatorId, long currentSegmentElapsedTicks)
    {
        if (Status != SessionStatus.Active)
            throw new InvalidOperationException("SESSION_NOT_ACTIVE");
        if (pausedByOperatorId == Guid.Empty)
            throw new ArgumentException("Operator id is required.", nameof(pausedByOperatorId));
        if (currentSegmentElapsedTicks <= 0)
            throw new ArgumentOutOfRangeException(nameof(currentSegmentElapsedTicks));
        if (CurrentSegmentStartedMonotonicTimestamp is null ||
            CurrentSegmentStartedMonotonicFrequency is null ||
            CurrentSegmentStartedSystemUptimeMilliseconds is null ||
            CurrentSegmentStartedBootIdentifier is null)
            throw new InvalidOperationException("SESSION_ACTIVE_SEGMENT_MISSING");

        AccumulatedElapsedTicks = checked(AccumulatedElapsedTicks + currentSegmentElapsedTicks);
        if (AccumulatedElapsedTicks <= 0)
            throw new InvalidOperationException("SESSION_BILLABLE_TIME_INVALID");

        CurrentSegmentStartedMonotonicTimestamp = null;
        CurrentSegmentStartedMonotonicFrequency = null;
        CurrentSegmentStartedSystemUptimeMilliseconds = null;
        CurrentSegmentStartedBootIdentifier = null;
        Status = SessionStatus.Paused;
        Version = checked(Version + 1);
    }

    public void Resume(Guid resumedByOperatorId, GameClockSnapshot resumedAt)
    {
        if (Status != SessionStatus.Paused)
            throw new InvalidOperationException("SESSION_NOT_PAUSED");
        if (resumedByOperatorId == Guid.Empty)
            throw new ArgumentException("Operator id is required.", nameof(resumedByOperatorId));
        ArgumentNullException.ThrowIfNull(resumedAt);
        if (resumedAt.Timestamp < 0 || resumedAt.TimestampFrequency <= 0 ||
            resumedAt.SystemUptimeMilliseconds < 0 || resumedAt.BootIdentifier == Guid.Empty)
            throw new ArgumentException("A valid monotonic clock snapshot is required.", nameof(resumedAt));

        CurrentSegmentStartedMonotonicTimestamp = resumedAt.Timestamp;
        CurrentSegmentStartedMonotonicFrequency = resumedAt.TimestampFrequency;
        CurrentSegmentStartedSystemUptimeMilliseconds = resumedAt.SystemUptimeMilliseconds;
        CurrentSegmentStartedBootIdentifier = resumedAt.BootIdentifier;
        Status = SessionStatus.Active;
        Version = checked(Version + 1);
    }

    public void End(Guid endedByOperatorId, DateTimeOffset endedAtUtc, long elapsedTicks)
    {
        if (Status is not (SessionStatus.Active or SessionStatus.Paused))
            throw new InvalidOperationException("SESSION_NOT_ACTIVE_OR_PAUSED");
        if (endedByOperatorId == Guid.Empty)
            throw new ArgumentException("Operator id is required.", nameof(endedByOperatorId));
        if (elapsedTicks <= 0 || elapsedTicks < AccumulatedElapsedTicks)
            throw new ArgumentOutOfRangeException(nameof(elapsedTicks), "Session billable time must be positive and cannot omit accumulated segments.");
        if (Status == SessionStatus.Paused && elapsedTicks != AccumulatedElapsedTicks)
            throw new ArgumentOutOfRangeException(nameof(elapsedTicks), "A paused Session must end using only its already accumulated active segments.");

        // UTC is an event timestamp, not the duration authority; it may move backwards.
        EndedByOperatorId = endedByOperatorId;
        EndedAtUtc = endedAtUtc.ToUniversalTime();
        ElapsedTicks = elapsedTicks;
        AccumulatedElapsedTicks = elapsedTicks;
        CurrentSegmentStartedMonotonicTimestamp = null;
        CurrentSegmentStartedMonotonicFrequency = null;
        CurrentSegmentStartedSystemUptimeMilliseconds = null;
        CurrentSegmentStartedBootIdentifier = null;
        Status = SessionStatus.PendingSettlement;
        Version = checked(Version + 1);
    }

    public void MarkPaid(Guid paidByOperatorId, DateTimeOffset paidAtUtc)
    {
        if (Status != SessionStatus.PendingSettlement || EndedAtUtc is null || ElapsedTicks is null)
            throw new InvalidOperationException("SESSION_NOT_READY_FOR_SETTLEMENT");
        if (paidByOperatorId == Guid.Empty)
            throw new ArgumentException("Operator id is required.", nameof(paidByOperatorId));

        PaidByOperatorId = paidByOperatorId;
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
