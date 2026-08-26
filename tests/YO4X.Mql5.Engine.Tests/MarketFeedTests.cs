using YO4X.Mql5.Engine.Feed;

namespace YO4X.Mql5.Engine.Tests;

/// <summary>CSV parsing and deterministic synthetic generation.</summary>
public sealed class MarketFeedTests
{
    [Fact]
    public void CsvFeedParsesAHeaderedMetaTraderExport()
    {
        string[] lines =
        [
            "time,open,high,low,close,tickvolume,spread",
            "2024.01.01 00:00,1.10000,1.10120,1.09980,1.10050,321,12",
            "2024.01.01 01:00,1.10050,1.10200,1.10010,1.10180,410,11",
        ];

        List<Mql5Bar> bars = [.. new Mql5CsvMarketFeed(lines, "EURUSD").ReadBars()];

        Assert.Equal(2, bars.Count);
        Assert.Equal(new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc), bars[0].Time);
        Assert.Equal(1.10000, bars[0].Open, 5);
        Assert.Equal(1.10120, bars[0].High, 5);
        Assert.Equal(1.09980, bars[0].Low, 5);
        Assert.Equal(1.10050, bars[0].Close, 5);
        Assert.Equal(321, bars[0].TickVolume);
        Assert.Equal(12, bars[0].Spread);
        Assert.Equal(1.10180, bars[1].Close, 5);
    }

    [Fact]
    public void CsvFeedAcceptsSeparateDateAndTimeColumnsAndTabSeparators()
    {
        string[] lines =
        [
            "2024.02.05\t09:30\t1.20000\t1.20100\t1.19900\t1.20050\t100",
        ];

        Mql5Bar bar = Assert.Single(new Mql5CsvMarketFeed(lines, "EURUSD") { DefaultSpreadPoints = 20 }.ReadBars());

        Assert.Equal(new DateTime(2024, 2, 5, 9, 30, 0, DateTimeKind.Utc), bar.Time);
        Assert.Equal(1.20050, bar.Close, 5);
        Assert.Equal(20, bar.Spread);
    }

    [Fact]
    public void CsvFeedSkipsBlankCommentAndMalformedRowsInsteadOfThrowing()
    {
        string[] lines =
        [
            "# exported by hand",
            string.Empty,
            "time,open,high,low,close",
            "not,a,bar,at,all",
            "2024.01.01 00:00,1.10000,1.10120,1.09980,1.10050",
            "2024.01.01 01:00,1.10050,oops,1.10010,1.10180",
        ];

        List<Mql5Bar> bars = [.. new Mql5CsvMarketFeed(lines, "EURUSD").ReadBars()];

        Mql5Bar bar = Assert.Single(bars);
        Assert.Equal(1.10050, bar.Close, 5);
    }

    [Fact]
    public void CsvFeedReadsFromDisk()
    {
        string path = Path.Combine(Path.GetTempPath(), "yo4x-mql5-engine-" + Guid.NewGuid().ToString("N") + ".csv");
        try
        {
            File.WriteAllLines(
                path,
                [
                    "time,open,high,low,close,tickvolume,spread",
                    "2024.03.01 00:00,1.30000,1.30100,1.29900,1.30050,55,9",
                ]);

            Mql5Bar bar = Assert.Single(new Mql5CsvMarketFeed(path, "GBPUSD").ReadBars());
            Assert.Equal(1.30050, bar.Close, 5);
            Assert.Equal(9, bar.Spread);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void SyntheticFeedIsReproducibleFromItsSeed()
    {
        var first = new Mql5SyntheticMarketFeed("EURUSD", seed: 12345, barCount: 200);
        var second = new Mql5SyntheticMarketFeed("EURUSD", seed: 12345, barCount: 200);

        List<Mql5Bar> a = [.. first.ReadBars()];
        List<Mql5Bar> b = [.. second.ReadBars()];

        Assert.Equal(200, a.Count);
        Assert.Equal(a, b);

        // Re-enumerating the same instance must also replay identically.
        List<Mql5Bar> replay = [.. first.ReadBars()];
        Assert.Equal(a, replay);
    }

    [Fact]
    public void SyntheticFeedDivergesForADifferentSeed()
    {
        List<Mql5Bar> a = [.. new Mql5SyntheticMarketFeed("EURUSD", seed: 1, barCount: 50).ReadBars()];
        List<Mql5Bar> b = [.. new Mql5SyntheticMarketFeed("EURUSD", seed: 2, barCount: 50).ReadBars()];

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void SyntheticBarsAreInternallyConsistentAndAdvanceInTime()
    {
        var feed = new Mql5SyntheticMarketFeed("EURUSD", seed: 99, barCount: 250)
        {
            StartTime = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            PeriodMinutes = 15,
            SpreadPoints = 14,
        };

        List<Mql5Bar> bars = [.. feed.ReadBars()];
        for (int index = 0; index < bars.Count; index++)
        {
            Mql5Bar bar = bars[index];
            Assert.True(bar.High >= bar.Open, "high must not be below the open");
            Assert.True(bar.High >= bar.Close, "high must not be below the close");
            Assert.True(bar.Low <= bar.Open, "low must not be above the open");
            Assert.True(bar.Low <= bar.Close, "low must not be above the close");
            Assert.True(bar.Low > 0.0, "prices must stay positive");
            Assert.Equal(14, bar.Spread);
            Assert.Equal(feed.StartTime.AddMinutes(15 * index), bar.Time);

            if (index > 0)
            {
                Assert.Equal(bars[index - 1].Close, bar.Open, 10);
            }
        }
    }

    [Fact]
    public void DeterministicRandomIsStableAndBounded()
    {
        var first = new Mql5DeterministicRandom(7);
        var second = new Mql5DeterministicRandom(7);

        for (int index = 0; index < 1000; index++)
        {
            double a = first.NextDouble();
            Assert.Equal(a, second.NextDouble(), 15);
            Assert.InRange(a, 0.0, 1.0);
        }

        var bounded = new Mql5DeterministicRandom(11);
        for (int index = 0; index < 1000; index++)
        {
            Assert.InRange(bounded.NextInt32(5, 9), 5, 8);
        }
    }
}
