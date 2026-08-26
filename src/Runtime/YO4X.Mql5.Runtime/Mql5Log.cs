using System.Collections.Concurrent;

namespace YO4X.Mql5.Runtime;

/// <summary>Which MQL5 output surface a message was written to.</summary>
public enum Mql5LogChannel
{
    /// <summary><c>Print</c> and <c>PrintFormat</c>: the expert journal.</summary>
    Print,

    /// <summary><c>Comment</c>: the chart comment area. Visual only.</summary>
    Comment,

    /// <summary><c>Alert</c> and <c>MessageBox</c> text: a terminal dialog. Visual only.</summary>
    Alert,

    /// <summary><c>ArrayPrint</c>: the tabular array dump.</summary>
    ArrayPrint,

    /// <summary>A chart-drawing call recorded by a chart-stub built-in.</summary>
    Chart,

    /// <summary><c>PlaySound</c>: an audible cue the engine cannot produce.</summary>
    Sound
}

/// <summary>
/// Where the runtime sends everything a strategy would have written to the terminal.
///
/// The runtime never touches <c>System.Console</c>, a file or a socket. Generated
/// strategies are derived from untrusted third-party source, so every output path is
/// a call into a sink the host chose.
/// </summary>
public interface IMql5LogSink
{
    /// <summary>Records one message on <paramref name="channel"/>.</summary>
    void Log(Mql5LogChannel channel, string message);
}

/// <summary>A sink that discards everything. The default when the host supplies none.</summary>
public sealed class NullMql5LogSink : IMql5LogSink
{
    /// <summary>The shared instance.</summary>
    public static NullMql5LogSink Instance { get; } = new();

    private NullMql5LogSink()
    {
    }

    /// <inheritdoc />
    public void Log(Mql5LogChannel channel, string message)
    {
        // Deliberately empty: the null sink exists so the runtime never has to
        // branch on a missing sink.
    }
}

/// <summary>One recorded line of strategy output.</summary>
/// <param name="Channel">The MQL5 surface the strategy wrote to.</param>
/// <param name="Message">The rendered text.</param>
public readonly record struct Mql5LogEntry(Mql5LogChannel Channel, string Message);

/// <summary>
/// An in-memory sink that keeps the most recent <see cref="Capacity"/> entries.
///
/// Bounded on purpose: a strategy that prints on every tick would otherwise grow the
/// host's heap without limit over a long backtest.
/// </summary>
public sealed class Mql5LogRecorder : IMql5LogSink
{
    private readonly ConcurrentQueue<Mql5LogEntry> entries = new();
    private int count;

    /// <summary>Creates a recorder holding at most <paramref name="capacity"/> entries.</summary>
    public Mql5LogRecorder(int capacity = 4096)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        Capacity = capacity;
    }

    /// <summary>The maximum number of entries retained.</summary>
    public int Capacity { get; }

    /// <summary>The retained entries, oldest first.</summary>
    public IReadOnlyList<Mql5LogEntry> Entries => [.. entries];

    /// <inheritdoc />
    public void Log(Mql5LogChannel channel, string message)
    {
        entries.Enqueue(new Mql5LogEntry(channel, message));

        if (Interlocked.Increment(ref count) <= Capacity)
        {
            return;
        }

        if (entries.TryDequeue(out _))
        {
            Interlocked.Decrement(ref count);
        }
        else
        {
            Interlocked.Decrement(ref count);
        }
    }
}
