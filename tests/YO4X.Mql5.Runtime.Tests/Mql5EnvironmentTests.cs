using YO4X.Mql5.Runtime;

namespace YO4X.Mql5.Runtime.Tests;

/// <summary>
/// The surface that describes the environment rather than the market: terminal global
/// variables, <c>TerminalInfoInteger</c> and the depth of market.
///
/// All three used to throw. The tests here pin the two things that replaced the
/// refusals - a per-run global-variable store and a truthful answer for the terminal
/// properties this engine can actually answer - and pin the parts that are still
/// refused, so that relaxing one of those becomes a deliberate act rather than a
/// side effect.
/// </summary>
public sealed class Mql5EnvironmentTests
{
    // 2024-03-15T12:30:45Z, the FakeMarketContext clock, as an MQL5 datetime.
    private static readonly long ContextNow =
        Mql5Time.FromDateTime(new DateTime(2024, 3, 15, 12, 30, 45, DateTimeKind.Utc));

    private static Mql5Runtime Build() => new(new FakeMarketContext());

    // ------------------------------------------------------ global variables ---

    [Fact]
    public void AGlobalVariableRoundTripsThroughTheRun()
    {
        Mql5Runtime runtime = Build();

        Assert.False(runtime.GlobalVariableCheck("PM_LastLot_EURUSD"));

        long stamped = runtime.GlobalVariableSet("PM_LastLot_EURUSD", 0.25);

        Assert.Equal(ContextNow, stamped);
        Assert.True(runtime.GlobalVariableCheck("PM_LastLot_EURUSD"));
        Assert.Equal(0.25, runtime.GlobalVariableGet("PM_LastLot_EURUSD"));
        Assert.Equal(1, runtime.GlobalVariablesTotal());
    }

    [Fact]
    public void EachRunStartsWithAnEmptySet()
    {
        // The whole justification for implementing the family rather than refusing it:
        // the store cannot outlive the run, so two runs over the same bars see the same
        // state. One runtime instance is one run.
        Mql5Runtime first = Build();
        first.GlobalVariableSet("carried", 42);

        Mql5Runtime second = Build();

        Assert.Equal(1, first.GlobalVariablesTotal());
        Assert.Equal(0, second.GlobalVariablesTotal());
        Assert.False(second.GlobalVariableCheck("carried"));
    }

    [Fact]
    public void AMissingGlobalVariableReadsAsZeroAndRecordsNotFound()
    {
        Mql5Runtime runtime = Build();

        Assert.Equal(0.0, runtime.GlobalVariableGet("absent"));
        Assert.Equal(Mql5ErrorCodes.GlobalVariableNotFound, runtime.LastError);
    }

    [Fact]
    public void TheOutParameterFormSeparatesAStoredZeroFromAMiss()
    {
        Mql5Runtime runtime = Build();
        runtime.GlobalVariableSet("stored", 0);

        Assert.True(runtime.GlobalVariableGet("stored", out double stored));
        Assert.Equal(0.0, stored);

        Assert.False(runtime.GlobalVariableGet("absent", out double missing));
        Assert.Equal(0.0, missing);
        Assert.Equal(Mql5ErrorCodes.GlobalVariableNotFound, runtime.LastError);
    }

    [Fact]
    public void DeletingAGlobalVariableRemovesItAndReportsASecondAttempt()
    {
        Mql5Runtime runtime = Build();
        runtime.GlobalVariableSet("doomed", 1);

        Assert.True(runtime.GlobalVariableDel("doomed"));
        Assert.Equal(0, runtime.GlobalVariablesTotal());

        Assert.False(runtime.GlobalVariableDel("doomed"));
        Assert.Equal(Mql5ErrorCodes.GlobalVariableNotFound, runtime.LastError);
    }

