using YO4X.Mql5.Engine.Feed;
using YO4X.Mql5.Engine.Indicators;
using YO4X.Mql5.Engine.Trading;

namespace YO4X.Mql5.Engine.Tests;

/// <summary>
/// Accuracy tests for the indicators added on top of the original seven. As in
/// <see cref="IndicatorAccuracyTests"/>, every expectation is arithmetic on the input bars written
/// out in the comment above the assertion; nothing here compares an indicator against itself.
/// </summary>
public sealed class IndicatorExpansionAccuracyTests
{
    private static void Feed(IMql5Indicator indicator, IEnumerable<Mql5Bar> bars)
    {
        foreach (Mql5Bar bar in bars)
        {
            indicator.Append(bar);
        }
    }

    private static List<double> RunCloses(IMql5Indicator indicator, double[] closes, int buffer = 0)
    {
        var values = new List<double>(closes.Length);
        for (int index = 0; index < closes.Length; index++)
        {
            indicator.Append(EngineTestSupport.Flat(index, closes[index]));
            values.Add(indicator.Value(buffer, 0));
        }

        return values;
    }

    private static List<double> RunBars(IMql5Indicator indicator, List<Mql5Bar> bars, int buffer = 0)
    {
        var values = new List<double>(bars.Count);
        foreach (Mql5Bar bar in bars)
        {
            indicator.Append(bar);
            values.Add(indicator.Value(buffer, 0));
        }

        return values;
    }

    // --- iADX -------------------------------------------------------------------------------

    /// <summary>
    /// Bars, with the directional movement and true range worked out by hand:
    ///   b0  H10  L8   C9
    ///   b1  H12  L9   C11   up = 12-10 = 2, down = 8-9 = -1  -> +DM 2, -DM 0
    ///                       TR = max(12-9, |12-9|, |9-9|) = 3
    ///   b2  H11  L7   C8    up = 11-12 = -1, down = 9-7 = 2  -> +DM 0, -DM 2
    ///                       TR = max(11-7, |11-11|, |7-11|) = 4
    ///   b3  H13  L10  C12   up = 13-11 = 2, down = 7-10 = -3 -> +DM 2, -DM 0
    ///                       TR = max(13-10, |13-8|, |10-8|) = 5
    /// </summary>
    private static List<Mql5Bar> AdxBars() =>
    [
        EngineTestSupport.Bar(0, 9.0, 10.0, 8.0, 9.0),
        EngineTestSupport.Bar(1, 9.0, 12.0, 9.0, 11.0),
        EngineTestSupport.Bar(2, 11.0, 11.0, 7.0, 8.0),
        EngineTestSupport.Bar(3, 8.0, 13.0, 10.0, 12.0),
    ];

    [Fact]
    public void AdxDirectionalIndicesMatchWilderByHand()
    {
        var indicator = new Mql5AdxIndicator(2);
        Feed(indicator, AdxBars());

        // Wilder seeds on the mean of the first two samples, then averages recursively:
        //   TR   seed = (3 + 4) / 2 = 3.5     then (3.5 + 5) / 2 = 4.25
        //   +DM  seed = (2 + 0) / 2 = 1.0     then (1.0 + 2) / 2 = 1.5
        //   -DM  seed = (0 + 2) / 2 = 1.0     then (1.0 + 0) / 2 = 0.5
        // +DI = 100 * 1.5 / 4.25 = 600 / 17,  -DI = 100 * 0.5 / 4.25 = 200 / 17
        Assert.Equal(600.0 / 17.0, indicator.Value(1, 0), 10);
        Assert.Equal(200.0 / 17.0, indicator.Value(2, 0), 10);

        // One bar back both indices were 100 * 1.0 / 3.5 = 200 / 7.
        Assert.Equal(200.0 / 7.0, indicator.Value(1, 1), 10);
        Assert.Equal(200.0 / 7.0, indicator.Value(2, 1), 10);
    }

