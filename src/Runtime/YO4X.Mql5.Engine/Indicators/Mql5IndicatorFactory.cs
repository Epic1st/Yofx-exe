using System.Globalization;
using YO4X.Mql5.Engine.Trading;

namespace YO4X.Mql5.Engine.Indicators;

/// <summary>
/// Builds an indicator from the MQL5 function name and the argument list an EA would pass.
/// </summary>
/// <remarks>
/// Argument lists are accepted in either the bare form <c>iMA(14, 0, MODE_SMA, PRICE_CLOSE)</c> or
/// the full MQL5 form <c>iMA("EURUSD", PERIOD_H1, 14, 0, MODE_SMA, PRICE_CLOSE)</c>. Non-numeric
/// arguments such as the symbol are discarded, and any surplus leading numeric argument, which is
/// the timeframe, is dropped so that the trailing arguments line up with the indicator parameters.
/// </remarks>
public static class Mql5IndicatorFactory
{
    /// <summary>Returns the indicator names the factory understands.</summary>
    public static IReadOnlyList<string> SupportedNames { get; } =
    [
        "iMA",
        "iRSI",
        "iATR",
        "iBands",
        "iMACD",
        "iStochastic",
        "iCCI",
        "iADX",
        "iADXWilder",
        "iStdDev",
        "iMomentum",
        "iWPR",
        "iAO",
        "iDeMarker",
        "iForce",
        "iEnvelopes",
        "iFractals",
        "iAlligator",
        "iSAR",
        "iRVI",
        "iOsMA",
    ];

    /// <summary>
    /// Creates an indicator, or returns <see langword="null"/> when the name is unknown. Never
    /// throws for a malformed argument list.
    /// </summary>
    public static IMql5Indicator? Create(string name, IReadOnlyList<object?> parameters)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        string normalized = Normalize(name);

