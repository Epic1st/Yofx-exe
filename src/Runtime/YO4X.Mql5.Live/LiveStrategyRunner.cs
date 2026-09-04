using System.Reflection;
using System.Runtime.Loader;
using YO4X.Mql5.CodeGen;
using YO4X.Mql5.Engine.Feed;
using YO4X.Mql5.Engine.Trading;
using YO4X.Mql5.Runtime;
using YO4X.Mt5.ConnectionProbe.Windows;
using YO4X.StrategyGovernance;
using YO4X.StrategyGovernance.Packaging;
using RuntimeStrategy = YO4X.Mql5.Runtime.IMql5Strategy;

namespace YO4X.Mql5.Live;

/// <summary>Why a live strategy stopped, when it stopped.</summary>
public enum LiveStopReason
{
    /// <summary>The operator asked it to stop.</summary>
    Requested = 0,

    /// <summary>The strategy source would not compile.</summary>
    NotCompiled = 1,

    /// <summary>The strategy's own initialisation refused to start.</summary>
    InitRefused = 2,

    /// <summary>The strategy threw while handling a tick.</summary>
    Faulted = 3,
}

/// <summary>The authenticated event cadence used to enter a live strategy.</summary>
public enum LiveTickCadence
{
    BarClose = 0,
    EveryQuote = 1,
}

/// <summary>What a live run did while it was up.</summary>
/// <param name="Reason">Why it stopped.</param>
/// <param name="Detail">A sentence naming the cause, when there is one.</param>
/// <param name="BarsSeen">How many bars closed under this run.</param>
/// <param name="OrdersSent">How many instructions reached the broker.</param>
public sealed record LiveRunOutcome(
    LiveStopReason Reason,
    string? Detail,
    int BarsSeen,
    int OrdersSent);

/// <summary>
/// Compiles one MQL5 strategy and runs it against a live broker session.
///
/// <para>
/// The strategy is ticked when a bar closes, not when a quote arrives. That is the same
/// cadence its backtest used, and matching it is the only reason the backtest says anything
/// about the live run: a strategy fed four times as many ticks would take different trades and
/// the measured result would no longer describe it.
/// </para>
/// </summary>
public sealed class LiveStrategyRunner
{
    private readonly IMql5CompilationHost? host;
    private readonly Action<string> journal;