    [Fact]
    public void AdxMainLineIsTheSmoothedDirectionalIndex()
    {
        var indicator = new Mql5AdxIndicator(2);
        Feed(indicator, AdxBars());

        // DX on bar 2: +DI equals -DI so |difference| is zero -> DX = 0.
        // DX on bar 3: 100 * |600/17 - 200/17| / (800/17) = 100 * 400 / 800 = 50.
        // ADX is the Wilder average of DX, seeded on the mean of the first two: (0 + 50) / 2 = 25.
        Assert.Equal(25.0, indicator.Value(0, 0), 10);

        // Only one DX sample existed on bar 2, so the main line had not formed yet.
        Assert.Equal(Mql5IndicatorBase.EmptyValue, indicator.Value(0, 1));

        // Bar 0 has no previous bar at all: no true range, no directional movement.
        Assert.Equal(Mql5IndicatorBase.EmptyValue, indicator.Value(1, 3));
        Assert.Equal(Mql5IndicatorBase.EmptyValue, indicator.Value(2, 3));
    }

    [Fact]
    public void AdxExposesThreeBuffersInMetaTraderOrder()
    {
        var indicator = new Mql5AdxIndicator(14);

        Assert.Equal("iADX", indicator.Name);
        Assert.Equal(3, indicator.BufferCount);
        Assert.Equal(14, indicator.Period);
    }

    // --- iStdDev ----------------------------------------------------------------------------

    [Fact]
    public void StandardDeviationUsesThePopulationDivisorAroundTheMovingAverage()
    {
        double[] closes = [10.0, 12.0, 11.0, 14.0];
        List<double> values = RunCloses(
            new Mql5StdDevIndicator(3, 0, Mql5MaMethod.Sma, Mql5AppliedPrice.Close),
            closes);

        Assert.Equal(Mql5IndicatorBase.EmptyValue, values[1]);

        // Window 10, 12, 11: mean 11.
        // variance = ((-1)^2 + 1^2 + 0^2) / 3 = 2/3
        Assert.Equal(Math.Sqrt(2.0 / 3.0), values[2], 10);

        // Window 12, 11, 14: mean 37/3.
        // deviations -1/3, -4/3, 5/3 -> (1 + 16 + 25) / 9 = 42/9, divided by 3 = 14/9
        Assert.Equal(Math.Sqrt(14.0 / 9.0), values[3], 10);
    }

    [Fact]
    public void StandardDeviationShiftDisplacesTheSeries()
    {
        double[] closes = [10.0, 12.0, 11.0, 14.0];
        List<double> shifted = RunCloses(
            new Mql5StdDevIndicator(3, 1, Mql5MaMethod.Sma, Mql5AppliedPrice.Close),
            closes);

        // The bar-2 deviation, sqrt(2/3), is republished one bar later.
        Assert.Equal(Math.Sqrt(2.0 / 3.0), shifted[3], 10);
        Assert.Equal(Mql5IndicatorBase.EmptyValue, shifted[2]);
    }

    // --- iMomentum --------------------------------------------------------------------------

    [Fact]
    public void MomentumIsTheRatioOfPriceToThePriceOnePeriodBack()
    {
        double[] closes = [10.0, 12.0, 11.0, 15.0, 14.0];
        List<double> values = RunCloses(
            new Mql5MomentumIndicator(3, Mql5AppliedPrice.Close),
            closes);

        Assert.Equal(Mql5IndicatorBase.EmptyValue, values[2]);
        Assert.Equal(150.0, values[3], 10);              // 100 * 15 / 10
        Assert.Equal(100.0 * 14.0 / 12.0, values[4], 10);
    }

    // --- iWPR -------------------------------------------------------------------------------

    [Fact]
    public void WilliamsPercentRangeRunsFromMinusOneHundredToZero()
    {
        List<Mql5Bar> bars =
        [
            EngineTestSupport.Bar(0, 9.0, 10.0, 8.0, 9.0),
            EngineTestSupport.Bar(1, 9.0, 12.0, 9.0, 11.0),
            EngineTestSupport.Bar(2, 11.0, 11.0, 7.0, 8.0),
            EngineTestSupport.Bar(3, 8.0, 13.0, 10.0, 13.0),
        ];

        List<double> values = RunBars(new Mql5WilliamsPercentRangeIndicator(3), bars);

        Assert.Equal(Mql5IndicatorBase.EmptyValue, values[1]);

        // Bars 0..2: highest 12, lowest 7, close 8 -> -100 * (12 - 8) / (12 - 7) = -80
        Assert.Equal(-80.0, values[2], 10);

        // Bars 1..3: highest 13, lowest 7, close 13 -> the top of the range reads as zero
        Assert.Equal(0.0, values[3], 10);
    }

    // --- iAO --------------------------------------------------------------------------------

