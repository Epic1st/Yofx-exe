using YO4X.Mql5.Engine.Trading;

namespace YO4X.Mql5.Engine.Hosting;

/// <summary>
/// Everything that fixes the outcome of a run. Two runs with equal options over an equal feed
/// produce byte-identical reports.
/// </summary>
public sealed class Mql5RunOptions
{
    /// <summary>Gets the contract specification of the traded symbol.</summary>
    public Mql5SymbolSpec Symbol { get; init; } = new();

    /// <summary>Gets the starting balance in the deposit currency.</summary>
    public double InitialDeposit { get; init; } = 10_000.0;

    /// <summary>Gets the deposit currency code.</summary>
    public string DepositCurrency { get; init; } = "USD";

    /// <summary>Gets the account leverage.</summary>
    public int Leverage { get; init; } = 100;

    /// <summary>Gets the account position accounting mode.</summary>
    public Mql5MarginMode MarginMode { get; init; } = Mql5MarginMode.Netting;

    /// <summary>
    /// Gets the default spread in points, used for bars whose own spread column is zero.
    /// </summary>
    public int SpreadPoints { get; init; } = 10;

    /// <summary>
    /// Gets the adverse slippage in points applied to every market fill. Deterministic: the same
    /// number of points is always given up, never a random amount.
    /// </summary>
    public int SlippagePoints { get; init; }

    /// <summary>Gets the commission charged per lot per side, as a positive number.</summary>
    public double CommissionPerLot { get; init; }

    /// <summary>
    /// Gets the margin level percentage below which the broker force-closes positions.
    /// Zero disables the stop out.
    /// </summary>
    public double StopOutLevelPercent { get; init; } = 50.0;

    /// <summary>Gets the maximum number of trade requests honoured on a single tick.</summary>
    public int MaxOrdersPerTick { get; init; } = 32;

    /// <summary>Gets the maximum number of ticks the run loop processes.</summary>
    public int MaxTicks { get; init; } = 1_000_000;

    /// <summary>Gets the maximum number of pending orders that may rest at once.</summary>
    public int MaxPendingOrders { get; init; } = 256;

    /// <summary>
    /// Gets the seed handed to any stochastic component of the run. Present so that a run is
    /// reproducible from options alone; the broker itself is fully deterministic.
    /// </summary>
    public ulong Seed { get; init; }

    /// <summary>
    /// Gets a value indicating whether positions still open when the feed ends are closed at the
    /// final bar's price so the report balances.
    /// </summary>
    public bool CloseOpenPositionsAtEnd { get; init; } = true;
}
