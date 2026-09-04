using System.Runtime.CompilerServices;

namespace YO4X.Mql5.Runtime;

/// <summary>
/// The MQL5 standard library, as a callable C# surface.
///
/// A converted strategy calls nothing but this interface. Grouping every built-in
/// behind one object is what makes the untrusted-source rule enforceable: the
/// generated code has no other way to reach a clock, a price, a file or a socket, so
/// auditing the runtime audits every strategy.
///
/// The surface is split across partial declarations, one per MQL5 documentation
/// chapter, and each member carries which of the five support levels it belongs to:
///
/// <list type="bullet">
/// <item><description><b>Native</b> - implemented here, with no market context. Maths,
/// strings, arrays, conversions and diagnostics.</description></item>
/// <item><description><b>EngineBound</b> - delegated to
/// <see cref="IMql5MarketContext"/>. Symbol, account, positions, orders, trading and
/// the clock.</description></item>
/// <item><description><b>IndicatorBound</b> - delegated to
/// <see cref="IMql5MarketContext"/> as a handle request. No indicator mathematics
/// lives here.</description></item>
/// <item><description><b>ChartStub</b> - visual only. Recorded, answered from the
/// recording, and never drawn.</description></item>
/// <item><description><b>Unsupported</b> - file I/O, network, DLL imports and
/// terminal control. Throws <see cref="Mql5UnsupportedOperationException"/>; never
/// silently succeeds.</description></item>
/// </list>
///
/// A supported built-in never throws. Where MQL5 documents a failure value - 0 from
/// <c>ArraySize</c> on an unallocated array, -1 from <c>StringFind</c> on a miss,
/// <c>false</c> from <c>PositionSelect</c> when nothing is open - that value is
/// returned instead.
/// </summary>
public partial interface IMql5Runtime
{
    /// <summary>
    /// The last error code, as MQL5's <c>GetLastError</c> reports it. Errors raised by
    /// the runtime itself are recorded here rather than thrown.
    /// </summary>
    int LastError { get; }

    /// <summary>The recording behind the <c>Object*</c> and <c>Chart*</c> stubs.</summary>
    Mql5ChartObjectStore ChartObjects { get; }
}

/// <summary>
/// The default <see cref="IMql5Runtime"/>: MQL5's <c>Native</c> surface implemented in
/// C#, everything market-facing delegated to an <see cref="IMql5MarketContext"/>.
///
/// One instance belongs to one strategy run. The instance holds the pseudo-random
/// state, the last error code, the per-array timeseries flags and the chart-object
/// recording, so two strategies running side by side cannot see each other's state.
/// The type is not thread safe, mirroring MQL5, where one program runs on one thread.
/// </summary>
public sealed partial class Mql5Runtime : IMql5Runtime
{
    private readonly IMql5MarketContext context;
    private readonly Mql5RuntimeOptions options;
    private readonly IMql5LogSink log;
    private readonly ConditionalWeakTable<object, SeriesFlag> seriesFlags = [];
    private readonly Dictionary<string, int> indicatorHandles = new(StringComparer.Ordinal);
    private uint randomState;
    private long? clockBaseline;

    /// <summary>Creates a runtime bound to <paramref name="context"/> with default policy.</summary>
    public Mql5Runtime(IMql5MarketContext context)
        : this(context, Mql5RuntimeOptions.Default)
    {
    }

    /// <summary>Creates a runtime bound to <paramref name="context"/> under <paramref name="options"/>.</summary>
    public Mql5Runtime(IMql5MarketContext context, Mql5RuntimeOptions options)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(options);

        this.context = context;
        this.options = options;
        log = options.LogSink;
        randomState = options.RandomSeed;
        ChartObjects = new Mql5ChartObjectStore();
    }

    /// <inheritdoc />
    public int LastError { get; private set; }

    /// <inheritdoc />
    public Mql5ChartObjectStore ChartObjects { get; }

    /// <summary>
    /// Records an MQL5 error code without throwing. Supported built-ins report failure
    /// through their return value and this field, exactly as MQL5 does.
    /// </summary>
    private void SetError(int code) => LastError = code;

    private void Emit(Mql5LogChannel channel, string message) => log.Log(channel, message);

    private void RecordChartCall(string function)
    {
        if (options.LogChartCalls)
        {
            log.Log(Mql5LogChannel.Chart, function);
        }
    }

    private long ResolveChartId(long chartId) => chartId == 0 ? options.ChartId : chartId;

    private bool IsSeriesArray(object? array)
        => array is not null && seriesFlags.TryGetValue(array, out SeriesFlag? flag) && flag.Value;

    private void SetSeriesArray(object array, bool value)
    {
        if (seriesFlags.TryGetValue(array, out SeriesFlag? flag))
        {
            flag.Value = value;
            return;
        }

        seriesFlags.Add(array, new SeriesFlag { Value = value });
    }

    /// <summary>
    /// Moves the timeseries flag onto an array that has replaced another.
    /// </summary>
    /// <remarks>
    /// <see cref="Array.Resize"/> allocates a new object rather than growing the old one,
    /// and <see cref="seriesFlags"/> is keyed by identity, so a reallocated buffer starts
    /// with no flag at all. Left alone that is silent: a strategy that called
    /// <c>ArraySetAsSeries</c> and then copied into the buffer would index it forward from
    /// the oldest bar instead of backward from the newest, inverting every signal without
    /// raising anything.
    /// </remarks>
    private void CarrySeriesFlag(object? previous, object? replacement)
    {
        if (previous is null || replacement is null || ReferenceEquals(previous, replacement))
        {
            return;
        }

        if (IsSeriesArray(previous))
        {
            SetSeriesArray(replacement, true);
        }
    }

    private static Mql5UnsupportedOperationException Refuse(string function, string reason)
        => Mql5UnsupportedOperationException.For(function, reason);

    private sealed class SeriesFlag
    {
        public bool Value { get; set; }
    }
}
