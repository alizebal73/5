namespace GameNet.Server.Application;

public sealed class PersistenceConflictException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
