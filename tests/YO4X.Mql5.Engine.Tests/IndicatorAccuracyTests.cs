using YO4X.Mql5.Engine.Feed;
using YO4X.Mql5.Engine.Indicators;
using YO4X.Mql5.Engine.Trading;

namespace YO4X.Mql5.Engine.Tests;

/// <summary>
/// Every expectation here is computed by hand in the comment above the assertion, so a change in
/// the maths shows up as a failing number rather than a quietly different backtest.
/// </summary>
public sealed class IndicatorAccuracyTests
{
    private static readonly double[] Sample = [10.0, 12.0, 11.0, 15.0, 14.0, 13.0, 18.0, 17.0];

    private static List<double> Run(IMql5Indicator indicator, double[] closes, int buffer = 0)
    {
        var values = new List<double>(closes.Length);
        for (int index = 0; index < closes.Length; index++)
        {
            indicator.Append(EngineTestSupport.Flat(index, closes[index]));
            values.Add(indicator.Value(buffer, 0));
        }

        return values;
    }

    [Fact]
    public void SimpleMovingAverageMatchesHandComputedWindow()
    {
        List<double> values = Run(
            new Mql5MovingAverageIndicator(3, 0, Mql5MaMethod.Sma, Mql5AppliedPrice.Close),
            Sample);

        // Not formed until three closes exist.
        Assert.Equal(0.0, values[0]);
        Assert.Equal(0.0, values[1]);

        Assert.Equal(11.0, values[2], 10);                 // (10 + 12 + 11) / 3
        Assert.Equal(38.0 / 3.0, values[3], 10);           // (12 + 11 + 15) / 3 = 12.6666666667
        Assert.Equal(40.0 / 3.0, values[4], 10);           // (11 + 15 + 14) / 3 = 13.3333333333
        Assert.Equal(14.0, values[5], 10);                 // (15 + 14 + 13) / 3
        Assert.Equal(15.0, values[6], 10);                 // (14 + 13 + 18) / 3
        Assert.Equal(16.0, values[7], 10);                 // (13 + 18 + 17) / 3
    }

    [Fact]
    public void ExponentialMovingAverageSeedsWithSimpleAverageThenSmooths()
    {
        List<double> values = Run(
            new Mql5MovingAverageIndicator(3, 0, Mql5MaMethod.Ema, Mql5AppliedPrice.Close),
            Sample);

        // k = 2 / (3 + 1) = 0.5, seeded with the simple average of the first three closes.
        Assert.Equal(11.0, values[2], 10);        // seed  = (10 + 12 + 11) / 3
        Assert.Equal(13.0, values[3], 10);        // 15 * 0.5 + 11.0   * 0.5
        Assert.Equal(13.5, values[4], 10);        // 14 * 0.5 + 13.0   * 0.5
        Assert.Equal(13.25, values[5], 10);       // 13 * 0.5 + 13.5   * 0.5
        Assert.Equal(15.625, values[6], 10);      // 18 * 0.5 + 13.25  * 0.5
        Assert.Equal(16.3125, values[7], 10);     // 17 * 0.5 + 15.625 * 0.5
    }

    [Fact]
    public void SmoothedMovingAverageUsesWilderRecursion()
    {
        List<double> values = Run(
            new Mql5MovingAverageIndicator(3, 0, Mql5MaMethod.Smma, Mql5AppliedPrice.Close),
            Sample);

        Assert.Equal(11.0, values[2], 10);                       // seed = (10 + 12 + 11) / 3
        Assert.Equal(37.0 / 3.0, values[3], 10);                 // (11 * 2 + 15) / 3 = 12.3333333333
        Assert.Equal(((37.0 / 3.0 * 2.0) + 14.0) / 3.0, values[4], 10);  // 12.8888888889
    }

