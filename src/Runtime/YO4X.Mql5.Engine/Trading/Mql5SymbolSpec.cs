namespace YO4X.Mql5.Engine.Trading;

/// <summary>
/// Contract specification for the simulated symbol. Everything the broker needs to price a fill,
/// size a margin requirement and validate a volume lives here.
/// </summary>
public sealed class Mql5SymbolSpec
{
    /// <summary>Gets the symbol name.</summary>
    public string Name { get; init; } = "EURUSD";

    /// <summary>Gets the number of decimal places in a quote.</summary>
    public int Digits { get; init; } = 5;

    /// <summary>Gets the size of one point.</summary>
    public double Point => 1.0 / Math.Pow(10.0, Digits);

    /// <summary>Gets the minimal price change. Equal to <see cref="Point"/> for forex symbols.</summary>
    public double TickSize => Point;

    /// <summary>Gets the units of base currency in one lot.</summary>
    public double ContractSize { get; init; } = 100_000.0;

    /// <summary>
    /// Gets the conversion rate from the profit (quote) currency into the deposit currency.
    /// One for symbols quoted in the deposit currency, which is the default.
    /// </summary>
    public double QuoteToDepositRate { get; init; } = 1.0;

    /// <summary>Gets the smallest tradable volume.</summary>
    public double VolumeMin { get; init; } = 0.01;

    /// <summary>Gets the largest tradable volume.</summary>
    public double VolumeMax { get; init; } = 500.0;

    /// <summary>Gets the volume increment.</summary>
    public double VolumeStep { get; init; } = 0.01;

    /// <summary>Gets the minimum distance in points between the market and a stop or pending price.</summary>
    public int StopsLevelPoints { get; init; }

    /// <summary>Gets the freeze distance in points.</summary>
    public int FreezeLevelPoints { get; init; }

    /// <summary>Gets the swap charged per long lot per rollover, in deposit currency.</summary>
    public double SwapLong { get; init; }

    /// <summary>Gets the swap charged per short lot per rollover, in deposit currency.</summary>
    public double SwapShort { get; init; }

    /// <summary>Gets the value of one tick for one lot, expressed in the deposit currency.</summary>
    public double TickValue => ContractSize * TickSize * QuoteToDepositRate;

    /// <summary>Rounds a price to the symbol's digits.</summary>
    public double NormalizePrice(double price) => Math.Round(price, Digits, MidpointRounding.AwayFromZero);

    /// <summary>Rounds a volume to the symbol's volume step.</summary>
    public double NormalizeVolume(double volume)
    {
        if (VolumeStep <= 0.0)
        {
            return volume;
        }

        double steps = Math.Round(volume / VolumeStep, MidpointRounding.AwayFromZero);
        return Math.Round(steps * VolumeStep, 8, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// Converts a price delta into deposit-currency profit for the given volume.
    /// </summary>
    public double ProfitOf(double priceDelta, double volume) =>
        priceDelta / TickSize * TickValue * volume;

    /// <summary>
    /// Returns the margin required to hold <paramref name="volume"/> lots opened at
    /// <paramref name="price"/> under the supplied leverage.
    /// </summary>
    public double MarginOf(double volume, double price, int leverage)
    {
        if (leverage <= 0)
        {
            return 0.0;
        }

        return volume * ContractSize * price * QuoteToDepositRate / leverage;
    }
}
