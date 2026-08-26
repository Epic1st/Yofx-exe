using YO4X.Mql5.Runtime;

namespace YO4X.Mql5.Runtime.Tests;

/// <summary>
/// The <c>Array*</c> family. The recurring theme is MQL5's failure values: an
/// unallocated array has size 0, a bad range answers -1, and nothing throws.
/// </summary>
public sealed class Mql5ArrayTests
{
    private static Mql5Runtime Build() => new(new FakeMarketContext());

    [Fact]
    public void ArraySizeOfAnUnallocatedArrayIsZero()
    {
        Mql5Runtime runtime = Build();

        double[]? missing = null;
        Assert.Equal(0, runtime.ArraySize(missing));
        Assert.Equal(0, runtime.ArraySize(Array.Empty<double>()));
        Assert.Equal(3, runtime.ArraySize(new double[3]));
    }

    [Fact]
    public void ArrayResizeGrowsShrinksAndPreservesContent()
    {
        Mql5Runtime runtime = Build();
        double[]? buffer = [1, 2, 3];

        Assert.Equal(5, runtime.ArrayResize(ref buffer, 5));
        Assert.Equal(5, buffer!.Length);
        Assert.Equal([1, 2, 3, 0, 0], buffer!);

        Assert.Equal(2, runtime.ArrayResize(ref buffer, 2));
        Assert.Equal([1, 2], buffer!);
    }

    [Fact]
    public void ArrayResizeToANegativeSizeIsAnError()
    {
        Mql5Runtime runtime = Build();
        double[]? buffer = [1, 2];

        Assert.Equal(-1, runtime.ArrayResize(ref buffer, -1));
        Assert.Equal(Mql5ErrorCodes.ArrayBadSize, runtime.GetLastError());
        Assert.Equal(2, buffer!.Length);
    }

    [Fact]
    public void ArrayResizeAllocatesAnUnallocatedArray()
    {
        Mql5Runtime runtime = Build();
        int[]? buffer = null;

        Assert.Equal(4, runtime.ArrayResize(ref buffer, 4));
        Assert.NotNull(buffer);
        Assert.Equal(4, buffer!.Length);
    }

    [Fact]
    public void ArrayFreeLeavesAZeroLengthArray()
    {
        Mql5Runtime runtime = Build();
        double[]? buffer = [1, 2, 3];

        runtime.ArrayFree(ref buffer);
        Assert.NotNull(buffer);
        Assert.Empty(buffer!);
    }

    [Fact]
    public void ArrayCopyGrowsTheDestinationAndReportsTheCount()
    {
        Mql5Runtime runtime = Build();
        double[]? destination = null;
        double[] source = [1, 2, 3, 4];

        Assert.Equal(4, runtime.ArrayCopy(ref destination, source));
        Assert.Equal(source, destination!);

        Assert.Equal(2, runtime.ArrayCopy(ref destination, source, 0, 2));
        Assert.Equal(3, destination![0]);
        Assert.Equal(4, destination[1]);
    }

    [Fact]
    public void ArrayCopyFromANullSourceIsAnError()
    {
        Mql5Runtime runtime = Build();
        double[]? destination = [1];

        Assert.Equal(-1, runtime.ArrayCopy(ref destination, null));
        Assert.Equal(Mql5ErrorCodes.InvalidArray, runtime.GetLastError());
    }

    [Fact]
    public void ArrayFillAndInitializeWriteTheRequestedRange()
    {
        Mql5Runtime runtime = Build();
        double[] buffer = new double[5];

        runtime.ArrayFill(buffer, 1, 3, 7.0);
        Assert.Equal([0, 7, 7, 7, 0], buffer);

        Assert.Equal(5, runtime.ArrayInitialize(buffer, 1.5));
        Assert.All(buffer, value => Assert.Equal(1.5, value));
    }

    [Fact]
    public void ArrayFillClampsRatherThanThrowing()
    {
        Mql5Runtime runtime = Build();
        double[] buffer = new double[3];

        runtime.ArrayFill(buffer, 2, 99, 4.0);
        Assert.Equal([0, 0, 4], buffer);

        runtime.ArrayFill(buffer, 99, 1, 9.0);
        Assert.Equal([0, 0, 4], buffer);
    }

    [Fact]
    public void ArraySortIsAscending()
    {
        Mql5Runtime runtime = Build();
        double[] buffer = [3, 1, 2];

        Assert.True(runtime.ArraySort(buffer));
        Assert.Equal([1, 2, 3], buffer);
    }

    [Fact]
    public void ArrayMaximumAndMinimumReturnIndices()
    {
        Mql5Runtime runtime = Build();
        double[] buffer = [3, 9, 1, 7];

        Assert.Equal(1, runtime.ArrayMaximum(buffer));
        Assert.Equal(2, runtime.ArrayMinimum(buffer));
        Assert.Equal(3, runtime.ArrayMaximum(buffer, 2));
        Assert.Equal(2, runtime.ArrayMinimum(buffer, 2, 2));
    }

