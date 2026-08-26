namespace YO4X.Mql5.Runtime;

/// <summary>
/// Thrown when a strategy calls an MQL5 built-in this runtime deliberately refuses
/// to execute: file I/O, network access, DLL imports and terminal
/// control.
///
/// Refusal is loud on purpose. Returning a plausible success value for
/// <c>FileOpen</c> or <c>WebRequest</c> would let a converted strategy carry on with
/// state it never actually read, which is worse than stopping: the backtest would be
/// silently wrong rather than obviously incomplete.
/// </summary>
public sealed class Mql5UnsupportedOperationException : InvalidOperationException
{
    /// <summary>Creates the exception with no named function.</summary>
    public Mql5UnsupportedOperationException()
        : base("The MQL5 built-in is not supported by this runtime.")
    {
        FunctionName = string.Empty;
    }

    /// <summary>Creates the exception with an explicit message.</summary>
    public Mql5UnsupportedOperationException(string message)
        : base(message)
    {
        FunctionName = string.Empty;
    }

    /// <summary>Creates the exception with an explicit message and inner cause.</summary>
    public Mql5UnsupportedOperationException(string message, Exception innerException)
        : base(message, innerException)
    {
        FunctionName = string.Empty;
    }

    private Mql5UnsupportedOperationException(string functionName, string reason)
        : base($"MQL5 built-in '{functionName}' is not supported by this runtime: {reason}")
    {
        FunctionName = functionName;
    }

    /// <summary>The MQL5 name of the refused built-in, or empty when none was named.</summary>
    public string FunctionName { get; }

    /// <summary>
    /// Builds the exception for <paramref name="functionName"/>, recording why the
    /// runtime refuses it.
    /// </summary>
    public static Mql5UnsupportedOperationException For(string functionName, string reason)
        => new(functionName, reason);
}
