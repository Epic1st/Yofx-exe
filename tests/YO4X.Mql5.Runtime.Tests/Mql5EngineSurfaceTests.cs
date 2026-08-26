using YO4X.Mql5.Runtime;

namespace YO4X.Mql5.Runtime.Tests;

/// <summary>
/// The engine-bound, indicator-bound, chart-stub and refused halves of the surface.
///
/// The load-bearing claims here are: engine-bound calls reach the context untouched and
/// resolve a null symbol to the current one; indicator handles are cached; chart stubs
/// remember what a strategy wrote so it can read it back; and every refused built-in
/// throws rather than answering plausibly.
/// </summary>
public sealed class Mql5EngineSurfaceTests
{
    private const int SymbolBid = 1;
    private const int SymbolAsk = 2;
    private const int AccountBalance = 10;
    private const int PositionVolume = 20;
    private const int PositionType = 21;

    [Fact]
    public void SymbolAccessorsComeFromTheContext()
    {
        FakeMarketContext context = new() { Symbol = "GBPUSD", Digits = 3, Point = 0.001 };
        Mql5Runtime runtime = new(context);

        Assert.Equal("GBPUSD", runtime.Symbol());
        Assert.Equal(3, runtime.Digits());
        Assert.Equal(0.001, runtime.Point());
    }

    [Fact]
    public void SymbolInfoReadsThroughInBothDocumentedShapes()
    {
        FakeMarketContext context = new();
        context.SymbolDoubles[SymbolBid] = 1.2345;
        context.SymbolIntegers[SymbolAsk] = 7;
        Mql5Runtime runtime = new(context);

        Assert.Equal(1.2345, runtime.SymbolInfoDouble("EURUSD", SymbolBid));
        Assert.True(runtime.SymbolInfoDouble("EURUSD", SymbolBid, out double bid));
        Assert.Equal(1.2345, bid);

        Assert.Equal(7, runtime.SymbolInfoInteger("EURUSD", SymbolAsk));
        Assert.True(runtime.SymbolInfoInteger("EURUSD", SymbolAsk, out long ask));
        Assert.Equal(7, ask);
    }

    [Fact]
    public void ANullOrEmptySymbolResolvesToTheCurrentSymbol()
    {
        FakeMarketContext context = new() { Symbol = "USDJPY", OpenPositions = 1 };
        Mql5Runtime runtime = new(context);

        Assert.True(runtime.PositionSelect(null));
        Assert.True(runtime.PositionSelect(string.Empty));
        Assert.Equal(["USDJPY", "USDJPY"], context.SelectedSymbols);
    }

    [Fact]
    public void AccountAndPositionReadsGoThrough()
    {
        FakeMarketContext context = new() { OpenPositions = 2 };
        context.AccountDoubles[AccountBalance] = 10_000;
        context.PositionDoubles[PositionVolume] = 0.25;
        context.PositionIntegers[PositionType] = 1;
        Mql5Runtime runtime = new(context);

        Assert.Equal(10_000, runtime.AccountInfoDouble(AccountBalance));
        Assert.Equal(2, runtime.PositionsTotal());
        Assert.Equal(0.25, runtime.PositionGetDouble(PositionVolume));
        Assert.Equal(1, runtime.PositionGetInteger(PositionType));
    }

    [Fact]
    public void AFailedPositionSelectLeavesTheDocumentedErrorCode()
    {
        FakeMarketContext context = new() { OpenPositions = 0 };
        Mql5Runtime runtime = new(context);

        Assert.False(runtime.PositionSelect("EURUSD"));
        Assert.Equal(Mql5ErrorCodes.TradePositionNotFound, runtime.GetLastError());

        runtime.ResetLastError();
        Assert.Equal(Mql5ErrorCodes.Success, runtime.GetLastError());
    }

    [Fact]
    public void OrderSendFillsTheResultStructureInPlace()
    {
        FakeMarketContext context = new();
        Mql5Runtime runtime = new(context);

        Mql5TradeRequest request = new() { Symbol = "EURUSD", Volume = 0.1, Price = 1.2345 };
        Mql5TradeResult result = new();

        Assert.True(runtime.OrderSend(request, result));
        Assert.Equal((uint)Mql5Constants.TradeRetcode.Done, result.Retcode);
        Assert.Equal(0.1, result.Volume);
        Assert.Equal(1.2345, result.Price);
        Assert.Same(request, Assert.Single(context.SentRequests));
    }