    [Fact]
    public void AwesomeOscillatorIsTheFiveMinusThirtyFourBarMedianAverage()
    {
        // Bar i has high i+1 and low i-1, so its median price is exactly i.
        var bars = new List<Mql5Bar>();
        for (int index = 0; index < 40; index++)
        {
            bars.Add(EngineTestSupport.Bar(index, index, index + 1.0, index - 1.0, index));
        }

        List<double> values = RunBars(new Mql5AwesomeOscillatorIndicator(), bars);

        // Nothing before the thirty-fourth bar.
        Assert.Equal(Mql5IndicatorBase.EmptyValue, values[32]);

        // SMA(5) of medians at bar i is i - 2, SMA(34) is i - 16.5, so the oscillator sits at
        // (i - 2) - (i - 16.5) = 14.5 for every formed bar of a straight ramp.
        Assert.Equal(14.5, values[33], 10);
        Assert.Equal(14.5, values[39], 10);
    }

    // --- iDeMarker --------------------------------------------------------------------------

    [Fact]
    public void DeMarkerAveragesTheRisingHighsAgainstTheFallingLows()
    {
        List<Mql5Bar> bars =
        [
            EngineTestSupport.Bar(0, 9.0, 10.0, 8.0, 9.0),
            EngineTestSupport.Bar(1, 9.0, 11.0, 9.0, 10.0),     // DeMax 11-10 = 1, DeMin 0 (low rose)
            EngineTestSupport.Bar(2, 10.0, 10.5, 7.0, 8.0),     // DeMax 0 (high fell), DeMin 9-7 = 2
            EngineTestSupport.Bar(3, 8.0, 12.0, 8.0, 11.0),     // DeMax 12-10.5 = 1.5, DeMin 0
        ];

        List<double> values = RunBars(new Mql5DeMarkerIndicator(2), bars);

        Assert.Equal(Mql5IndicatorBase.EmptyValue, values[1]);

        // SMA(2) of DeMax = (1 + 0) / 2 = 0.5, of DeMin = (0 + 2) / 2 = 1.0
        // DeMarker = 0.5 / (0.5 + 1.0) = 1/3
        Assert.Equal(1.0 / 3.0, values[2], 10);

        // SMA(2) of DeMax = (0 + 1.5) / 2 = 0.75, of DeMin = (2 + 0) / 2 = 1.0
        // DeMarker = 0.75 / 1.75 = 3/7
        Assert.Equal(3.0 / 7.0, values[3], 10);
    }

    // --- iForce -----------------------------------------------------------------------------

    [Fact]
    public void ForceIndexIsVolumeTimesTheMovingAverageStep()
    {
        // EngineTestSupport bars all carry a tick volume of 100.
        double[] closes = [10.0, 12.0, 11.0];
        List<double> values = RunCloses(
            new Mql5ForceIndexIndicator(1, Mql5MaMethod.Sma, Mql5AppliedVolume.Tick),
            closes);

        // A one-bar average is the close itself, so the first bar only primes the previous value.
        Assert.Equal(Mql5IndicatorBase.EmptyValue, values[0]);
        Assert.Equal(200.0, values[1], 10);     // 100 * (12 - 10)
        Assert.Equal(-100.0, values[2], 10);    // 100 * (11 - 12)
    }

    // --- iEnvelopes -------------------------------------------------------------------------

    [Fact]
    public void EnvelopesStrikeTheBandsAsAPercentageOfTheAverage()
    {
        double[] closes = [10.0, 12.0, 11.0];
        var indicator = new Mql5EnvelopesIndicator(3, 0, Mql5MaMethod.Sma, Mql5AppliedPrice.Close, 1.0);
        for (int index = 0; index < closes.Length; index++)
        {
            indicator.Append(EngineTestSupport.Flat(index, closes[index]));
        }

        // SMA(3) = 11, deviation 1 per cent.
        Assert.Equal(2, indicator.BufferCount);
        Assert.Equal(11.0 * 1.01, indicator.Value(0, 0), 10);   // buffer 0 is the upper band
        Assert.Equal(11.0 * 0.99, indicator.Value(1, 0), 10);   // buffer 1 is the lower band
    }

    // --- iFractals --------------------------------------------------------------------------

