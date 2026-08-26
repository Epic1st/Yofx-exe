namespace YO4X.MarketData.Mt5Import;

/// <summary>
/// Raised when a stated fail-closed policy refuses the import. The run produces no output at all;
/// a partially converted dataset is never left behind.
/// </summary>
internal sealed class Mt5ImportRefusedException : Exception
{
    internal Mt5ImportRefusedException(string message)
        : base(message)
    {
    }

    internal Mt5ImportRefusedException()
    {
    }

    internal Mt5ImportRefusedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
