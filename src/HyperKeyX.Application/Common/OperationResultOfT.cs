namespace HyperKeyX.Application.Common;

public readonly record struct OperationResult<T>(bool Succeeded, T Value, string? ErrorMessage)
{
    public static OperationResult<T> Success(T value) => new(true, value, null);

    public static OperationResult<T> Failure(string message) =>
        new(false, default!, message);
}