    [Fact]
    public void FractalsMarkTheCentreBarOfAFiveBarPeakAndTrough()
    {
        List<Mql5Bar> bars =
        [
            EngineTestSupport.Bar(0, 9.5, 10.0, 9.0, 9.5),
            EngineTestSupport.Bar(1, 9.5, 11.0, 8.0, 10.5),
            EngineTestSupport.Bar(2, 10.5, 15.0, 7.0, 12.0),   // high above and low below all four neighbours
            EngineTestSupport.Bar(3, 12.0, 12.0, 8.0, 10.0),
            EngineTestSupport.Bar(4, 10.0, 13.0, 9.0, 12.0),
        ];

        var indicator = new Mql5FractalsIndicator();

        // After four bars the centre bar still lacks its second right-hand neighbour.
        Feed(indicator, bars.GetRange(0, 4));
        Assert.Equal(Mql5IndicatorBase.EmptyValue, indicator.Value(0, 1));
        Assert.Equal(Mql5IndicatorBase.EmptyValue, indicator.Value(1, 1));

        // The fifth bar confirms it, and the value is written back onto bar 2.
        indicator.Append(bars[4]);
        Assert.Equal(15.0, indicator.Value(0, 2), 10);
        Assert.Equal(7.0, indicator.Value(1, 2), 10);

        // The two most recent bars can never carry a confirmed fractal.
        Assert.Equal(Mql5IndicatorBase.EmptyValue, indicator.Value(0, 0));
        Assert.Equal(Mql5IndicatorBase.EmptyValue, indicator.Value(0, 1));
    }

    [Fact]
    public void FractalsIgnoreABarThatOnlyTiesItsNeighbours()
    {
        List<Mql5Bar> bars =
        [
            EngineTestSupport.Bar(0, 9.5, 10.0, 9.0, 9.5),
            EngineTestSupport.Bar(1, 9.5, 11.0, 9.0, 10.5),
            EngineTestSupport.Bar(2, 10.5, 11.0, 9.0, 10.5),   // high ties bar 1, low ties every bar
            EngineTestSupport.Bar(3, 10.5, 10.0, 9.5, 9.8),
            EngineTestSupport.Bar(4, 9.8, 10.0, 9.5, 9.9),
        ];

        var indicator = new Mql5FractalsIndicator();
        Feed(indicator, bars);

        Assert.Equal(Mql5IndicatorBase.EmptyValue, indicator.Value(0, 2));
        Assert.Equal(Mql5IndicatorBase.EmptyValue, indicator.Value(1, 2));
    }

    // --- iAlligator -------------------------------------------------------------------------

    [Fact]
    public void AlligatorPublishesJawTeethAndLipsInMetaTraderOrder()
    {
        double[] closes = [10.0, 12.0, 11.0, 15.0];

        // Flat bars, so the median price equals the close. Simple averaging and small periods
        // keep the arithmetic checkable; the lips carry a one-bar shift.
        var indicator = new Mql5AlligatorIndicator(3, 0, 2, 0, 1, 1, Mql5MaMethod.Sma, Mql5AppliedPrice.Median);
        for (int index = 0; index < closes.Length; index++)
        {
            indicator.Append(EngineTestSupport.Flat(index, closes[index]));
        }

        Assert.Equal(3, indicator.BufferCount);
        Assert.Equal(38.0 / 3.0, indicator.Value(0, 0), 10);   // jaw:   (12 + 11 + 15) / 3
        Assert.Equal(13.0, indicator.Value(1, 0), 10);         // teeth: (11 + 15) / 2
        Assert.Equal(11.0, indicator.Value(2, 0), 10);         // lips:  the one-bar average, shifted one bar
    }

    // --- iSAR -------------------------------------------------------------------------------

