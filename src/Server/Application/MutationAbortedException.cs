using GameNet.Shared.Primitives;

namespace GameNet.Server.Application;

public sealed class MutationAbortedException(Error error) : Exception(error.Message)
{
    public Error Error { get; } = error;
}
