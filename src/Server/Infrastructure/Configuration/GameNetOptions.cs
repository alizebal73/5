namespace GameNet.Server.Infrastructure.Configuration;

public sealed class GameNetOptions
{
    public const string SectionName = "GameNet";
    public string BusinessTimeZone { get; init; } = "Asia/Tehran";
    public string Currency { get; init; } = "TOM";
    public AuthenticationOptions Authentication { get; init; } = new();
    public AgentOptions Agent { get; init; } = new();
}

public sealed class AuthenticationOptions
{
    public bool Enabled { get; init; }
    public string? Issuer { get; init; }
    public string? Audience { get; init; }
}

public sealed class AgentOptions
{
    public int LeaseDurationSeconds { get; init; } = 15;
    public int HeartbeatIntervalSeconds { get; init; } = 5;
    public int AccessTokenLifetimeSeconds { get; init; } = 300;
}
