using YO4X.Mql5.Runtime;

namespace YO4X.Mql5.Runtime.Tests;

/// <summary>
/// The conversion surface, led by <c>NormalizeDouble</c>.
///
/// <c>NormalizeDouble</c> is called 1228 times across the lowered corpus and every
/// price a strategy sends passes through it, so its rounding mode is load-bearing:
/// MQL5 rounds half away from zero and clamps <c>digits</c> to 0 to 8. Rounding half to
/// even instead would move a stop by one point on every exact tie, in every strategy.
/// </summary>
public sealed class Mql5ConversionTests
{
    private static Mql5Runtime Build() => new(new FakeMarketContext());

    [Theory]
    [InlineData(1.234567891, 5, 1.23457)]
    [InlineData(1.234564891, 5, 1.23456)]
    [InlineData(0.1 + 0.2, 2, 0.3)]
    [InlineData(1.005, 2, 1.0)]
    [InlineData(123.456, 0, 123.0)]
    [InlineData(-1.234567891, 5, -1.23457)]
    public void NormalizeDoubleRoundsToTheRequestedPrecision(double value, int digits, double expected)
    {
        Mql5Runtime runtime = Build();
        Assert.Equal(expected, runtime.NormalizeDouble(value, digits), 10);
    }

    [Fact]
    public void NormalizeDoubleRoundsTiesAwayFromZero()
    {
        Mql5Runtime runtime = Build();

        Assert.Equal(0.13, runtime.NormalizeDouble(0.125, 2), 10);
        Assert.Equal(-0.13, runtime.NormalizeDouble(-0.125, 2), 10);
        Assert.Equal(3.0, runtime.NormalizeDouble(2.5, 0), 10);
        Assert.Equal(-3.0, runtime.NormalizeDouble(-2.5, 0), 10);
        Assert.Equal(2.0, runtime.NormalizeDouble(1.5, 0), 10);
    }

    [Fact]
    public void NormalizeDoubleClampsDigitsToTheDocumentedRange()
    {
        Mql5Runtime runtime = Build();

        // MQL5 documents 0 to 8. Anything outside clamps rather than failing, because
        // strategies pass Digits() straight in.
        Assert.Equal(123.0, runtime.NormalizeDouble(123.456, -3), 10);
        Assert.Equal(runtime.NormalizeDouble(1.23456789012, 8), runtime.NormalizeDouble(1.23456789012, 15), 12);
    }

    [Fact]
    public void NormalizeDoublePassesNonFiniteValuesThrough()
    {
        Mql5Runtime runtime = Build();

        Assert.True(double.IsNaN(runtime.NormalizeDouble(double.NaN, 5)));
        Assert.True(double.IsPositiveInfinity(runtime.NormalizeDouble(double.PositiveInfinity, 5)));
        Assert.True(double.IsNegativeInfinity(runtime.NormalizeDouble(double.NegativeInfinity, 5)));
    }

    [Fact]
    public void NormalizeDoubleIsIdempotent()
    {
        Mql5Runtime runtime = Build();

        double once = runtime.NormalizeDouble(1.234567891, 5);
        Assert.Equal(once, runtime.NormalizeDouble(once, 5), 12);
    }

    [Theory]
    [InlineData(1.5, 2, "1.50")]
    [InlineData(1.23456789, 4, "1.2346")]
    [InlineData(-1.5, 0, "-2")]
    [InlineData(1234.5, 1, "1234.5")]
    [InlineData(0.0, 3, "0.000")]
    public void DoubleToStringUsesFixedNotationForNonNegativeDigits(double value, int digits, string expected)
    {
        Mql5Runtime runtime = Build();
        Assert.Equal(expected, runtime.DoubleToString(value, digits));
    }

    [Fact]
    public void DoubleToStringUsesScientificNotationForNegativeDigits()
    {
        Mql5Runtime runtime = Build();

        Assert.Equal("1.2345e+03", runtime.DoubleToString(1234.5, -4));
        Assert.Equal("-1.23e+03", runtime.DoubleToString(-1234.5, -2));
    }

    [Fact]
    public void DoubleToStringDefaultsToEightDecimals()
    {
        Mql5Runtime runtime = Build();
        Assert.Equal("1.50000000", runtime.DoubleToString(1.5));
    }

    [Theory]
    [InlineData(42L, 0, "42")]
    [InlineData(42L, 5, "   42")]
    [InlineData(-42L, 6, "   -42")]
    [InlineData(42L, 1, "42")]
    public void IntegerToStringRightAligns(long number, int length, string expected)
    {
        Mql5Runtime runtime = Build();
        Assert.Equal(expected, runtime.IntegerToString(number, length));
    }

    [Fact]
    public void IntegerToStringHonoursTheFillSymbol()
    {
        Mql5Runtime runtime = Build();
        Assert.Equal("00042", runtime.IntegerToString(42, 5, '0'));
    }

    [Theory]
    [InlineData("3.14abc", 3.14)]
    [InlineData("  -2.5", -2.5)]
    [InlineData("1e3", 1000.0)]
    [InlineData("abc", 0.0)]
    [InlineData("", 0.0)]
    [InlineData("12", 12.0)]
    public void StringToDoubleReadsTheLeadingNumericPrefix(string text, double expected)
    {
        Mql5Runtime runtime = Build();
        Assert.Equal(expected, runtime.StringToDouble(text), 10);
    }