        return normalized switch
        {
            "ima" => CreateMovingAverage(parameters),
            "irsi" => CreateRsi(parameters),
            "iatr" => CreateAtr(parameters),
            "ibands" => CreateBands(parameters),
            "imacd" => CreateMacd(parameters),
            "istochastic" => CreateStochastic(parameters),
            "icci" => CreateCci(parameters),
            "iadx" => CreateAdx(parameters, "iADX"),
            "iadxwilder" => CreateAdx(parameters, "iADXWilder"),
            "istddev" => CreateStdDev(parameters),
            "imomentum" => CreateMomentum(parameters),
            "iwpr" => CreateWilliamsPercentRange(parameters),
            "iao" => new Mql5AwesomeOscillatorIndicator(),
            "idemarker" => CreateDeMarker(parameters),
            "iforce" => CreateForce(parameters),
            "ienvelopes" => CreateEnvelopes(parameters),
            "ifractals" => new Mql5FractalsIndicator(),
            "ialligator" => CreateAlligator(parameters),
            "isar" => CreateParabolicSar(parameters),
            "irvi" => CreateRelativeVigorIndex(parameters),
            "iosma" => CreateOsMa(parameters),
            _ => null,
        };
    }

    /// <summary>Builds the cache key that makes repeated handle requests share one instance.</summary>
    public static string BuildKey(string name, IReadOnlyList<object?> parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        var builder = new System.Text.StringBuilder(Normalize(name ?? string.Empty));
        foreach (object? parameter in parameters)
        {
            builder.Append('|');
            builder.Append(Describe(parameter));
        }

        return builder.ToString();
    }

    private static string Normalize(string name) => name.Trim().ToLowerInvariant();

    private static string Describe(object? parameter) => parameter switch
    {
        null => "null",
        string text => text,
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => parameter.ToString() ?? string.Empty,
    };

    private static Mql5MovingAverageIndicator CreateMovingAverage(IReadOnlyList<object?> parameters)
    {
        List<double> values = Numeric(parameters, 4);
        return new Mql5MovingAverageIndicator(
            Int(values, 0, 14),
            Int(values, 1, 0),
            (Mql5MaMethod)Int(values, 2, (int)Mql5MaMethod.Sma),
            AppliedPriceAt(values, 3));
    }

    private static Mql5RsiIndicator CreateRsi(IReadOnlyList<object?> parameters)
    {
        List<double> values = Numeric(parameters, 2);
        return new Mql5RsiIndicator(Int(values, 0, 14), AppliedPriceAt(values, 1));
    }

    private static Mql5AtrIndicator CreateAtr(IReadOnlyList<object?> parameters)
    {
        List<double> values = Numeric(parameters, 2);
        int period = Int(values, 0, 14);
        int smoothing = Int(values, 1, (int)Mql5MaMethod.Smma);
        return new Mql5AtrIndicator(period, (Mql5MaMethod)smoothing);
    }

    private static Mql5BandsIndicator CreateBands(IReadOnlyList<object?> parameters)
    {
        List<double> values = Numeric(parameters, 4);
        return new Mql5BandsIndicator(
            Int(values, 0, 20),
            Int(values, 1, 0),
            Double(values, 2, 2.0),
            AppliedPriceAt(values, 3));
    }

    private static Mql5MacdIndicator CreateMacd(IReadOnlyList<object?> parameters)
    {
        List<double> values = Numeric(parameters, 4);
        return new Mql5MacdIndicator(
            Int(values, 0, 12),
            Int(values, 1, 26),
            Int(values, 2, 9),
            AppliedPriceAt(values, 3));
    }

    private static Mql5StochasticIndicator CreateStochastic(IReadOnlyList<object?> parameters)
    {
        List<double> values = Numeric(parameters, 5);
        return new Mql5StochasticIndicator(
            Int(values, 0, 5),
            Int(values, 1, 3),
            Int(values, 2, 3),
            (Mql5MaMethod)Int(values, 3, (int)Mql5MaMethod.Sma),
            (Mql5StochasticPriceField)Int(values, 4, (int)Mql5StochasticPriceField.LowHigh));
    }

    private static Mql5CciIndicator CreateCci(IReadOnlyList<object?> parameters)
    {
        List<double> values = Numeric(parameters, 2);
        return new Mql5CciIndicator(
            Int(values, 0, 14),
            AppliedPriceAt(values, 1, Mql5AppliedPrice.Typical));
    }

    private static Mql5AdxIndicator CreateAdx(IReadOnlyList<object?> parameters, string name)
    {
        List<double> values = Numeric(parameters, 1);
        return new Mql5AdxIndicator(name, Int(values, 0, 14));
    }

    private static Mql5StdDevIndicator CreateStdDev(IReadOnlyList<object?> parameters)
    {
        List<double> values = Numeric(parameters, 4);
        return new Mql5StdDevIndicator(
            Int(values, 0, 20),
            Int(values, 1, 0),
            (Mql5MaMethod)Int(values, 2, (int)Mql5MaMethod.Sma),
            AppliedPriceAt(values, 3));
    }

    private static Mql5MomentumIndicator CreateMomentum(IReadOnlyList<object?> parameters)
    {
        List<double> values = Numeric(parameters, 2);
        return new Mql5MomentumIndicator(Int(values, 0, 14), AppliedPriceAt(values, 1));
    }

    private static Mql5WilliamsPercentRangeIndicator CreateWilliamsPercentRange(IReadOnlyList<object?> parameters)
    {
        List<double> values = Numeric(parameters, 1);
        return new Mql5WilliamsPercentRangeIndicator(Int(values, 0, 14));
    }

    private static Mql5DeMarkerIndicator CreateDeMarker(IReadOnlyList<object?> parameters)
    {
        List<double> values = Numeric(parameters, 1);
        return new Mql5DeMarkerIndicator(Int(values, 0, 14));
    }

    private static Mql5ForceIndexIndicator CreateForce(IReadOnlyList<object?> parameters)
    {
        List<double> values = Numeric(parameters, 3);
        return new Mql5ForceIndexIndicator(
            Int(values, 0, 13),
            (Mql5MaMethod)Int(values, 1, (int)Mql5MaMethod.Sma),
            AppliedVolumeAt(values, 2));
    }

    private static Mql5EnvelopesIndicator CreateEnvelopes(IReadOnlyList<object?> parameters)
    {
        List<double> values = Numeric(parameters, 5);
        return new Mql5EnvelopesIndicator(
            Int(values, 0, 14),
            Int(values, 1, 0),
            (Mql5MaMethod)Int(values, 2, (int)Mql5MaMethod.Sma),
            AppliedPriceAt(values, 3),
            Double(values, 4, 0.1));
    }

    private static Mql5AlligatorIndicator CreateAlligator(IReadOnlyList<object?> parameters)
    {
        List<double> values = Numeric(parameters, 8);
        return new Mql5AlligatorIndicator(
            Int(values, 0, 13),
            Int(values, 1, 8),
            Int(values, 2, 8),
            Int(values, 3, 5),
            Int(values, 4, 5),
            Int(values, 5, 3),
            (Mql5MaMethod)Int(values, 6, (int)Mql5MaMethod.Smma),
            AppliedPriceAt(values, 7, Mql5AppliedPrice.Median));
    }

    private static Mql5ParabolicSarIndicator CreateParabolicSar(IReadOnlyList<object?> parameters)
    {
        List<double> values = Numeric(parameters, 2);
        return new Mql5ParabolicSarIndicator(Double(values, 0, 0.02), Double(values, 1, 0.2));
    }

    private static Mql5RelativeVigorIndexIndicator CreateRelativeVigorIndex(IReadOnlyList<object?> parameters)
    {
        List<double> values = Numeric(parameters, 1);
        return new Mql5RelativeVigorIndexIndicator(Int(values, 0, 10));
    }

    private static Mql5OsMaIndicator CreateOsMa(IReadOnlyList<object?> parameters)
    {
        List<double> values = Numeric(parameters, 4);
        return new Mql5OsMaIndicator(
            Int(values, 0, 12),
            Int(values, 1, 26),
            Int(values, 2, 9),
            AppliedPriceAt(values, 3));
    }

    private static Mql5AppliedVolume AppliedVolumeAt(List<double> values, int index)
    {
        int raw = Int(values, index, (int)Mql5AppliedVolume.Tick);
        return Enum.IsDefined(typeof(Mql5AppliedVolume), raw)
            ? (Mql5AppliedVolume)raw
            : Mql5AppliedVolume.Tick;
    }

    private static Mql5AppliedPrice AppliedPriceAt(
        List<double> values,
        int index,
        Mql5AppliedPrice fallback = Mql5AppliedPrice.Close)
    {
        int raw = Int(values, index, (int)fallback);
        return Enum.IsDefined(typeof(Mql5AppliedPrice), raw) ? (Mql5AppliedPrice)raw : fallback;
    }

    private static int Int(List<double> values, int index, int fallback)
    {
        if (index < 0 || index >= values.Count)
        {
            return fallback;
        }

        double value = values[index];
        return double.IsNaN(value) || double.IsInfinity(value)
            ? fallback
            : (int)Math.Round(value, MidpointRounding.AwayFromZero);
    }

    private static double Double(List<double> values, int index, double fallback)
    {
        if (index < 0 || index >= values.Count)
        {
            return fallback;
        }

        double value = values[index];
        return double.IsNaN(value) || double.IsInfinity(value) ? fallback : value;
    }

    private static List<double> Numeric(IReadOnlyList<object?> parameters, int expected)
    {
        var values = new List<double>();
        if (parameters is null)
        {
            return values;
        }

        foreach (object? parameter in parameters)
        {
            if (TryCoerce(parameter, out double value))
            {
                values.Add(value);
            }
        }

        // Anything beyond the indicator's own arity is a leading symbol or timeframe argument.
        while (values.Count > expected)
        {
            values.RemoveAt(0);
        }

        return values;
    }

    private static bool TryCoerce(object? parameter, out double value)
    {
        value = 0.0;

        switch (parameter)
        {
            case null:
                return false;
            case bool:
                return false;
            case string text:
                return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
            case Enum enumeration:
                value = Convert.ToInt64(enumeration, CultureInfo.InvariantCulture);
                return true;
            case double number:
                value = number;
                return true;
            case float number:
                value = number;
                return true;
            case int number:
                value = number;
                return true;
            case long number:
                value = number;
                return true;
            case short number:
                value = number;
                return true;
            case byte number:
                value = number;
                return true;
            case uint number:
                value = number;
                return true;
            case ulong number:
                value = number;
                return true;
            case decimal number:
                value = (double)number;
                return true;
            default:
                return false;
        }
    }
}