    [Fact]
    public void NamesAreIndexedInCreationOrderSoADownwardDeleteLoopVisitsEveryOne()
    {
        // This is the corpus pattern verbatim: walk the set backwards deleting every
        // name that carries the strategy's prefix. It only works if removing index i
        // leaves every lower index where it was.
        Mql5Runtime runtime = Build();
        runtime.GlobalVariableSet("ants_globalBalans", 1000);
        runtime.GlobalVariableSet("other_untouched", 7);
        runtime.GlobalVariableSet("ants_PreLots", 0.1);

        Assert.Equal("ants_globalBalans", runtime.GlobalVariableName(0));
        Assert.Equal("other_untouched", runtime.GlobalVariableName(1));
        Assert.Equal("ants_PreLots", runtime.GlobalVariableName(2));

        for (int i = runtime.GlobalVariablesTotal() - 1; i >= 0; i--)
        {
            string name = runtime.GlobalVariableName(i);
            if (name.StartsWith("ants_", StringComparison.Ordinal))
            {
                runtime.GlobalVariableDel(name);
            }
        }

        Assert.Equal(1, runtime.GlobalVariablesTotal());
        Assert.Equal("other_untouched", runtime.GlobalVariableName(0));
    }

    [Fact]
    public void AnIndexOutsideTheSetReadsAsAnEmptyName()
    {
        Mql5Runtime runtime = Build();

        Assert.Equal(string.Empty, runtime.GlobalVariableName(0));
        Assert.Equal(Mql5ErrorCodes.InvalidParameter, runtime.LastError);
    }

    [Fact]
    public void ANameLongerThanMetaTraderAcceptsIsRejected()
    {
        // MQL5: "A global variable name should not exceed 63 characters." A name this
        // runtime accepted and MetaTrader rejected would make the two disagree about
        // whether the strategy has any stored state at all.
        Mql5Runtime runtime = Build();

        Assert.NotEqual(0, runtime.GlobalVariableSet(new string('n', 63), 1));
        Assert.Equal(0, runtime.GlobalVariableSet(new string('n', 64), 1));
        Assert.Equal(Mql5ErrorCodes.InvalidParameter, runtime.LastError);
        Assert.Equal(1, runtime.GlobalVariablesTotal());
    }

    [Fact]
    public void ReadingAGlobalVariableCountsAsAnAccess()
    {
        // MQL5 documents this on GlobalVariableTime: "Addressing a variable for its
        // value, for example using the GlobalVariableGet() and GlobalVariableCheck()
        // functions, also modifies the time of last access."
        FakeMarketContext context = new();
        Mql5Runtime runtime = new(context);
        runtime.GlobalVariableSet("touched", 1);

        Assert.Equal(ContextNow, runtime.GlobalVariableTime("touched"));

        context.TimeCurrent = context.TimeCurrent.AddHours(1);
        runtime.GlobalVariableGet("touched");

        Assert.Equal(ContextNow + 3600, runtime.GlobalVariableTime("touched"));
    }

    [Fact]
    public void TheTimestampFollowsTheSimulatedClockNotTheWallClock()
    {
        FakeMarketContext context = new() { TimeCurrent = new DateTime(2019, 1, 2, 3, 4, 5, DateTimeKind.Utc) };
        Mql5Runtime runtime = new(context);

        long stamped = runtime.GlobalVariableSet("clocked", 1);

        Assert.Equal(Mql5Time.FromDateTime(context.TimeCurrent), stamped);
    }

    [Fact]
    public void ATemporaryVariableIsCreatedOnceAndThenReportsThatItExists()
    {
        Mql5Runtime runtime = Build();

        Assert.True(runtime.GlobalVariableTemp("lock"));
        Assert.Equal(0.0, runtime.GlobalVariableGet("lock"));

        Assert.False(runtime.GlobalVariableTemp("lock"));
        Assert.Equal(Mql5ErrorCodes.GlobalVariableExists, runtime.LastError);
    }

