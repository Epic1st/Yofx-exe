using YO4X.Mql5.Runtime;

namespace YO4X.Mql5.Runtime.Tests;

/// <summary>
/// <c>StringFormat</c> and <c>PrintFormat</c> against printf's grammar.
///
/// MQL5 inherits printf verbatim, so these expectations are C's, not .NET's. The cases
/// that matter most are the ones a naive translation into .NET composite formatting
/// gets wrong: flag combinations, star-supplied width and precision, and the exponent
/// form - C prints a two-digit exponent where .NET's own <c>"E"</c> specifier prints
/// three.
/// </summary>
public sealed class Mql5FormatTests
{
    private static Mql5Runtime Build() => new(new FakeMarketContext());

    [Theory]
    [InlineData("%d", 42, "42")]
    [InlineData("%i", 42, "42")]
    [InlineData("%5d", 42, "   42")]
    [InlineData("%-5d|", 42, "42   |")]
    [InlineData("%05d", 42, "00042")]
    [InlineData("%+d", 42, "+42")]
    [InlineData("% d", 42, " 42")]
    [InlineData("%d", -42, "-42")]
    [InlineData("%05d", -42, "-0042")]
    [InlineData("%.5d", 42, "00042")]
    public void IntegerConversionsFollowPrintf(string format, int value, string expected)
    {
        Mql5Runtime runtime = Build();
        Assert.Equal(expected, runtime.StringFormat(format, value));
    }

    [Theory]
    [InlineData("%x", 255, "ff")]
    [InlineData("%X", 255, "FF")]
    [InlineData("%#x", 255, "0xff")]
    [InlineData("%#X", 255, "0XFF")]
    [InlineData("%08x", 255, "000000ff")]
    [InlineData("%o", 8, "10")]
    [InlineData("%u", 7, "7")]
    public void RadixConversionsFollowPrintf(string format, int value, string expected)
    {
        Mql5Runtime runtime = Build();
        Assert.Equal(expected, runtime.StringFormat(format, value));
    }

    [Theory]
    [InlineData("%f", 3.5, "3.500000")]
    [InlineData("%.2f", 3.14159, "3.14")]
    [InlineData("%.0f", 3.14159, "3")]
    [InlineData("%8.3f", 3.14159, "   3.142")]
    [InlineData("%-8.3f|", 3.14159, "3.142   |")]
    [InlineData("%08.3f", 3.14159, "0003.142")]
    [InlineData("%+.2f", 3.14159, "+3.14")]
    [InlineData("%.2f", -3.14159, "-3.14")]
    public void FixedConversionsFollowPrintf(string format, double value, string expected)
    {
        Mql5Runtime runtime = Build();
        Assert.Equal(expected, runtime.StringFormat(format, value));
    }

    [Theory]
    [InlineData("%e", 1234.5, "1.234500e+03")]
    [InlineData("%E", 1234.5, "1.234500E+03")]
    [InlineData("%.2e", 1234.5, "1.23e+03")]
    [InlineData("%.0e", 1234.5, "1e+03")]
    [InlineData("%e", 0.000012345, "1.234500e-05")]
    public void ScientificConversionsUseATwoDigitExponent(string format, double value, string expected)
    {
        Mql5Runtime runtime = Build();
        Assert.Equal(expected, runtime.StringFormat(format, value));
    }

    [Theory]
    [InlineData("%g", 0.0001, "0.0001")]
    [InlineData("%g", 0.00001, "1e-05")]
    [InlineData("%g", 123456789.0, "1.23457e+08")]
    [InlineData("%g", 1.5, "1.5")]
    [InlineData("%g", 100.0, "100")]
    [InlineData("%.3g", 3.14159, "3.14")]
    public void GeneralConversionsPickTheShorterForm(string format, double value, string expected)
    {
        Mql5Runtime runtime = Build();
        Assert.Equal(expected, runtime.StringFormat(format, value));
    }

    [Theory]
    [InlineData("%s", "abc", "abc")]
    [InlineData("%.2s", "abcdef", "ab")]
    [InlineData("%6s|", "abc", "   abc|")]
    [InlineData("%-6s|", "abc", "abc   |")]
    public void StringConversionsFollowPrintf(string format, string value, string expected)
    {
        Mql5Runtime runtime = Build();
        Assert.Equal(expected, runtime.StringFormat(format, value));
    }

    [Fact]
    public void CharacterAndLiteralPercentAreHandled()
    {
        Mql5Runtime runtime = Build();

        Assert.Equal("A", runtime.StringFormat("%c", 65));
        Assert.Equal("100%", runtime.StringFormat("100%%"));
        Assert.Equal("50% done", runtime.StringFormat("%d%% done", 50));
    }

    [Fact]
    public void StarSuppliedWidthAndPrecisionConsumeArguments()
    {
        Mql5Runtime runtime = Build();

        Assert.Equal("   42", runtime.StringFormat("%*d", 5, 42));
        Assert.Equal("3.14", runtime.StringFormat("%.*f", 2, 3.14159));
        Assert.Equal("42   |", runtime.StringFormat("%*d|", -5, 42));
    }