    [Fact]
    public void ParabolicSarClimbsWithTheTrendThenFlipsToTheExtremePoint()
    {
        List<Mql5Bar> bars =
        [
            EngineTestSupport.Bar(0, 9.5, 10.0, 9.0, 9.5),
            EngineTestSupport.Bar(1, 9.5, 11.0, 9.5, 10.5),
            EngineTestSupport.Bar(2, 10.5, 12.0, 10.0, 11.5),
            EngineTestSupport.Bar(3, 11.5, 13.0, 11.0, 12.5),
            EngineTestSupport.Bar(4, 12.5, 12.0, 8.0, 8.5),
        ];

        List<double> values = RunBars(new Mql5ParabolicSarIndicator(0.02, 0.2), bars);

        Assert.Equal(Mql5IndicatorBase.EmptyValue, values[0]);

        // Bar 1 closes above bar 0, so the series opens long at the lower of the two lows.
        // Extreme point = max(10, 11) = 11, acceleration = 0.02.
        Assert.Equal(9.0, values[1], 10);

        // Bar 2: 9 + 0.02 * (11 - 9) = 9.04, clamped by the lows of bars 1 and 0 to min(9.04, 9.5, 9) = 9.
        // The new high of 12 lifts the extreme point and the factor to 0.04.
        Assert.Equal(9.0, values[2], 10);

        // Bar 3: 9 + 0.04 * (12 - 9) = 9.12; the lows of bars 2 and 1 are 10 and 9.5, so no clamp.
        Assert.Equal(9.12, values[3], 10);

        // Bar 4: 9.12 + 0.06 * (13 - 9.12) = 9.3528, but the low of 8 trades through it,
        // so the stop flips to the extreme point of the long trend, 13.
        Assert.Equal(13.0, values[4], 10);
    }

    [Fact]
    public void ParabolicSarOpensShortWhenTheSecondBarClosesLower()
    {
        List<Mql5Bar> bars =
        [
            EngineTestSupport.Bar(0, 10.5, 11.0, 9.5, 10.5),
            EngineTestSupport.Bar(1, 10.5, 10.0, 9.0, 9.5),
        ];

        List<double> values = RunBars(new Mql5ParabolicSarIndicator(0.02, 0.2), bars);

        // A short series starts at the higher of the two highs.
        Assert.Equal(11.0, values[1], 10);
    }

    // --- iRVI -------------------------------------------------------------------------------

    [Fact]
    public void RelativeVigorIndexUsesTheFourBarTriangularAverage()
    {
        // Every bar has a range of 2. Bodies (close - open) are 0.5 except bar 2, which is 1.0.
        List<Mql5Bar> bars =
        [
            EngineTestSupport.Bar(0, 10.0, 11.0, 9.0, 10.5),
            EngineTestSupport.Bar(1, 10.5, 12.0, 10.0, 11.0),
            EngineTestSupport.Bar(2, 11.0, 12.5, 10.5, 12.0),
            EngineTestSupport.Bar(3, 12.0, 13.0, 11.0, 12.5),
            EngineTestSupport.Bar(4, 12.5, 13.5, 11.5, 13.0),
            EngineTestSupport.Bar(5, 13.0, 14.0, 12.0, 13.5),
            EngineTestSupport.Bar(6, 13.5, 14.5, 12.5, 14.0),
        ];

        var indicator = new Mql5RelativeVigorIndexIndicator(1);
        List<double> main = RunBars(indicator, bars);

        // Denominator on every formed bar: (2 + 2*2 + 2*2 + 2) / 6 = 2.
        // Bar 3 numerator: (0.5 + 2*1.0 + 2*0.5 + 0.5) / 6 = 4/6 -> main = (4/6) / 2 = 1/3
        Assert.Equal(Mql5IndicatorBase.EmptyValue, main[2]);
        Assert.Equal(1.0 / 3.0, main[3], 10);

        // Bar 4 numerator: (0.5 + 2*0.5 + 2*1.0 + 0.5) / 6 = 4/6 -> main = 1/3
        Assert.Equal(1.0 / 3.0, main[4], 10);

        // Bar 5 numerator: (0.5 + 2*0.5 + 2*0.5 + 1.0) / 6 = 3.5/6 -> main = 3.5/12 = 7/24
        Assert.Equal(7.0 / 24.0, main[5], 10);

        // Bar 6 numerator: (0.5 + 2*0.5 + 2*0.5 + 0.5) / 6 = 3/6 -> main = 0.25
        Assert.Equal(0.25, main[6], 10);

        // Signal = (0.25 + 2*(7/24) + 2*(1/3) + 1/3) / 6
        //        = (9/36 + 21/36 + 24/36 + 12/36) / 6 = (66/36) / 6 = 11/36
        Assert.Equal(11.0 / 36.0, indicator.Value(1, 0), 10);

        // Only one main value existed on bar 5, so the signal had not formed.
        Assert.Equal(Mql5IndicatorBase.EmptyValue, indicator.Value(1, 1));
    }

    // --- iOsMA ------------------------------------------------------------------------------

