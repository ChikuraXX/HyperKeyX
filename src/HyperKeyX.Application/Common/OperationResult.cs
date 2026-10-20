namespace HyperKeyX.Application.Common;

public readonly record struct OperationResult(bool Succeeded, string? ErrorMessage)
{
    public static OperationResult Success() => new(true, null);

    public static OperationResult Failure(string message) => new(false, message);
}
