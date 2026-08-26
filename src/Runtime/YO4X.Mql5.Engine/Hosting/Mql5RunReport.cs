using YO4X.Mql5.Engine.Trading;

namespace YO4X.Mql5.Engine.Hosting;

/// <summary>The complete, reproducible outcome of one strategy run.</summary>
public sealed record Mql5RunReport
{
    /// <summary>Gets the symbol traded.</summary>
    public required string Symbol { get; init; }

    /// <summary>Gets the code <c>OnInit</c> returned.</summary>
    public required int InitRetcode { get; init; }

    /// <summary>Gets the number of bars actually delivered to <c>OnTick</c>.</summary>
    public required int TicksProcessed { get; init; }

    /// <summary>Gets the number of bars the feed produced, including any skipped by the tick cap.</summary>
    public required int BarsSeen { get; init; }

    /// <summary>Gets the starting balance.</summary>
    public required double InitialDeposit { get; init; }

    /// <summary>Gets the balance after the run.</summary>
    public required double FinalBalance { get; init; }

    /// <summary>Gets the equity after the run.</summary>
    public required double FinalEquity { get; init; }

    /// <summary>Gets the largest peak-to-trough equity decline, in deposit currency.</summary>
    public required double MaxDrawdown { get; init; }

    /// <summary>Gets the largest peak-to-trough equity decline as a percentage of the peak.</summary>
    public required double MaxDrawdownPercent { get; init; }

    /// <summary>Gets the sum of winning net profits.</summary>
    public required double GrossProfit { get; init; }

    /// <summary>Gets the sum of losing net profits, as a positive number.</summary>
    public required double GrossLoss { get; init; }

    /// <summary>
    /// Gets gross profit divided by gross loss. Positive infinity when there were wins and no
    /// losses; zero when there were no wins.
    /// </summary>
    public required double ProfitFactor { get; init; }

    /// <summary>Gets the number of closes, partial closes included.</summary>
    public required int TotalTrades { get; init; }

    /// <summary>Gets the number of closes with a net profit above zero.</summary>
    public required int WinningTrades { get; init; }

    /// <summary>Gets the number of closes with a net profit below zero.</summary>
    public required int LosingTrades { get; init; }

    /// <summary>Gets every close in the order it happened.</summary>
    public required IReadOnlyList<Mql5ClosedTrade> Trades { get; init; }

    /// <summary>Gets every order event with its simulated timestamp.</summary>
    public required IReadOnlyList<Mql5OrderEvent> Events { get; init; }

    /// <summary>Gets the equity sampled once per processed tick.</summary>
    public required IReadOnlyList<double> EquityCurve { get; init; }

    /// <summary>Gets a value indicating whether the per-tick order cap fired.</summary>
    public required bool OrdersPerTickCapTriggered { get; init; }

    /// <summary>Gets a value indicating whether the run stopped because the tick cap was reached.</summary>
    public required bool TickCapTriggered { get; init; }

    /// <summary>Gets a value indicating whether the margin stop out fired.</summary>
    public required bool StopOutTriggered { get; init; }

    /// <summary>
    /// Gets the message of the exception a misbehaving strategy threw, or an empty string. The
    /// host absorbs the fault and finishes the report rather than propagating it.
    /// </summary>
    public required string StrategyFault { get; init; }

    /// <summary>Gets a value indicating whether the run completed without a cap or a fault.</summary>
    public bool CompletedCleanly =>
        StrategyFault.Length == 0 && !TickCapTriggered && !OrdersPerTickCapTriggered;
}
