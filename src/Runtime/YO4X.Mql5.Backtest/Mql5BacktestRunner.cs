using System.Reflection;
using System.Runtime.Loader;
using YO4X.Mql5.CodeGen;
using YO4X.Mql5.Compilation;
using YO4X.Mql5.Engine.Feed;
using YO4X.Mql5.Engine.Hosting;
using YO4X.Mql5.Engine.Trading;
using YO4X.Mql5.Runtime;
using YO4X.StrategyGovernance;
using EngineContextInterface = YO4X.Mql5.Engine.Context.IMql5MarketContext;
using EngineMarketContext = YO4X.Mql5.Engine.Context.Mql5MarketContext;
using EngineStrategy = YO4X.Mql5.Engine.Hosting.IMql5Strategy;
using RuntimeStrategy = YO4X.Mql5.Runtime.IMql5Strategy;

namespace YO4X.Mql5.Backtest;

/// <summary>Why a backtest could not be produced, when it could not.</summary>
public enum Mql5BacktestOutcome
{
    /// <summary>The strategy ran to completion and the report is present.</summary>
    Completed,

    /// <summary>The MQL5 front end could not parse or lower the source.</summary>
    NotLowered,

    /// <summary>The code generator refused the module.</summary>
    NotGenerated,

    /// <summary>The generated C# did not compile.</summary>
    NotCompiled,

    /// <summary>The compiled assembly did not expose a usable strategy type.</summary>
    NotLoadable,

    /// <summary>The strategy's own OnInit refused to start.</summary>
    InitRefused,
}

/// <summary>One backtest attempt: what happened, and the report if one exists.</summary>
public sealed record Mql5BacktestResult(
    Mql5BacktestOutcome Outcome,
    Mql5RunReport? Report,
    IReadOnlyList<Mql5RestrictedDiagnostic> Diagnostics,
    string? Detail)
{
    /// <summary>True when a report was produced.</summary>
    public bool Succeeded => Outcome == Mql5BacktestOutcome.Completed && Report is not null;

    /// <summary>The first error diagnostic, if any, as a short sentence.</summary>
    public string Explain()
    {
        if (Detail is { Length: > 0 })
        {
            return Detail;
        }

        foreach (Mql5RestrictedDiagnostic diagnostic in Diagnostics)
        {
            if (diagnostic.Severity == Mql5RestrictedDiagnosticSeverity.Error)
            {
                return $"{diagnostic.Code} at line {diagnostic.Line}: {diagnostic.Message}";
            }
        }

        return Outcome.ToString();
    }
}

/// <summary>
/// Compiles one MQL5 source file and runs it against the offline engine over a bar feed.
///
/// <para>
/// Each strategy is compiled into its own assembly and loaded into its own collectible
/// context, so a sweep over a large corpus does not accumulate assemblies for the whole
/// run, and one strategy's types can never satisfy another's lookup.
/// </para>
/// </summary>
public static class Mql5BacktestRunner
{
    /// <summary>
    /// Compiles <paramref name="source"/> and runs it over <paramref name="feed"/>.
    /// </summary>
    /// <param name="source">The MQL5 source document to compile.</param>
    /// <param name="feed">The bars to replay.</param>
    /// <param name="options">Account, symbol and execution settings for the run.</param>
    /// <param name="periodMinutes">The feed's bar period, in minutes.</param>
    /// <param name="host">The C# compiler to use.</param>
    public static Mql5BacktestResult Run(
        Mql5SourceDocument source,
        IMql5MarketFeed feed,
        Mql5RunOptions options,
        int periodMinutes,
        IMql5CompilationHost host)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(feed);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(host);

        Mql5FrontEndResult front = Mql5FrontEnd.Compile(source);
        if (!front.Succeeded || front.Module is null)
        {
            return new Mql5BacktestResult(
                Mql5BacktestOutcome.NotLowered,
                null,
                front.Diagnostics,
                null);
        }

        Mql5CodeGenResult generated = Mql5CodeGenerator.Generate(front.Module, null!);
        if (!generated.Succeeded || generated.CSharpSource is null)
        {
            return new Mql5BacktestResult(
                Mql5BacktestOutcome.NotGenerated,
                null,
                generated.Diagnostics,
                null);
        }

