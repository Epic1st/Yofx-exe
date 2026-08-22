namespace YO4X.BuildingBlocks;

public sealed class ResourceConflictException : Exception
{
    public ResourceConflictException(string code, string message)
        : base(message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        Code = code;
    }

    public string Code { get; }
}