    [Fact]
    public void ConditionalSetAssignsOnlyWhenTheStoredValueStillMatches()
    {
        Mql5Runtime runtime = Build();
        runtime.GlobalVariableSet("mutex", 0);

        Assert.True(runtime.GlobalVariableSetOnCondition("mutex", 1, 0));
        Assert.Equal(1.0, runtime.GlobalVariableGet("mutex"));

        Assert.False(runtime.GlobalVariableSetOnCondition("mutex", 2, 0));
        Assert.Equal(Mql5ErrorCodes.GlobalVariableNotModified, runtime.LastError);
        Assert.Equal(1.0, runtime.GlobalVariableGet("mutex"));
    }

    [Fact]
    public void ConditionalSetOnAMissingVariableReportsNotFoundRatherThanNotModified()
    {
        // The distinction matters: the usual idiom reads NOT_FOUND as "create it", and
        // answering NOT_MODIFIED for an absent variable would strand that branch.
        Mql5Runtime runtime = Build();

        Assert.False(runtime.GlobalVariableSetOnCondition("absent", 1, 0));
        Assert.Equal(Mql5ErrorCodes.GlobalVariableNotFound, runtime.LastError);
    }

    [Fact]
    public void FlushingIsAcceptedAndChangesNothing()
    {
        Mql5Runtime runtime = Build();
        runtime.GlobalVariableSet("kept", 5);

        runtime.GlobalVariablesFlush();

        Assert.Equal(5.0, runtime.GlobalVariableGet("kept"));
        Assert.Equal(1, runtime.GlobalVariablesTotal());
    }

    [Fact]
    public void DeleteAllHonoursThePrefixAndReportsTheCount()
    {
        Mql5Runtime runtime = Build();
        runtime.GlobalVariableSet("BRT_a", 1);
        runtime.GlobalVariableSet("BRT_b", 2);
        runtime.GlobalVariableSet("other", 3);

        Assert.Equal(2, runtime.GlobalVariablesDeleteAll("BRT_"));
        Assert.Equal(1, runtime.GlobalVariablesTotal());
        Assert.Equal("other", runtime.GlobalVariableName(0));
    }

    [Fact]
    public void DeleteAllWithNoArgumentsEmptiesTheSet()
    {
        Mql5Runtime runtime = Build();
        runtime.GlobalVariableSet("a", 1);
        runtime.GlobalVariableSet("b", 2);

        Assert.Equal(2, runtime.GlobalVariablesDeleteAll());
        Assert.Equal(0, runtime.GlobalVariablesTotal());
    }

    [Fact]
    public void DeleteAllKeepsVariablesTouchedAtOrAfterTheCutOff()
    {
        FakeMarketContext context = new();
        Mql5Runtime runtime = new(context);
        runtime.GlobalVariableSet("old", 1);

        context.TimeCurrent = context.TimeCurrent.AddHours(2);
        runtime.GlobalVariableSet("new", 2);

        long cutOff = Mql5Time.FromDateTime(context.TimeCurrent);

        Assert.Equal(1, runtime.GlobalVariablesDeleteAll(null, cutOff));
        Assert.Equal(1, runtime.GlobalVariablesTotal());
        Assert.Equal("new", runtime.GlobalVariableName(0));
    }

    // -------------------------------------------------- terminal properties ---

    [Theory]
    // Trading is permitted and the simulated broker is always attached.
    [InlineData(8, 1)]   // TERMINAL_TRADE_ALLOWED
    [InlineData(6, 1)]   // TERMINAL_CONNECTED
    // Every outward channel is refused, so "not permitted" is the truthful answer.
    [InlineData(7, 0)]   // TERMINAL_DLLS_ALLOWED
    [InlineData(9, 0)]   // TERMINAL_EMAIL_ENABLED
    [InlineData(10, 0)]  // TERMINAL_FTP_ENABLED
    [InlineData(26, 0)]  // TERMINAL_NOTIFICATIONS_ENABLED
    [InlineData(22, 0)]  // TERMINAL_MQID
    [InlineData(23, 0)]  // TERMINAL_COMMUNITY_ACCOUNT
    [InlineData(24, 0)]  // TERMINAL_COMMUNITY_CONNECTION
    [InlineData(38, 0)]  // TERMINAL_VPS
    [InlineData(19, 0)]  // TERMINAL_OPENCL_SUPPORT: 0 is MQL5's own "not supported"
    // Nothing is attached to a keyboard, so no key is down or toggled.
    [InlineData(1016, 0)] // TERMINAL_KEYSTATE_SHIFT
    [InlineData(1027, 0)] // TERMINAL_KEYSTATE_ESCAPE
    [InlineData(1145, 0)] // TERMINAL_KEYSTATE_SCRLOCK
    public void TerminalPropertiesTheEngineCanAnswerAreAnswered(int propertyId, long expected)
    {
        Mql5Runtime runtime = Build();

        Assert.Equal(expected, runtime.TerminalInfoInteger(propertyId));
    }

