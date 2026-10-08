namespace GameNet.Shared.Primitives;

public sealed record Error(string Code, string Message);

public readonly record struct Result<T>
{
    private readonly T? _value;
    private readonly Error? _error;

    private Result(T value) { IsSuccess = true; _value = value; _error = null; }
    private Result(Error error) { IsSuccess = false; _value = default; _error = error; }

    public bool IsSuccess { get; }
    public T Value => IsSuccess ? _value! : throw new InvalidOperationException("RESULT_HAS_NO_VALUE");
    public Error Error => !IsSuccess ? _error! : throw new InvalidOperationException("RESULT_HAS_NO_ERROR");

    public static Result<T> Success(T value) => new(value);
    public static Result<T> Failure(Error error) => new(error);
}