    [Fact]
    public void LinearWeightedMovingAverageWeightsTheNewestBarHeaviest()
    {
        List<double> values = Run(
            new Mql5MovingAverageIndicator(3, 0, Mql5MaMethod.Lwma, Mql5AppliedPrice.Close),
            Sample);

        Assert.Equal(67.0 / 6.0, values[2], 10);   // (10*1 + 12*2 + 11*3) / 6 = 11.1666666667
        Assert.Equal(79.0 / 6.0, values[3], 10);   // (12*1 + 11*2 + 15*3) / 6 = 13.1666666667
    }

    [Fact]
    public void MovingAverageShiftDisplacesTheSeries()
    {
        List<double> plain = Run(
            new Mql5MovingAverageIndicator(3, 0, Mql5MaMethod.Sma, Mql5AppliedPrice.Close),
            Sample);
        List<double> shifted = Run(
            new Mql5MovingAverageIndicator(3, 1, Mql5MaMethod.Sma, Mql5AppliedPrice.Close),
            Sample);

        Assert.Equal(plain[3], shifted[4], 10);
        Assert.Equal(plain[6], shifted[7], 10);
    }

    [Fact]
    public void RelativeStrengthIndexMatchesWilderByHand()
    {
        double[] closes = [10.0, 11.0, 10.5, 12.0, 11.5, 13.0];
        List<double> values = Run(new Mql5RsiIndicator(3, Mql5AppliedPrice.Close), closes);

        // Changes: +1.0, -0.5, +1.5, -0.5, +1.5
        // Seed at index 3: avgGain = (1.0 + 0 + 1.5) / 3 = 0.8333333333
        //                  avgLoss = (0   + 0.5 + 0) / 3 = 0.1666666667
        //                  RS = 5.0  ->  100 - 100 / 6 = 83.3333333333
        Assert.Equal(0.0, values[2]);
        Assert.Equal(100.0 - (100.0 / 6.0), values[3], 9);

        // Index 4: avgGain = (0.8333333333 * 2 + 0)   / 3 = 0.5555555556
        //          avgLoss = (0.1666666667 * 2 + 0.5) / 3 = 0.2777777778
        //          RS = 2.0  ->  100 - 100 / 3 = 66.6666666667
        Assert.Equal(100.0 - (100.0 / 3.0), values[4], 9);

        // Index 5: avgGain = (0.5555555556 * 2 + 1.5) / 3 = 0.8703703704
        //          avgLoss = (0.2777777778 * 2 + 0)   / 3 = 0.1851851852
        //          RS = 4.7  ->  100 - 100 / 5.7 = 82.4561403509
        Assert.Equal(100.0 - (100.0 / 5.7), values[5], 9);
    }

    [Fact]
    public void RelativeStrengthIndexSaturatesAtOneHundredWithoutLosses()
    {
        double[] closes = [10.0, 11.0, 12.0, 13.0, 14.0];
        List<double> values = Run(new Mql5RsiIndicator(3, Mql5AppliedPrice.Close), closes);

        Assert.Equal(100.0, values[3], 9);
        Assert.Equal(100.0, values[4], 9);
    }

    [Fact]
    public void AverageTrueRangeMatchesWilderByHand()
    {
        var bars = new List<Mql5Bar>
        {
            EngineTestSupport.Bar(0, 9.5, 10.0, 9.0, 9.5),
            EngineTestSupport.Bar(1, 9.5, 10.5, 9.5, 10.0),
            EngineTestSupport.Bar(2, 10.0, 11.0, 10.0, 10.8),
            EngineTestSupport.Bar(3, 10.8, 12.0, 10.5, 11.5),
            EngineTestSupport.Bar(4, 11.5, 11.8, 10.2, 10.5),
        };

        var indicator = new Mql5AtrIndicator(3);
        var values = new List<double>();
        foreach (Mql5Bar bar in bars)
        {
            indicator.Append(bar);
            values.Add(indicator.Value(0, 0));
        }

        // True ranges: 1.0 (first bar has no previous close), 1.0, 1.0, 1.5, 1.6
        Assert.Equal(1.0, values[2], 10);                       // seed = (1.0 + 1.0 + 1.0) / 3
        Assert.Equal(3.5 / 3.0, values[3], 10);                 // (1.0 * 2 + 1.5) / 3 = 1.1666666667
        Assert.Equal(((3.5 / 3.0 * 2.0) + 1.6) / 3.0, values[4], 10);  // 1.3111111111
    }