    [Fact]
    public void OsMaIsTheMacdMainLineLessItsSignal()
    {
        double[] closes = [10.0, 12.0, 11.0, 15.0, 14.0];
        List<double> values = RunCloses(
            new Mql5OsMaIndicator(2, 4, 2, Mql5AppliedPrice.Close),
            closes);

        // EMA(2), k = 2/3, seeded on (10 + 12) / 2 = 11:
        //   bar 2: 11 * 2/3 + 11 * 1/3 = 11
        //   bar 3: 15 * 2/3 + 11 * 1/3 = 41/3
        //   bar 4: 14 * 2/3 + (41/3) * 1/3 = 125/9
        // EMA(4), k = 2/5, seeded on (10 + 12 + 11 + 15) / 4 = 12:
        //   bar 4: 14 * 0.4 + 12 * 0.6 = 12.8 = 64/5
        // main:  bar 3 = 41/3 - 12 = 5/3,  bar 4 = 125/9 - 64/5 = 49/45
        // signal is the two-bar simple average: (5/3 + 49/45) / 2 = 62/45
        // OsMA = 49/45 - 62/45 = -13/45
        Assert.Equal(-13.0 / 45.0, values[4], 10);

        // On bar 3 the signal average had only one sample, so nothing was published.
        Assert.Equal(Mql5IndicatorBase.EmptyValue, values[3]);
    }

    // --- factory wiring ---------------------------------------------------------------------

    [Theory]
    [InlineData("iADX", typeof(Mql5AdxIndicator))]
    [InlineData("iADXWilder", typeof(Mql5AdxIndicator))]
    [InlineData("iStdDev", typeof(Mql5StdDevIndicator))]
    [InlineData("iMomentum", typeof(Mql5MomentumIndicator))]
    [InlineData("iWPR", typeof(Mql5WilliamsPercentRangeIndicator))]
    [InlineData("iAO", typeof(Mql5AwesomeOscillatorIndicator))]
    [InlineData("iDeMarker", typeof(Mql5DeMarkerIndicator))]
    [InlineData("iForce", typeof(Mql5ForceIndexIndicator))]
    [InlineData("iEnvelopes", typeof(Mql5EnvelopesIndicator))]
    [InlineData("iFractals", typeof(Mql5FractalsIndicator))]
    [InlineData("iAlligator", typeof(Mql5AlligatorIndicator))]
    [InlineData("iSAR", typeof(Mql5ParabolicSarIndicator))]
    [InlineData("iRVI", typeof(Mql5RelativeVigorIndexIndicator))]
    [InlineData("iOsMA", typeof(Mql5OsMaIndicator))]
    public void FactoryBuildsEveryNewlySupportedIndicator(string name, Type expected)
    {
        Assert.Contains(name, Mql5IndicatorFactory.SupportedNames);

        IMql5Indicator? indicator = Mql5IndicatorFactory.Create(name, []);
        Assert.NotNull(indicator);
        Assert.Equal(expected, indicator.GetType());
        Assert.Equal(name, indicator.Name);
    }

    [Fact]
    public void FactoryDropsTheLeadingSymbolAndTimeframeForTheNewIndicators()
    {
        // The MQL5 full form and the bare form must land on the same parameters.
        var bare = Assert.IsType<Mql5AdxIndicator>(Mql5IndicatorFactory.Create("iADX", [21]));
        var full = Assert.IsType<Mql5AdxIndicator>(
            Mql5IndicatorFactory.Create("iADX", ["EURUSD", 16385, 21]));

        Assert.Equal(21, bare.Period);
        Assert.Equal(21, full.Period);

        var bareWpr = Assert.IsType<Mql5WilliamsPercentRangeIndicator>(
            Mql5IndicatorFactory.Create("iWPR", [30]));
        var fullWpr = Assert.IsType<Mql5WilliamsPercentRangeIndicator>(
            Mql5IndicatorFactory.Create("iWPR", ["EURUSD", 16385, 30]));

        Assert.Equal(30, bareWpr.Period);
        Assert.Equal(30, fullWpr.Period);
    }

