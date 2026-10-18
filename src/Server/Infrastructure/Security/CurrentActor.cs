namespace GameNet.Server.Infrastructure.Security;

public sealed record CurrentActor(string ActorType, string? ActorId, IReadOnlySet<string> Permissions);