    [Fact]
    public void AverageTrueRangeSupportsPlainSimpleSmoothing()
    {
        var bars = new List<Mql5Bar>
        {
            EngineTestSupport.Bar(0, 9.5, 10.0, 9.0, 9.5),
            EngineTestSupport.Bar(1, 9.5, 10.5, 9.5, 10.0),
            EngineTestSupport.Bar(2, 10.0, 11.0, 10.0, 10.8),
            EngineTestSupport.Bar(3, 10.8, 12.0, 10.5, 11.5),
            EngineTestSupport.Bar(4, 11.5, 11.8, 10.2, 10.5),
        };

        var indicator = new Mql5AtrIndicator(3, Mql5MaMethod.Sma);
        double last = 0.0;
        foreach (Mql5Bar bar in bars)
        {
            indicator.Append(bar);
            last = indicator.Value(0, 0);
        }

        Assert.Equal(4.1 / 3.0, last, 10);   // (1.0 + 1.5 + 1.6) / 3 = 1.3666666667
    }

    [Fact]
    public void BollingerBandsUsePopulationStandardDeviation()
    {
        double[] closes = [10.0, 12.0, 11.0];
        var indicator = new Mql5BandsIndicator(3, 0, 2.0, Mql5AppliedPrice.Close);
        for (int index = 0; index < closes.Length; index++)
        {
            indicator.Append(EngineTestSupport.Flat(index, closes[index]));
        }

        // mean = 11, variance = ((-1)^2 + 1^2 + 0^2) / 3 = 0.6666666667, sd = 0.8164965809
        double sd = Math.Sqrt(2.0 / 3.0);
        Assert.Equal(11.0, indicator.Value(0, 0), 10);
        Assert.Equal(11.0 + (2.0 * sd), indicator.Value(1, 0), 10);
        Assert.Equal(11.0 - (2.0 * sd), indicator.Value(2, 0), 10);
    }

    [Fact]
    public void CommodityChannelIndexMatchesLambertFormula()
    {
        double[] closes = [10.0, 12.0, 11.0, 14.0];
        var indicator = new Mql5CciIndicator(3, Mql5AppliedPrice.Close);
        var values = new List<double>();
        for (int index = 0; index < closes.Length; index++)
        {
            indicator.Append(EngineTestSupport.Flat(index, closes[index]));
            values.Add(indicator.Value(0, 0));
        }

        // Window 10, 12, 11: mean 11, mean deviation (1 + 1 + 0) / 3 = 0.6666666667
        // CCI = (11 - 11) / (0.015 * 0.6666666667) = 0
        Assert.Equal(0.0, values[2], 10);

        // Window 12, 11, 14: mean 12.3333333333,
        // mean deviation (0.3333333333 + 1.3333333333 + 1.6666666667) / 3 = 1.1111111111
        // CCI = 1.6666666667 / (0.015 * 1.1111111111) = 100
        Assert.Equal(100.0, values[3], 8);
    }

    [Fact]
    public void MacdMainLineIsTheDifferenceOfTheTwoExponentialAverages()
    {
        double[] closes = [10.0, 12.0, 11.0, 15.0, 14.0, 13.0, 18.0, 17.0, 16.0, 19.0];
        var macd = new Mql5MacdIndicator(2, 4, 2, Mql5AppliedPrice.Close);
        var fast = new Mql5MovingAverageIndicator(2, 0, Mql5MaMethod.Ema, Mql5AppliedPrice.Close);
        var slow = new Mql5MovingAverageIndicator(4, 0, Mql5MaMethod.Ema, Mql5AppliedPrice.Close);

        for (int index = 0; index < closes.Length; index++)
        {
            Mql5Bar bar = EngineTestSupport.Flat(index, closes[index]);
            macd.Append(bar);
            fast.Append(bar);
            slow.Append(bar);

            if (index >= 3)
            {
                Assert.Equal(fast.Value(0, 0) - slow.Value(0, 0), macd.Value(0, 0), 10);
            }
        }

        // Signal is a two-bar simple average of the main line, matching the bundled MetaTrader MACD.
        double previousMain = macd.Value(0, 1);
        double currentMain = macd.Value(0, 0);
        Assert.Equal((previousMain + currentMain) / 2.0, macd.Value(1, 0), 10);
    }