    [Fact]
    public void LengthModifiersAreConsumedRatherThanEmitted()
    {
        Mql5Runtime runtime = Build();

        Assert.Equal("5", runtime.StringFormat("%I64d", 5L));
        Assert.Equal("5", runtime.StringFormat("%lld", 5L));
        Assert.Equal("5", runtime.StringFormat("%ld", 5L));
        Assert.Equal("5", runtime.StringFormat("%hd", (short)5));
    }

    [Fact]
    public void SeveralConversionsInOneFormatConsumeArgumentsInOrder()
    {
        Mql5Runtime runtime = Build();

        Assert.Equal(
            "EURUSD buy 0.10 at 1.23456",
            runtime.StringFormat("%s %s %.2f at %.5f", "EURUSD", "buy", 0.1, 1.23456));
    }

    [Fact]
    public void MalformedSpecifiersAreEmittedLiterallyRatherThanThrowing()
    {
        Mql5Runtime runtime = Build();

        Assert.Equal("%", runtime.StringFormat("%"));
        Assert.Equal("%q", runtime.StringFormat("%q", 1));
        Assert.Equal("%5", runtime.StringFormat("%5"));
    }

    [Fact]
    public void MissingArgumentsDegradeRatherThanThrowing()
    {
        Mql5Runtime runtime = Build();

        Assert.Equal("0", runtime.StringFormat("%d"));
        Assert.Equal("", runtime.StringFormat("%s"));
        Assert.Equal("0.00", runtime.StringFormat("%.2f"));
    }

    [Fact]
    public void FormattingIsCultureInvariant()
    {
        System.Globalization.CultureInfo original = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
            Mql5Runtime runtime = Build();

            Assert.Equal("1.50", runtime.StringFormat("%.2f", 1.5));
            Assert.Equal("1.50000000", runtime.DoubleToString(1.5));
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void NonFiniteValuesRenderTheWayTheCRuntimeRendersThem()
    {
        Mql5Runtime runtime = Build();

        Assert.Equal("nan", runtime.StringFormat("%f", double.NaN));
        Assert.Equal("inf", runtime.StringFormat("%f", double.PositiveInfinity));
        Assert.Equal("-inf", runtime.StringFormat("%f", double.NegativeInfinity));
    }

    [Fact]
    public void DescribeRendersValuesTheWayPrintDoes()
    {
        Assert.Equal("true", Mql5Format.Describe(true));
        Assert.Equal("false", Mql5Format.Describe(false));
        Assert.Equal("0.1", Mql5Format.Describe(0.1));
        Assert.Equal("2", Mql5Format.Describe(2.0));
        Assert.Equal("0.3333333333333333", Mql5Format.Describe(1.0 / 3.0));
        Assert.Equal("42", Mql5Format.Describe(42));
        Assert.Equal("abc", Mql5Format.Describe("abc"));
        Assert.Equal(string.Empty, Mql5Format.Describe(null));
    }

    [Fact]
    public void PrintConcatenatesWithoutSeparatorsAndReachesTheSink()
    {
        Mql5LogRecorder sink = new();
        Mql5Runtime runtime = new(new FakeMarketContext(), new Mql5RuntimeOptions { LogSink = sink });

        runtime.Print("lots=", 0.1, " ok=", true);

        Mql5LogEntry entry = Assert.Single(sink.Entries);
        Assert.Equal(Mql5LogChannel.Print, entry.Channel);
        Assert.Equal("lots=0.1 ok=true", entry.Message);
    }

    [Fact]
    public void PrintFormatReachesTheSinkOnThePrintChannel()
    {
        Mql5LogRecorder sink = new();
        Mql5Runtime runtime = new(new FakeMarketContext(), new Mql5RuntimeOptions { LogSink = sink });

        runtime.PrintFormat("open %s at %.5f", "EURUSD", 1.2345);

        Mql5LogEntry entry = Assert.Single(sink.Entries);
        Assert.Equal(Mql5LogChannel.Print, entry.Channel);
        Assert.Equal("open EURUSD at 1.23450", entry.Message);
    }

    [Fact]
    public void CommentAndAlertUseTheirOwnChannels()
    {
        Mql5LogRecorder sink = new();
        Mql5Runtime runtime = new(new FakeMarketContext(), new Mql5RuntimeOptions { LogSink = sink });

        runtime.Comment("panel");
        runtime.Alert("stop hit");

        Assert.Equal(Mql5LogChannel.Comment, sink.Entries[0].Channel);
        Assert.Equal(Mql5LogChannel.Alert, sink.Entries[1].Channel);
    }

    [Fact]
    public void TheLogRecorderIsBounded()
    {
        Mql5LogRecorder sink = new(capacity: 4);
        Mql5Runtime runtime = new(new FakeMarketContext(), new Mql5RuntimeOptions { LogSink = sink });

        for (int index = 0; index < 20; index++)
        {
            runtime.Print(index);
        }

        Assert.Equal(4, sink.Entries.Count);
        Assert.Equal("19", sink.Entries[^1].Message);
    }
}