    [Fact]
    public void ArrayExtremaOnAnEmptyArrayAnswerMinusOne()
    {
        Mql5Runtime runtime = Build();

        Assert.Equal(-1, runtime.ArrayMaximum(Array.Empty<double>()));
        Assert.Equal(-1, runtime.ArrayMinimum<double>(null));
    }

    [Fact]
    public void ArrayBsearchFindsExactMatchesAndNearestOtherwise()
    {
        Mql5Runtime runtime = Build();
        double[] buffer = [1, 3, 5, 7];

        Assert.Equal(2, runtime.ArrayBsearch(buffer, 5.0));
        Assert.Equal(0, runtime.ArrayBsearch(buffer, 0.0));
        Assert.Equal(3, runtime.ArrayBsearch(buffer, 99.0));
        Assert.Equal(1, runtime.ArrayBsearch(buffer, 4.0));
    }

    [Fact]
    public void ArraySeriesFlagIsRecordedPerArray()
    {
        Mql5Runtime runtime = Build();
        double[] flagged = new double[3];
        double[] plain = new double[3];

        Assert.True(runtime.ArraySetAsSeries(flagged, true));
        Assert.True(runtime.ArrayGetAsSeries(flagged));
        Assert.True(runtime.ArrayIsSeries(flagged));
        Assert.False(runtime.ArrayGetAsSeries(plain));

        Assert.True(runtime.ArraySetAsSeries(flagged, false));
        Assert.False(runtime.ArrayGetAsSeries(flagged));
    }

    [Fact]
    public void ArraySetAsSeriesOnANullArrayFails()
    {
        Mql5Runtime runtime = Build();
        Assert.False(runtime.ArraySetAsSeries<double>(null, true));
        Assert.Equal(Mql5ErrorCodes.InvalidArray, runtime.GetLastError());
    }

    [Fact]
    public void ArrayRangeReportsTheLengthForRankZeroOnly()
    {
        Mql5Runtime runtime = Build();
        double[] buffer = new double[6];

        Assert.Equal(6, runtime.ArrayRange(buffer, 0));
        Assert.Equal(0, runtime.ArrayRange(buffer, 1));
    }

    [Fact]
    public void ArrayReverseFlipsTheRequestedSpan()
    {
        Mql5Runtime runtime = Build();
        double[] buffer = [1, 2, 3, 4];

        Assert.True(runtime.ArrayReverse(buffer));
        Assert.Equal([4, 3, 2, 1], buffer);

        Assert.True(runtime.ArrayReverse(buffer, 1, 2));
        Assert.Equal([4, 2, 3, 1], buffer);
    }

    [Fact]
    public void ArrayCompareReportsTheSignAndMinusTwoOnError()
    {
        Mql5Runtime runtime = Build();

        Assert.Equal(0, runtime.ArrayCompare<double>([1, 2], [1, 2]));
        Assert.Equal(-1, runtime.ArrayCompare<double>([1, 2], [1, 3]));
        Assert.Equal(1, runtime.ArrayCompare<double>([1, 3], [1, 2]));
        Assert.Equal(-1, runtime.ArrayCompare<double>([1], [1, 2]));
        Assert.Equal(-2, runtime.ArrayCompare<double>(null, [1]));
    }

    [Fact]
    public void ArrayInsertGrowsTheDestination()
    {
        Mql5Runtime runtime = Build();
        double[]? destination = [1, 4];

        Assert.True(runtime.ArrayInsert(ref destination, [2, 3], 1));
        Assert.Equal([1, 2, 3, 4], destination!);
    }

    [Fact]
    public void ArrayRemoveShrinksTheArray()
    {
        Mql5Runtime runtime = Build();
        double[]? buffer = [1, 2, 3, 4];

        Assert.True(runtime.ArrayRemove(ref buffer, 1, 2));
        Assert.Equal([1, 4], buffer!);

        Assert.False(runtime.ArrayRemove(ref buffer, 99));
    }

    [Fact]
    public void ArraySwapExchangesTheBuffers()
    {
        Mql5Runtime runtime = Build();
        double[]? first = [1, 2];
        double[]? second = [9];

        Assert.True(runtime.ArraySwap(ref first, ref second));
        Assert.Equal([9], first!);
        Assert.Equal([1, 2], second!);
    }

    [Fact]
    public void ArrayPrintReachesTheLogSink()
    {
        Mql5LogRecorder sink = new();
        Mql5Runtime runtime = new(new FakeMarketContext(), new Mql5RuntimeOptions { LogSink = sink });

        runtime.ArrayPrint<double>([1.5, 2.5], digits: 2, separator: ",");

        Mql5LogEntry entry = Assert.Single(sink.Entries);
        Assert.Equal(Mql5LogChannel.ArrayPrint, entry.Channel);
        Assert.Equal("1.50,2.50", entry.Message);
    }

    [Fact]
    public void ArraysOfStringsWorkThroughTheSameGenericSurface()
    {
        Mql5Runtime runtime = Build();
        string[]? names = ["b", "a", "c"];

        Assert.True(runtime.ArraySort(names));
        Assert.Equal(["a", "b", "c"], names);
        Assert.Equal(3, runtime.ArraySize(names));
        Assert.Equal(2, runtime.ArrayMaximum(names));
    }
}
