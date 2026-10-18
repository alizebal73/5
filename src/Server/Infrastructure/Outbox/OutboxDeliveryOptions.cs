namespace GameNet.Server.Infrastructure.Outbox;

public sealed class OutboxDeliveryOptions
{
    public int BatchSize { get; init; } = 10;
    // Preserve claim order by default; opt into parallel handlers only when event consumers are order-safe.
    public int MaxConcurrentHandlers { get; init; } = 1;
    public int PollIntervalSeconds { get; init; } = 2;
    public int LeaseDurationSeconds { get; init; } = 120;
    public int MaxAttempts { get; init; } = 10;
    public int InitialRetryDelaySeconds { get; init; } = 2;
    public int MaximumRetryDelaySeconds { get; init; } = 300;

    public bool IsValid =>
        BatchSize is >= 1 and <= 100 &&
        MaxConcurrentHandlers is >= 1 and <= 32 &&
        PollIntervalSeconds is >= 1 and <= 60 &&
        LeaseDurationSeconds is >= 10 and <= 600 &&
        MaxAttempts is >= 1 and <= 100 &&
        InitialRetryDelaySeconds is >= 1 and <= 300 &&
        MaximumRetryDelaySeconds >= InitialRetryDelaySeconds &&
        MaximumRetryDelaySeconds <= 3600;
}