        string assemblyName = "YO4X.Backtest." + Sanitize(source.RelativePath);
        Mql5CompilationResult compiled = host.Compile(
            assemblyName,
            [new Mql5GeneratedSource(source.RelativePath, generated.CSharpSource, generated.FullTypeName)]);
        if (!compiled.Succeeded || compiled.AssemblyBytes is null)
        {
            return new Mql5BacktestResult(
                Mql5BacktestOutcome.NotCompiled,
                null,
                compiled.Diagnostics,
                null);
        }

        var context = new AssemblyLoadContext(assemblyName, isCollectible: true);
        try
        {
            Assembly assembly = context.LoadFromStream(new MemoryStream(compiled.AssemblyBytes));
            Type? strategyType = assembly.GetType(generated.FullTypeName, throwOnError: false);
            if (strategyType is null)
            {
                return new Mql5BacktestResult(
                    Mql5BacktestOutcome.NotLoadable,
                    null,
                    compiled.Diagnostics,
                    "The compiled assembly does not declare " + generated.FullTypeName + ".");
            }

            var adapter = new GeneratedStrategyAdapter(strategyType, periodMinutes);
            Mql5RunReport report = Mql5StrategyHost.Run(adapter, feed, options);
            return new Mql5BacktestResult(
                report.InitRetcode == Mql5InitCode.Succeeded
                    ? Mql5BacktestOutcome.Completed
                    : Mql5BacktestOutcome.InitRefused,
                report,
                compiled.Diagnostics,
                adapter.ConstructionFault);
        }
        finally
        {
            context.Unload();
        }
    }

    private static string Sanitize(string path)
    {
        Span<char> buffer = stackalloc char[Math.Min(path.Length, 64)];
        int written = 0;
        foreach (char character in path)
        {
            if (written == buffer.Length)
            {
                break;
            }

            buffer[written++] = char.IsAsciiLetterOrDigit(character) ? character : '_';
        }

        return written == 0 ? "strategy" : new string(buffer[..written]);
    }

    /// <summary>
    /// Presents a generated strategy to the engine's host.
    ///
    /// <para>
    /// The two sides disagree about shape: the engine hands a context to every entry point,
    /// while a generated strategy is constructed with its runtime and then called with no
    /// arguments. The strategy therefore cannot exist until the host produces its context,
    /// which is why it is built on the first <c>OnInit</c> rather than up front.
    /// </para>
    /// </summary>
    private sealed class GeneratedStrategyAdapter(Type strategyType, int periodMinutes) : EngineStrategy
    {
        private RuntimeStrategy? strategy;
        private MethodInfo? declaredInit;
        private object? instance;

        /// <summary>Set when the strategy could not be constructed at all.</summary>
        public string? ConstructionFault { get; private set; }

        public int OnInit(EngineContextInterface context)
        {
            if (context is not EngineMarketContext engineContext)
            {
                ConstructionFault = "The engine supplied an unexpected market context type.";
                return Mql5InitCode.Failed;
            }

            try
            {
                var runtime = new Mql5Runtime(new EngineRuntimeContext(engineContext, periodMinutes));
                instance = Activator.CreateInstance(strategyType, runtime);
                strategy = instance as RuntimeStrategy;
            }
            catch (Exception exception) when (exception is MissingMethodException
                or TargetInvocationException
                or InvalidOperationException)
            {
                ConstructionFault = "The strategy could not be constructed: " + exception.Message;
                return Mql5InitCode.Failed;
            }

            if (strategy is null)
            {
                ConstructionFault = "The compiled type does not implement the runtime strategy contract.";
                return Mql5InitCode.Failed;
            }

            // The generated interface method discards the MQL5 return code, so the concrete
            // class's own OnInit is preferred when it declares one. An EA that answers
            // INIT_FAILED must be allowed to stop the run rather than trade anyway.
            declaredInit = strategyType.GetMethod(
                "OnInit",
                BindingFlags.Public | BindingFlags.Instance,
                binder: null,
                Type.EmptyTypes,
                modifiers: null);

            try
            {
                if (declaredInit is not null && declaredInit.ReturnType == typeof(int))
                {
                    return (int)(declaredInit.Invoke(instance, null) ?? Mql5InitCode.Succeeded);
                }

                strategy.OnInit();
                return Mql5InitCode.Succeeded;
            }
            catch (TargetInvocationException exception)
            {
                ConstructionFault = "OnInit threw: " + (exception.InnerException?.Message ?? exception.Message);
                return Mql5InitCode.Failed;
            }
        }

        public void OnTick(EngineContextInterface context) => strategy?.OnTick();

        public void OnDeinit(EngineContextInterface context, int reason) => strategy?.OnDeinit(reason);
    }
}