    [Fact]
    public void StochasticReadsOneHundredAtTheTopAndZeroAtTheBottomOfItsRange()
    {
        var indicator = new Mql5StochasticIndicator(3, 1, 1, Mql5MaMethod.Sma, Mql5StochasticPriceField.LowHigh);

        indicator.Append(EngineTestSupport.Bar(0, 9.0, 10.0, 9.0, 10.0));
        indicator.Append(EngineTestSupport.Bar(1, 10.0, 11.0, 9.0, 11.0));
        indicator.Append(EngineTestSupport.Bar(2, 11.0, 12.0, 10.0, 12.0));

        // highest 12, lowest 9, close 12 -> 100 * (12 - 9) / (12 - 9)
        Assert.Equal(100.0, indicator.Value(0, 0), 8);

        indicator.Append(EngineTestSupport.Bar(3, 12.0, 12.0, 9.0, 9.0));

        // highest 12, lowest 9, close 9 -> 100 * (9 - 9) / (12 - 9)
        Assert.Equal(0.0, indicator.Value(0, 0), 8);
    }

    [Fact]
    public void AppliedPriceSelectorsPickTheRightBarField()
    {
        Mql5Bar bar = EngineTestSupport.Bar(0, 1.0, 4.0, 2.0, 3.0);

        Assert.Equal(3.0, Value(Mql5AppliedPrice.Close, bar), 10);
        Assert.Equal(1.0, Value(Mql5AppliedPrice.Open, bar), 10);
        Assert.Equal(4.0, Value(Mql5AppliedPrice.High, bar), 10);
        Assert.Equal(2.0, Value(Mql5AppliedPrice.Low, bar), 10);
        Assert.Equal(3.0, Value(Mql5AppliedPrice.Median, bar), 10);    // (4 + 2) / 2
        Assert.Equal(3.0, Value(Mql5AppliedPrice.Typical, bar), 10);   // (4 + 2 + 3) / 3
        Assert.Equal(3.0, Value(Mql5AppliedPrice.Weighted, bar), 10);  // (4 + 2 + 6) / 4

        static double Value(Mql5AppliedPrice applied, Mql5Bar bar)
        {
            var indicator = new Mql5MovingAverageIndicator(1, 0, Mql5MaMethod.Sma, applied);
            indicator.Append(bar);
            return indicator.Value(0, 0);
        }
    }

    [Fact]
    public void FactoryAcceptsBothBareAndFullMql5ArgumentLists()
    {
        IMql5Indicator? bare = Mql5IndicatorFactory.Create("iMA", [14, 0, Mql5MaMethod.Ema, Mql5AppliedPrice.Close]);
        IMql5Indicator? full = Mql5IndicatorFactory.Create("iMA", ["EURUSD", 16385, 14, 0, Mql5MaMethod.Ema, Mql5AppliedPrice.Close]);

        var bareMa = Assert.IsType<Mql5MovingAverageIndicator>(bare);
        var fullMa = Assert.IsType<Mql5MovingAverageIndicator>(full);

        Assert.Equal(14, bareMa.Period);
        Assert.Equal(Mql5MaMethod.Ema, bareMa.Method);
        Assert.Equal(14, fullMa.Period);
        Assert.Equal(Mql5MaMethod.Ema, fullMa.Method);
    }

    [Fact]
    public void FactoryReturnsNullForAnUnknownIndicator() =>
        Assert.Null(Mql5IndicatorFactory.Create("iSuperTrend", [10]));
}
