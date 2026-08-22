namespace YO4X.Admin.Postgres;

public sealed class AdminAuthorizationDeniedException : Exception
{
    public AdminAuthorizationDeniedException(string code, string message)
        : base(message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        Code = code;
    }

    public string Code { get; }
}

public sealed class AdminResourceNotFoundException : Exception
{
    public AdminResourceNotFoundException()
        : base("The resource was not found.")
    {
    }
}