    [Theory]
    [InlineData(5)]    // TERMINAL_BUILD: experts gate features on it and there is no build
    [InlineData(11)]   // TERMINAL_MAXBARS
    [InlineData(12)]   // TERMINAL_CODEPAGE
    [InlineData(16)]   // TERMINAL_MEMORY_AVAILABLE
    [InlineData(18)]   // TERMINAL_X64
    [InlineData(20)]   // TERMINAL_DISK_SPACE
    [InlineData(21)]   // TERMINAL_CPU_CORES
    [InlineData(27)]   // TERMINAL_SCREEN_DPI
    [InlineData(28)]   // TERMINAL_PING_LAST
    [InlineData(114)]  // THEME_COLOR_BOOK_BUY
    [InlineData(-1)]   // and anything unmeasured
    public void TerminalPropertiesThatDescribeTheMachineStayRefused(int propertyId)
    {
        Mql5Runtime runtime = Build();

        Mql5UnsupportedOperationException failure =
            Assert.Throws<Mql5UnsupportedOperationException>(() => runtime.TerminalInfoInteger(propertyId));

        Assert.Equal(nameof(IMql5Runtime.TerminalInfoInteger), failure.FunctionName);
        Assert.Contains(propertyId.ToString(System.Globalization.CultureInfo.InvariantCulture), failure.Message, StringComparison.Ordinal);
    }

    // --------------------------------------------------- program properties ---

    [Fact]
    public void TheEngineReportsItselfAsATester()
    {
        // The single most consequential answer in this file. Sixty-nine corpus call
        // sites ask it, and a strategy told it is running live behaves differently -
        // one skips its trial-licence file read entirely when it knows it is a test.
        Mql5Runtime runtime = Build();

        Assert.Equal(1, runtime.MqlInfoInteger(6));
    }

    [Theory]
    [InlineData(6, 1)]   // MQL_TESTER: this engine has no other mode
    [InlineData(4, 1)]   // MQL_TRADE_ALLOWED: OrderSend reaches the simulated broker
    [InlineData(7, 0)]   // MQL_OPTIMIZATION: one pass, not a sweep
    [InlineData(16, 0)]  // MQL_FORWARD
    [InlineData(12, 0)]  // MQL_FRAME_MODE
    [InlineData(8, 0)]   // MQL_VISUAL_MODE: nothing is drawn
    [InlineData(3, 0)]   // MQL_DLLS_ALLOWED: no import surface exists
    [InlineData(14, 0)]  // MQL_SIGNALS_ALLOWED: no Signals subsystem
    [InlineData(5, 0)]   // MQL_DEBUG
    [InlineData(10, 0)]  // MQL_PROFILER
    [InlineData(18, 0)]  // MQL_STARTED_FROM_CONFIG
    public void ProgramPropertiesThatFollowFromWhatTheEngineIsAreAnswered(int propertyId, long expected)
    {
        Mql5Runtime runtime = Build();

        Assert.Equal(expected, runtime.MqlInfoInteger(propertyId));
    }

