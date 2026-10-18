namespace GameNet.Server.Infrastructure.Transactions;
public sealed class PersistenceConflictException(string code, Exception innerException) : Exception(code, innerException)
{
    public string Code { get; } = code;
}