    [Fact]
    public void FactoryReadsTheFullParabolicSarArgumentList()
    {
        // iSAR(symbol, timeframe, step, maximum): the two doubles must survive the coercion.
        IMql5Indicator? indicator = Mql5IndicatorFactory.Create("iSAR", ["EURUSD", 16385, 0.05, 0.5]);
        var sar = Assert.IsType<Mql5ParabolicSarIndicator>(indicator);

        // A step of 0.05 on the same bars as the hand-checked run gives
        // 9 + 0.05 * (11 - 9) = 9.1, clamped by the low of bar 0 to 9.
        sar.Append(EngineTestSupport.Bar(0, 9.5, 10.0, 9.0, 9.5));
        sar.Append(EngineTestSupport.Bar(1, 9.5, 11.0, 9.5, 10.5));
        sar.Append(EngineTestSupport.Bar(2, 10.5, 12.0, 10.0, 11.5));
        Assert.Equal(9.0, sar.Value(0, 0), 10);

        // Bar 3: 9 + 0.10 * (12 - 9) = 9.3, and the lows of bars 2 and 1 do not clamp it.
        sar.Append(EngineTestSupport.Bar(3, 11.5, 13.0, 11.0, 12.5));
        Assert.Equal(9.3, sar.Value(0, 0), 10);
    }

    [Fact]
    public void FactoryStillRefusesCustomIndicators() =>
        Assert.Null(Mql5IndicatorFactory.Create("iCustom", ["EURUSD", 16385, "MyIndicator", 14]));

    [Fact]
    public void UnformedBarsReadAsTheSharedEmptyValue()
    {
        // Nothing in the new set may invent its own sentinel: the base class value is the only one.
        Assert.Equal(0.0, Mql5IndicatorBase.EmptyValue);

        IMql5Indicator[] indicators =
        [
            new Mql5AdxIndicator(14),
            new Mql5StdDevIndicator(20, 0, Mql5MaMethod.Sma, Mql5AppliedPrice.Close),
            new Mql5MomentumIndicator(14, Mql5AppliedPrice.Close),
            new Mql5WilliamsPercentRangeIndicator(14),
            new Mql5AwesomeOscillatorIndicator(),
            new Mql5DeMarkerIndicator(14),
            new Mql5ForceIndexIndicator(13, Mql5MaMethod.Sma, Mql5AppliedVolume.Tick),
            new Mql5EnvelopesIndicator(14, 0, Mql5MaMethod.Sma, Mql5AppliedPrice.Close, 0.1),
            new Mql5FractalsIndicator(),
            new Mql5AlligatorIndicator(13, 8, 8, 5, 5, 3, Mql5MaMethod.Smma, Mql5AppliedPrice.Median),
            new Mql5ParabolicSarIndicator(0.02, 0.2),
            new Mql5RelativeVigorIndexIndicator(10),
            new Mql5OsMaIndicator(12, 26, 9, Mql5AppliedPrice.Close),
        ];

        foreach (IMql5Indicator indicator in indicators)
        {
            indicator.Append(EngineTestSupport.Bar(0, 1.1, 1.2, 1.0, 1.15));

            for (int buffer = 0; buffer < indicator.BufferCount; buffer++)
            {
                Assert.Equal(Mql5IndicatorBase.EmptyValue, indicator.Value(buffer, 0));
            }

            Assert.Equal(1, indicator.Count);
        }
    }

    [Fact]
    public void EveryBufferAdvancesInLockstepWithTheBars()
    {
        // A desynchronised buffer would silently misalign indicator values against bar indices.
        IMql5Indicator[] indicators =
        [
            new Mql5AdxIndicator(3),
            new Mql5EnvelopesIndicator(3, 2, Mql5MaMethod.Sma, Mql5AppliedPrice.Close, 0.5),
            new Mql5FractalsIndicator(),
            new Mql5AlligatorIndicator(5, 3, 3, 2, 2, 1, Mql5MaMethod.Smma, Mql5AppliedPrice.Median),
            new Mql5RelativeVigorIndexIndicator(4),
        ];

        List<Mql5Bar> bars = EngineTestSupport.Ramp(30);
        foreach (IMql5Indicator indicator in indicators)
        {
            Feed(indicator, bars);
            Assert.Equal(bars.Count, indicator.Count);

            // Reading past the end must fall back to the empty value rather than throwing.
            Assert.Equal(Mql5IndicatorBase.EmptyValue, indicator.Value(0, bars.Count));
            Assert.Equal(Mql5IndicatorBase.EmptyValue, indicator.Value(indicator.BufferCount, 0));
        }
    }
}
