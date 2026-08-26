using System.Reflection;
using System.Runtime.Loader;
using YO4X.Mql5.CodeGen;
using YO4X.Mql5.Engine.Feed;
using YO4X.Mql5.Runtime;
using YO4X.Mt5.ConnectionProbe.Windows;
using YO4X.StrategyGovernance;
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
    private readonly IMql5CompilationHost host;
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
        Mt5NetApiDemoTradeClient broker,
        IReadOnlyList<Mql5Bar> seed,
        int periodMinutes,
        int digits,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(broker);
        ArgumentNullException.ThrowIfNull(seed);

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
        Mql5CompilationResult compiled = host.Compile(
            assemblyName,
            [new Mql5GeneratedSource(source.RelativePath, generated.CSharpSource, generated.FullTypeName)]);
        if (!compiled.Succeeded || compiled.AssemblyBytes is null)
        {
            string? first = compiled.Diagnostics
                .FirstOrDefault(diagnostic => diagnostic.Severity == Mql5RestrictedDiagnosticSeverity.Error)
                ?.Message;
            return new LiveRunOutcome(LiveStopReason.NotCompiled, first, 0, 0);
        }

        var context = new AssemblyLoadContext(assemblyName, isCollectible: true);
        try
        {
            Assembly assembly = context.LoadFromStream(new MemoryStream(compiled.AssemblyBytes));
            Type? type = assembly.GetType(generated.FullTypeName, throwOnError: false);
            if (type is null)
            {
                return new LiveRunOutcome(LiveStopReason.NotCompiled, "no strategy type was emitted", 0, 0);
            }

            var series = new LiveBarSeries(broker.Symbol, periodMinutes, seed);
            var market = new LiveBrokerContext(series, broker, digits, journal);
            var runtime = new Mql5Runtime(market);
            if (Activator.CreateInstance(type, runtime) is not RuntimeStrategy strategy)
            {
                return new LiveRunOutcome(LiveStopReason.NotCompiled, "the type is not a strategy", 0, 0);
            }

            return await DriveAsync(strategy, type, series, market, broker, cancellationToken)
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
        Mt5NetApiDemoTradeClient broker,
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
            return new LiveRunOutcome(
                LiveStopReason.InitRefused,
                exception.InnerException?.Message ?? exception.Message,
                0,
                0);
        }

        journal($"live: initialised on {series.Count} seeded bars");

        // The vendor pushes quotes on its own thread. They are queued rather than handled
        // there, so a slow strategy can never stall the socket reader, and the strategy is
        // only ever entered from this loop.
        var closes = new System.Collections.Concurrent.ConcurrentQueue<bool>();
        broker.QuoteObserver = (time, bid, ask) =>
        {
            if (series.Accept(time, bid, ask))
            {
                closes.Enqueue(true);
            }
        };

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                if (!closes.TryDequeue(out _))
                {
                    await Task.Delay(50, cancellationToken).ConfigureAwait(false);
                    continue;
                }

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
                strategy.OnDeinit(0);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                journal("live: OnDeinit threw: " + exception.Message);
            }
        }

        return new LiveRunOutcome(LiveStopReason.Requested, null, bars, market.OpenPositions.Count);
    }
}
