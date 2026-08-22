namespace YO4X.BuildingBlocks;

public sealed class AuthorizationDeniedException : Exception
{
    public AuthorizationDeniedException(string code, string message)
        : base(message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        Code = code;
    }

    public string Code { get; }
}