    [Theory]
    [InlineData("42abc", 42L)]
    [InlineData("-17", -17L)]
    [InlineData("  8 ", 8L)]
    [InlineData("abc", 0L)]
    [InlineData("3.9", 3L)]
    public void StringToIntegerReadsTheLeadingIntegerPrefix(string text, long expected)
    {
        Mql5Runtime runtime = Build();
        Assert.Equal(expected, runtime.StringToInteger(text));
    }

    [Fact]
    public void TimeToStringHonoursTheModeBitmask()
    {
        Mql5Runtime runtime = Build();
        long moment = runtime.StringToTime("2024.03.15 12:30:45");

        Assert.Equal("2024.03.15 12:30", runtime.TimeToString(moment));
        Assert.Equal("2024.03.15", runtime.TimeToString(moment, Mql5Constants.TimeDate));
        Assert.Equal("12:30", runtime.TimeToString(moment, Mql5Constants.TimeMinutes));
        Assert.Equal("12:30:45", runtime.TimeToString(moment, Mql5Constants.TimeSeconds));
        Assert.Equal("2024.03.15 12:30:45", runtime.TimeToString(moment, Mql5Constants.TimeDate | Mql5Constants.TimeSeconds));
    }

    [Theory]
    [InlineData("2024.03.15 12:30:45")]
    [InlineData("2024.03.15 12:30")]
    [InlineData("2024.03.15")]
    [InlineData("2024/03/15")]
    [InlineData("2024-03-15")]
    public void StringToTimeAcceptsTheDocumentedForms(string text)
    {
        Mql5Runtime runtime = Build();
        long parsed = runtime.StringToTime(text);

        Assert.True(parsed > 0);
        Assert.Equal("2024.03.15", runtime.TimeToString(parsed, Mql5Constants.TimeDate));
    }

    [Fact]
    public void StringToTimeReturnsZeroForNonsense()
    {
        Mql5Runtime runtime = Build();
        Assert.Equal(0, runtime.StringToTime("not a date"));
        Assert.Equal(Mql5ErrorCodes.InvalidDatetime, runtime.GetLastError());
    }

    [Fact]
    public void TimeToStructAndStructToTimeRoundTrip()
    {
        Mql5Runtime runtime = Build();
        long moment = runtime.StringToTime("2024.03.15 12:30:45");

        Assert.True(runtime.TimeToStruct(moment, out Mql5DateTime broken));
        Assert.Equal(2024, broken.Year);
        Assert.Equal(3, broken.Month);
        Assert.Equal(15, broken.Day);
        Assert.Equal(12, broken.Hour);
        Assert.Equal(30, broken.Minute);
        Assert.Equal(45, broken.Second);
        Assert.Equal(5, broken.DayOfWeek);
        Assert.Equal(75, broken.DayOfYear);
        Assert.Equal(moment, runtime.StructToTime(broken));
    }

    [Fact]
    public void StructToTimeAnswersZeroForAnImpossibleDate()
    {
        Mql5Runtime runtime = Build();
        Mql5DateTime broken = new() { Year = 2024, Month = 13, Day = 40 };

        Assert.Equal(0, runtime.StructToTime(broken));
    }

    [Fact]
    public void CharAndShortToStringProduceOneCharacter()
    {
        Mql5Runtime runtime = Build();

        Assert.Equal("A", runtime.CharToString(65));
        Assert.Equal("A", runtime.ShortToString(65));
    }

    [Fact]
    public void ColorConversionsRoundTripThroughMql5ByteOrder()
    {
        Mql5Runtime runtime = Build();

        int red = Mql5Colors.Pack(255, 0, 0);
        Assert.Equal("255,0,0", runtime.ColorToString(red));
        Assert.Equal("clrRed", runtime.ColorToString(red, useColorName: true));
        Assert.Equal(red, runtime.StringToColor("clrRed"));
        Assert.Equal(red, runtime.StringToColor("255,0,0"));
        Assert.Equal(Mql5Constants.ColorNone, runtime.StringToColor("not a colour"));
    }

    [Fact]
    public void ColorToArgbReordersTheBytesAndAppliesAlpha()
    {
        Mql5Runtime runtime = Build();

        int color = Mql5Colors.Pack(0x12, 0x34, 0x56);
        Assert.Equal(0xFF123456u, runtime.ColorToArgb(color));
        Assert.Equal(0x80123456u, runtime.ColorToArgb(color, 0x80));
    }

    [Fact]
    public void CharArrayAndStringRoundTrip()
    {
        Mql5Runtime runtime = Build();

        byte[]? buffer = null;
        int written = runtime.StringToCharArray("hello", ref buffer);

        Assert.Equal(6, written);
        Assert.Equal("hello", runtime.CharArrayToString(buffer));
    }

    [Fact]
    public void ShortArrayAndStringRoundTrip()
    {
        Mql5Runtime runtime = Build();

        ushort[]? buffer = null;
        int written = runtime.StringToShortArray("hi", ref buffer);

        Assert.Equal(3, written);
        Assert.Equal("hi", runtime.ShortArrayToString(buffer));
    }

    [Fact]
    public void StructConversionsReportFailureRatherThanFabricating()
    {
        Mql5Runtime runtime = Build();
        byte[]? buffer = new byte[8];

        Assert.False(runtime.CharArrayToStruct(buffer));
        Assert.False(runtime.StructToCharArray(ref buffer));
        Assert.Equal(Mql5ErrorCodes.StructWithObjectsOrClass, runtime.GetLastError());
    }
}