    /// <summary>Creates a runner.</summary>
    /// <param name="host">The C# compiler used to build the strategy.</param>
    /// <param name="journal">Receives one line per lifecycle event.</param>
    public LiveStrategyRunner(IMql5CompilationHost host, Action<string> journal)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(journal);
        this.host = host;
        this.journal = journal;
    }

    /// <summary>Creates a package-only runner with no source compiler capability.</summary>
    public LiveStrategyRunner(Action<string> journal)
    {
        ArgumentNullException.ThrowIfNull(journal);
        this.journal = journal;
    }

    /// <summary>
    /// Runs a strategy until the token is cancelled or it faults.
    /// </summary>
    /// <param name="source">The MQL5 source to compile.</param>
    /// <param name="broker">The guarded trade client for the account.</param>
    /// <param name="seed">Closed historical bars to start the series from.</param>
    /// <param name="periodMinutes">The bar period the strategy expects.</param>
    /// <param name="digits">The symbol's price precision.</param>
    /// <param name="cancellationToken">Stops the run.</param>
    public async Task<LiveRunOutcome> RunAsync(
        Mql5SourceDocument source,
        IMt5TradeGateway broker,
        IReadOnlyList<Mql5Bar> seed,
        int periodMinutes,
        int digits,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(broker);
        ArgumentNullException.ThrowIfNull(seed);

        IMql5CompilationHost compiler = host
            ?? throw new InvalidOperationException("This runner accepts authenticated packages only.");
        Mql5FrontEndResult front = Mql5FrontEnd.Compile(source);
        if (!front.Succeeded || front.Module is null)
        {
            return new LiveRunOutcome(LiveStopReason.NotCompiled, "the front end could not lower it", 0, 0);
        }

        Mql5CodeGenResult generated = Mql5CodeGenerator.Generate(front.Module, null!);
        if (!generated.Succeeded || generated.CSharpSource is null)
        {
            return new LiveRunOutcome(LiveStopReason.NotCompiled, "the code generator refused it", 0, 0);
        }

        string assemblyName = "YO4X.Live." + Guid.NewGuid().ToString("N")[..8];
        Mql5CompilationResult compiled = compiler.Compile(
            assemblyName,
            [new Mql5GeneratedSource(source.RelativePath, generated.CSharpSource, generated.FullTypeName)]);
        if (!compiled.Succeeded || compiled.AssemblyBytes is null)
        {
            string? first = compiled.Diagnostics
                .FirstOrDefault(diagnostic => diagnostic.Severity == Mql5RestrictedDiagnosticSeverity.Error)
                ?.Message;
            return new LiveRunOutcome(LiveStopReason.NotCompiled, first, 0, 0);
        }

        return await RunCompiledAssemblyAsync(
                compiled.AssemblyBytes,
                generated.FullTypeName,
                assemblyName,
                broker,
                seed,
                periodMinutes,
                digits,
                null,
                LiveTickCadence.BarClose,
                null,
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Runs an already authenticated .yo4x assembly. Production callers use this entry point;
    /// it contains no source parser, code generator, or compiler fallback.
    /// </summary>
    public Task<LiveRunOutcome> RunPackagedAsync(
        Yo4xStrategyManifest manifest,
        byte[] assemblyBytes,
        IMt5TradeGateway broker,
        IReadOnlyList<Mql5Bar> seed,
        int periodMinutes,
        int digits,
        CancellationToken cancellationToken)
        => RunPackagedAsync(
            manifest,
            assemblyBytes,
            broker,
            seed,
            periodMinutes,
            digits,
            new Dictionary<string, string>(StringComparer.Ordinal),
            LiveTickCadence.BarClose,
            cancellationToken);

    /// <summary>Runs a package with validated user inputs and an explicit event cadence.</summary>
    public Task<LiveRunOutcome> RunPackagedAsync(
        Yo4xStrategyManifest manifest,
        byte[] assemblyBytes,
        IMt5TradeGateway broker,
        IReadOnlyList<Mql5Bar> seed,
        int periodMinutes,
        int digits,
        IReadOnlyDictionary<string, string> inputValues,
        LiveTickCadence cadence,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(assemblyBytes);
        string entryTypeName = manifest.EntryTypeName
            ?? throw new InvalidDataException("The .yo4x package has no strategy entry type.");
        return RunCompiledAssemblyAsync(
            assemblyBytes,
            entryTypeName,
            "YO4X.Package." + manifest.StrategyId,
            broker,
            seed,
            periodMinutes,
            digits,
            inputValues,
            cadence,
            null,
            cancellationToken);
    }

    /// <summary>
    /// Runs a package and awaits a caller-owned readiness transition after OnInit succeeds,
    /// before any live quote can enter the strategy.
    /// </summary>
    public Task<LiveRunOutcome> RunPackagedAsync(
        Yo4xStrategyManifest manifest,
        byte[] assemblyBytes,
        IMt5TradeGateway broker,
        IReadOnlyList<Mql5Bar> seed,
        int periodMinutes,
        int digits,
        IReadOnlyDictionary<string, string> inputValues,
        LiveTickCadence cadence,
        Func<CancellationToken, Task> onInitialized,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(onInitialized);
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(assemblyBytes);
        string entryTypeName = manifest.EntryTypeName
            ?? throw new InvalidDataException("The .yo4x package has no strategy entry type.");
        return RunCompiledAssemblyAsync(
            assemblyBytes,
            entryTypeName,
            "YO4X.Package." + manifest.StrategyId,
            broker,
            seed,
            periodMinutes,
            digits,
            inputValues,
            cadence,
            onInitialized,
            cancellationToken);
    }

    private async Task<LiveRunOutcome> RunCompiledAssemblyAsync(
        byte[] assemblyBytes,
        string entryTypeName,
        string assemblyName,
        IMt5TradeGateway broker,
        IReadOnlyList<Mql5Bar> seed,
        int periodMinutes,
        int digits,
        IReadOnlyDictionary<string, string>? inputValues,
        LiveTickCadence cadence,
        Func<CancellationToken, Task>? onInitialized,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(assemblyBytes);
        ArgumentException.ThrowIfNullOrWhiteSpace(entryTypeName);
        ArgumentNullException.ThrowIfNull(broker);
        ArgumentNullException.ThrowIfNull(seed);

        var context = new AssemblyLoadContext(assemblyName, isCollectible: true);
        try
        {
            Assembly assembly = context.LoadFromStream(new MemoryStream(assemblyBytes, writable: false));
            Type? type = assembly.GetType(entryTypeName, throwOnError: false, ignoreCase: false);
            if (type is null)
            {
                return new LiveRunOutcome(LiveStopReason.NotCompiled, "no strategy type was emitted", 0, 0);
            }

            var series = new LiveBarSeries(broker.Symbol, periodMinutes, seed);
            var market = new LiveBrokerContext(series, broker, digits, journal);
            var runtime = new Mql5Runtime(
                market,
                new Mql5RuntimeOptions { LogSink = new JournalLogSink(journal) });
            if (Activator.CreateInstance(type, runtime) is not RuntimeStrategy strategy)
            {
                return new LiveRunOutcome(LiveStopReason.NotCompiled, "the type is not a strategy", 0, 0);
            }


            ApplyInputValues(strategy, type, inputValues);

            return await DriveAsync(
                    strategy, type, series, market, broker, cadence, onInitialized, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            context.Unload();
        }
    }

    private async Task<LiveRunOutcome> DriveAsync(
        RuntimeStrategy strategy,
        Type type,
        LiveBarSeries series,
        LiveBrokerContext market,
        IMt5TradeGateway broker,
        LiveTickCadence cadence,
        Func<CancellationToken, Task>? onInitialized,
        CancellationToken cancellationToken)
    {
        int bars = 0;
        int ordersBefore = 0;

        try
        {
            MethodInfo? declaredInit = type.GetMethod(
                "OnInit",
                BindingFlags.Public | BindingFlags.Instance,
                binder: null,
                Type.EmptyTypes,
                modifiers: null);
            if (declaredInit is { ReturnType.Name: "Int32" })
            {
                int code = (int)(declaredInit.Invoke(strategy, null) ?? 0);
                if (code != 0)
                {
                    try
                    {
                        strategy.OnDeinit(Mql5DeinitReason.InitFailed);
                    }
                    catch (Exception exception) when (exception is not OperationCanceledException)
                    {
                        journal("live: OnDeinit threw: " + exception.Message);
                    }

                    return new LiveRunOutcome(
                        LiveStopReason.InitRefused,
                        $"the strategy's OnInit returned {code}",
                        0,
                        0);
                }
            }
            else
            {
                strategy.OnInit();
            }
        }
        catch (TargetInvocationException exception)
        {
            try
            {
                strategy.OnDeinit(Mql5DeinitReason.InitFailed);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                journal("live: OnDeinit threw: " + ex.Message);
            }

            return new LiveRunOutcome(
                LiveStopReason.InitRefused,
                exception.InnerException?.Message ?? exception.Message,
                0,
                0);
        }

        journal($"live: initialised on {series.Count} seeded bars");
        if (onInitialized is not null)
            await onInitialized(cancellationToken).ConfigureAwait(false);

        // The vendor pushes quotes on its own thread. They are queued rather than handled
        // there, so a slow strategy can never stall the socket reader, and the strategy is
        // only ever entered from this loop.
        var quotes = new System.Collections.Concurrent.ConcurrentQueue<(DateTime Time, double Bid, double Ask)>();
        broker.QuoteObserver = (time, bid, ask) => quotes.Enqueue((time, bid, ask));

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                if (!quotes.TryDequeue(out var quote))
                {
                    await Task.Delay(50, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                bool barClosed = series.Accept(quote.Time, quote.Bid, quote.Ask);
                if (!barClosed && cadence != LiveTickCadence.EveryQuote)
                {
                    continue;
                }

                if (barClosed)
                    bars++;
                try
                {
                    strategy.OnTick();
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    journal("live: strategy faulted: " + exception.Message);
                    return new LiveRunOutcome(
                        LiveStopReason.Faulted,
                        exception.Message,
                        bars,
                        market.OpenPositions.Count + ordersBefore);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // A requested stop, not a failure.
        }
        finally
        {
            broker.QuoteObserver = null;
            try
            {
                strategy.OnDeinit(cancellationToken.IsCancellationRequested
                    ? Mql5DeinitReason.Remove
                    : Mql5DeinitReason.Close);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                journal("live: OnDeinit threw: " + exception.Message);
            }
        }

        return new LiveRunOutcome(LiveStopReason.Requested, null, bars, market.OpenPositions.Count);
    }

    private static void ApplyInputValues(
        RuntimeStrategy strategy,
        Type strategyType,
        IReadOnlyDictionary<string, string>? inputValues)
    {
        if (inputValues is null || inputValues.Count == 0)
            return;

        foreach ((string name, string text) in inputValues)
        {
            FieldInfo field = strategyType.GetField(name, BindingFlags.Public | BindingFlags.Instance)
                ?? throw new InvalidDataException($"The package does not declare input '{name}'.");
            field.SetValue(strategy, ConvertInput(text, field.FieldType, name));
        }
    }

    private static object ConvertInput(string text, Type targetType, string name)
    {
        try
        {
            if (targetType == typeof(string))
                return text;
            if (targetType == typeof(bool))
                return text switch
                {
                    "1" => true,
                    "0" => false,
                    _ => bool.Parse(text),
                };
            if (targetType.IsEnum)
                return Enum.Parse(targetType, text, ignoreCase: true);
            return Convert.ChangeType(text, targetType, System.Globalization.CultureInfo.InvariantCulture)
                ?? throw new InvalidDataException($"Input '{name}' has no value.");
        }
        catch (Exception exception) when (exception is FormatException or InvalidCastException or OverflowException or ArgumentException)
        {
            throw new InvalidDataException($"Input '{name}' is not valid for {targetType.Name}.", exception);
        }
    }

    private sealed class JournalLogSink(Action<string> write) : IMql5LogSink
    {
        public void Log(Mql5LogChannel channel, string message)
        {
            string safe = message.Replace('\r', ' ').Replace('\n', ' ').Trim();
            if (safe.Length > 500) safe = safe[..500];
            write($"strategy:{channel}: {safe}");
        }
    }
}