    [Theory]
    [InlineData(2)]   // MQL_PROGRAM_TYPE: expert or indicator is the host's to say
    [InlineData(9)]   // MQL_LICENSE_TYPE: nothing here was bought from the Market
    [InlineData(11)]  // MQL_MEMORY_USED
    [InlineData(13)]  // MQL_MEMORY_LIMIT
    [InlineData(17)]  // MQL_HANDLES_USED
    [InlineData(15)]  // MQL_CODEPAGE: undocumented
    [InlineData(19)]  // MQL_GLOBAL_COUNTER: undocumented
    [InlineData(999)]
    public void ProgramPropertiesTheEngineCannotKnowAreRefusedRatherThanZeroed(int propertyId)
    {
        // The point of the change: a silent 0 here is a false statement, and false in
        // the direction that makes a strategy take a branch rather than stop.
        Mql5Runtime runtime = Build();

        Mql5UnsupportedOperationException failure =
            Assert.Throws<Mql5UnsupportedOperationException>(() => runtime.MqlInfoInteger(propertyId));

        Assert.Equal(nameof(IMql5MarketContext.MqlInfoInteger), failure.FunctionName);
    }

    [Theory]
    [InlineData(0)]  // MQL_PROGRAM_NAME
    [InlineData(1)]  // MQL_PROGRAM_PATH
    [InlineData(7)]
    public void ProgramStringsAreRefusedRatherThanAnsweredWithTheFailureValue(int propertyId)
    {
        // The empty string is what MQL5 returns when MQLInfoString fails, so handing it
        // back as an answer says "this call failed" to code that checks, and hands a
        // silently wrong name to code that does not.
        Mql5Runtime runtime = Build();

        Mql5UnsupportedOperationException failure =
            Assert.Throws<Mql5UnsupportedOperationException>(() => runtime.MqlInfoString(propertyId));

        Assert.Equal(nameof(IMql5MarketContext.MqlInfoString), failure.FunctionName);
    }

    [Fact]
    public void AHostCanOwnThePropertiesItKnowsAndDelegateTheRest()
    {
        // The defaults live on a public static precisely so that an engine which knows
        // the program's name can answer that one property and hand the rest back,
        // instead of having to restate the whole table to override a single entry.
        static string HostAnswer(int propertyId)
            => propertyId == 0 ? "Quantum Queen X 4.3" : Mql5ProgramInfo.InfoString(propertyId);

        Assert.Equal("Quantum Queen X 4.3", HostAnswer(0));
        Assert.Throws<Mql5UnsupportedOperationException>(() => HostAnswer(1));
    }

    // ------------------------------------------------------ depth of market ---

    [Fact]
    public void TheDepthOfMarketReportsThatNoBookIsAvailable()
    {
        // False is MQL5's own answer for a symbol with no book, and every caller has to
        // handle it. Fabricating levels would let a strategy size itself against
        // liquidity that was never measured.
        Mql5Runtime runtime = Build();
        Mql5BookInfo[]? book = null;

        Assert.False(runtime.MarketBookGet("EURUSD", ref book));
        Assert.Equal(Mql5ErrorCodes.MarketNotSelected, runtime.LastError);
        Assert.Null(book);
    }

    [Fact]
    public void SubscribingToTheDepthOfMarketAlsoReportsFailure()
    {
        // The pair has to agree: a strategy told its subscription failed must not then
        // be told a book exists.
        Mql5Runtime runtime = Build();

        Assert.False(runtime.MarketBookAdd("EURUSD"));
    }

    // ------------------------------------------------------------- file I/O ---

    [Fact]
    public void FileAccessIsStillRefusedAndSaysWhy()
    {
        // Kept refused deliberately. A file outlives the run, so a strategy that reads
        // its own state back from one replays differently depending on what the last run
        // wrote - the corpus case is an expert stamping a trial start date and then
        // refusing to trade once the stamp is old enough.
        Mql5Runtime runtime = Build();

        Mql5UnsupportedOperationException failure =
            Assert.Throws<Mql5UnsupportedOperationException>(() => runtime.FileOpen("trial.dat", 0));

        Assert.Equal(nameof(IMql5Runtime.FileOpen), failure.FunctionName);
        Assert.Contains("outlives the run", failure.Message, StringComparison.Ordinal);
    }
}
