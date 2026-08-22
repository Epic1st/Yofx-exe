namespace YO4X.BuildingBlocks;

public sealed record OperationError(string Code, string Message, string? Path = null);

public sealed record OperationResult<T>(T? Value, IReadOnlyList<OperationError> Errors)
{
    public bool IsSuccess => Errors.Count == 0;
}

public static class OperationResult
{
    public static OperationResult<T> Success<T>(T value) => new(value, []);

    public static OperationResult<T> Failure<T>(params OperationError[] errors) => new(default, errors);
}