    [Fact]
    public void ARejectedOrderSendReportsFalseAndSetsTheErrorCode()
    {
        FakeMarketContext context = new() { AcceptOrders = false };
        Mql5Runtime runtime = new(context);

        Assert.False(runtime.OrderSend(new Mql5TradeRequest(), new Mql5TradeResult()));
        Assert.Equal(Mql5ErrorCodes.TradeSendFailed, runtime.GetLastError());
    }

    [Fact]
    public void TimeCurrentComesFromTheContextClockNotTheWallClock()
    {
        FakeMarketContext context = new() { TimeCurrent = new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc) };
        Mql5Runtime runtime = new(context);

        long now = runtime.TimeCurrent();
        Assert.Equal("2020.01.02 03:04:05", runtime.TimeToString(now, Mql5Constants.TimeDate | Mql5Constants.TimeSeconds));
    }

    [Fact]
    public void TickCountsAdvanceWithSimulatedTimeAndStartAtZero()
    {
        FakeMarketContext context = new() { TimeCurrent = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc) };
        Mql5Runtime runtime = new(context);

        Assert.Equal(0u, runtime.GetTickCount());

        context.TimeCurrent = context.TimeCurrent.AddSeconds(5);
        Assert.Equal(5000u, runtime.GetTickCount());
        Assert.Equal(5_000_000UL, runtime.GetMicrosecondCount());
    }

    [Fact]
    public void PeriodSecondsKnowsTheDocumentedTimeframes()
    {
        Mql5Runtime runtime = new(new FakeMarketContext());

        Assert.Equal(60, runtime.PeriodSeconds(Mql5Constants.Timeframes.M1));
        Assert.Equal(3600, runtime.PeriodSeconds(Mql5Constants.Timeframes.H1));
        Assert.Equal(86400, runtime.PeriodSeconds(Mql5Constants.Timeframes.D1));
        Assert.Equal(604800, runtime.PeriodSeconds(Mql5Constants.Timeframes.W1));
    }

    [Fact]
    public void IndicatorHandlesReachTheContextWithTheirMql5Name()
    {
        FakeMarketContext context = new();
        Mql5Runtime runtime = new(context);

        int handle = runtime.IMA("EURUSD", Mql5Constants.Timeframes.H1, 14, 0, 1, 0);

        Assert.NotEqual(Mql5Constants.InvalidHandle, handle);
        Assert.Equal("iMA:EURUSD,16385,14,0,1,0", Assert.Single(context.HandleRequests));
    }

    [Fact]
    public void IdenticalIndicatorRequestsShareOneHandle()
    {
        FakeMarketContext context = new();
        Mql5Runtime runtime = new(context);

        int first = runtime.IATR("EURUSD", Mql5Constants.Timeframes.H1, 14);
        int second = runtime.IATR("EURUSD", Mql5Constants.Timeframes.H1, 14);
        int different = runtime.IATR("EURUSD", Mql5Constants.Timeframes.H1, 20);

        Assert.Equal(first, second);
        Assert.NotEqual(first, different);
        Assert.Equal(2, context.HandleRequests.Count);
    }

    [Fact]
    public void CopyBufferReadsThroughTheContext()
    {
        FakeMarketContext context = new() { BufferValues = [1.0, 2.0, 3.0] };
        Mql5Runtime runtime = new(context);

        int handle = runtime.IRSI(null, 0, 14, 0);
        double[]? buffer = null;

        Assert.Equal(3, runtime.CopyBuffer(handle, 0, 0, 3, ref buffer));
        Assert.Equal([1.0, 2.0, 3.0], buffer!);
    }

    [Fact]
    public void CopyBufferOnAnInvalidHandleFailsWithoutThrowing()
    {
        Mql5Runtime runtime = new(new FakeMarketContext());
        double[]? buffer = null;

        Assert.Equal(-1, runtime.CopyBuffer(Mql5Constants.InvalidHandle, 0, 0, 10, ref buffer));
        Assert.Equal(Mql5ErrorCodes.IndicatorCannotCreate, runtime.GetLastError());
    }

    [Fact]
    public void CopySeriesReversesOutputForATargetFlaggedAsATimeseries()
    {
        Mql5Runtime runtime = new(new FakeMarketContext());

        double[]? chronological = null;
        Assert.Equal(4, runtime.CopyClose("EURUSD", Mql5Constants.Timeframes.H1, 0, 4, ref chronological));
        Assert.Equal([1.0, 2.0, 3.0, 4.0], chronological!);

        double[]? series = new double[4];
        runtime.ArraySetAsSeries(series, true);
        Assert.Equal(4, runtime.CopyClose("EURUSD", Mql5Constants.Timeframes.H1, 0, 4, ref series));
        Assert.Equal([4.0, 3.0, 2.0, 1.0], series!);
    }

    [Fact]
    public void UnimplementedContextMembersAnswerMql5FailureValues()
    {
        Mql5Runtime runtime = new(new FakeMarketContext());

        Assert.Equal(0, runtime.OrdersTotal());
        Assert.Equal(0UL, runtime.PositionGetTicket(0));
        Assert.False(runtime.OrderSelect(1));
        Assert.Equal(string.Empty, runtime.AccountInfoString(1));
        Assert.Equal(0, runtime.ITime("EURUSD", Mql5Constants.Timeframes.H1, 0));
        Assert.Equal(0, runtime.Bars("EURUSD", Mql5Constants.Timeframes.H1));
        Assert.Equal(-1, runtime.IBarShift("EURUSD", Mql5Constants.Timeframes.H1, 0));
        Assert.False(runtime.SymbolInfoTick("EURUSD", out _));
    }

    [Fact]
    public void ChartObjectsRememberWhatAStrategyWroteOnThem()
    {
        Mql5Runtime runtime = new(new FakeMarketContext());

        Assert.True(runtime.ObjectCreate(0, "panel", 42, 0, 100, 1.5));
        Assert.False(runtime.ObjectCreate(0, "panel", 42, 0, 100, 1.5));

        Assert.True(runtime.ObjectSetInteger(0, "panel", 7, 255));
        Assert.Equal(255, runtime.ObjectGetInteger(0, "panel", 7));

        Assert.True(runtime.ObjectSetDouble(0, "panel", 8, 1.75));
        Assert.Equal(1.75, runtime.ObjectGetDouble(0, "panel", 8));

        Assert.True(runtime.ObjectSetString(0, "panel", 9, "hello"));
        Assert.Equal("hello", runtime.ObjectGetString(0, "panel", 9));

        Assert.Equal(0, runtime.ObjectFind(0, "panel"));
        Assert.Equal(1, runtime.ObjectsTotal(0));
        Assert.Equal("panel", runtime.ObjectName(0, 0));
    }

    [Fact]
    public void ChartObjectPropertyWritesToAMissingObjectFail()
    {
        Mql5Runtime runtime = new(new FakeMarketContext());

        Assert.False(runtime.ObjectSetInteger(0, "ghost", 1, 1));
        Assert.Equal(Mql5ErrorCodes.ObjectNotFound, runtime.GetLastError());
        Assert.Equal(-1, runtime.ObjectFind(0, "ghost"));
    }

    [Fact]
    public void ObjectsDeleteAllHonoursTheNamePrefix()
    {
        Mql5Runtime runtime = new(new FakeMarketContext());

        runtime.ObjectCreate(0, "ea_a", 1, 0, 0, 0);
        runtime.ObjectCreate(0, "ea_b", 1, 0, 0, 0);
        runtime.ObjectCreate(0, "other", 1, 0, 0, 0);

        Assert.Equal(2, runtime.ObjectsDeleteAll(0, "ea_"));
        Assert.Equal(1, runtime.ObjectsTotal(0));
        Assert.Equal("other", runtime.ObjectName(0, 0));
    }

    [Fact]
    public void ObjectDeleteReportsWhetherAnythingWasThere()
    {
        Mql5Runtime runtime = new(new FakeMarketContext());

        runtime.ObjectCreate(0, "line", 1, 0, 0, 0);
        Assert.True(runtime.ObjectDelete(0, "line"));
        Assert.False(runtime.ObjectDelete(0, "line"));
    }

    [Fact]
    public void ObjectMoveUpdatesAnAnchor()
    {
        Mql5Runtime runtime = new(new FakeMarketContext());
        runtime.ObjectCreate(0, "trend", 1, 0, 100, 1.0);

        Assert.True(runtime.ObjectMove(0, "trend", 1, 200, 2.0));
        Assert.False(runtime.ObjectMove(0, "missing", 0, 0, 0));

        Mql5ChartObject stored = Assert.Single(runtime.ChartObjects.Objects(0));
        Assert.Equal((200L, 2.0), stored.Anchors[1]);
    }

    [Fact]
    public void ChartPropertiesRoundTripThroughTheRecording()
    {
        Mql5Runtime runtime = new(new FakeMarketContext());

        Assert.True(runtime.ChartSetInteger(0, 5, 1));
        Assert.Equal(1, runtime.ChartGetInteger(0, 5));

        Assert.True(runtime.ChartSetDouble(0, 6, 2.5));
        Assert.Equal(2.5, runtime.ChartGetDouble(0, 6));

        Assert.True(runtime.ChartSetString(0, 7, "title"));
        Assert.Equal("title", runtime.ChartGetString(0, 7));
    }

    [Fact]
    public void ChartSymbolAndPeriodComeFromTheMarketContext()
    {
        FakeMarketContext context = new() { Symbol = "XAUUSD" };
        Mql5Runtime runtime = new(context);

        Assert.Equal("XAUUSD", runtime.ChartSymbol());
        Assert.Equal(Mql5Constants.Timeframes.Current, runtime.ChartPeriod());
    }

    [Fact]
    public void TextGetSizeReportsANonZeroMetric()
    {
        Mql5Runtime runtime = new(new FakeMarketContext());

        Assert.True(runtime.TextGetSize("abc", out uint width, out uint height));
        Assert.True(width > 0);
        Assert.True(height > 0);
    }

    [Fact]
    public void ChartCallsCanBeRoutedToTheLogSink()
    {
        Mql5LogRecorder sink = new();
        Mql5Runtime runtime = new(new FakeMarketContext(), new Mql5RuntimeOptions { LogSink = sink, LogChartCalls = true });

        runtime.ObjectCreate(0, "x", 1, 0, 0, 0);

        Assert.Contains(sink.Entries, entry => entry.Channel == Mql5LogChannel.Chart && entry.Message == "ObjectCreate");
    }

    [Theory]
    [InlineData("FileOpen")]
    [InlineData("FileWrite")]
    [InlineData("FileReadString")]
    [InlineData("FolderCreate")]
    [InlineData("TerminalInfoInteger")]
    [InlineData("WebRequest")]
    [InlineData("SendMail")]
    [InlineData("SendNotification")]
    [InlineData("Sleep")]
    [InlineData("MessageBox")]
    [InlineData("TerminalInfoString")]
    [InlineData("ICustom")]
    [InlineData("ChartScreenShot")]
    [InlineData("CalendarValueHistory")]
    public void RefusedBuiltinsThrowAndNameThemselves(string function)
    {
        Mql5Runtime runtime = new(new FakeMarketContext());
        byte[]? bytes = null;
        string headers = string.Empty;


        Mql5UnsupportedOperationException failure = Assert.Throws<Mql5UnsupportedOperationException>(() =>
        {
            switch (function)
            {
                case "FileOpen": runtime.FileOpen("x", 0); break;
                case "FileWrite": runtime.FileWrite(1, "x"); break;
                case "FileReadString": runtime.FileReadString(1); break;
                case "FolderCreate": runtime.FolderCreate("x"); break;
                // TERMINAL_BUILD. Trade permission and its neighbours are answered; the build
                // number describes a MetaTrader installation that does not exist here.
                case "TerminalInfoInteger": runtime.TerminalInfoInteger(5); break;
                case "WebRequest": runtime.WebRequest("GET", "http://x", null, 0, null, ref bytes, ref headers); break;
                case "SendMail": runtime.SendMail("s", "b"); break;
                case "SendNotification": runtime.SendNotification("t"); break;
                case "Sleep": runtime.Sleep(10); break;
                case "MessageBox": runtime.MessageBox("t"); break;
                case "TerminalInfoString": runtime.TerminalInfoString(1); break;
                case "ICustom": runtime.ICustom("EURUSD", 0, "MyInd"); break;
                case "ChartScreenShot": runtime.ChartScreenShot(0, "f", 1, 1); break;
                case "CalendarValueHistory": runtime.CalendarValueHistory(0, 0); break;
                default: throw new InvalidOperationException(function);
            }
        });

        Assert.Equal(function, failure.FunctionName);
        Assert.Contains(function, failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ConstructingWithoutAContextIsRejected()
    {
        Assert.Throws<ArgumentNullException>(() => new Mql5Runtime(null!));
    }
}
