namespace GameNet.Shared.Primitives;

public interface IGameClock
{
    DateTimeOffset UtcNow { get; }
}

public sealed class SystemGameClock(TimeProvider provider) : IGameClock
{
    public DateTimeOffset UtcNow => provider.GetUtcNow();
}
