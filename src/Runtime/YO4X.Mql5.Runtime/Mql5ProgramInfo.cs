namespace YO4X.Mql5.Runtime;

/// <summary>
/// The default answers for MQL5's <c>MQLInfoInteger</c> and <c>MQLInfoString</c>: what
/// a running MQL5 program can ask about the circumstances it was started under.
///
/// <see cref="IMql5MarketContext"/> used to default the whole family to <c>0</c> and
/// the empty string. That was not a neutral placeholder, it was a set of false
/// statements, and false in the worst direction: <c>MQL_TESTER</c> answering 0 tells a
/// strategy it is running live on someone's money. Strategies act on that. One corpus
/// expert skips its trial-licence file check entirely under
/// <c>MQLInfoInteger(MQL_TESTER)</c> and only reached the refused <c>FileOpen</c>
/// because it had been told it was live; three more gate every tick behind
/// <c>MQL_TRADE_ALLOWED</c> and were silently returning early. Nothing failed - they
/// just quietly did the wrong thing, which is the outcome this engine exists to avoid.
///
/// So the properties that are true of this engine <b>by construction</b> are answered
/// truthfully, and the rest are refused by name. The dividing line is whether the
/// answer follows from what this engine is:
///
/// <list type="bullet">
/// <item><description><b>Answered.</b> This engine is a strategy tester and has no
/// other mode; it runs one pass, not an optimization sweep; it draws nothing; it
/// accepts orders; and every outward channel - DLL imports, the Signals service - is
/// refused elsewhere in this runtime, so "not permitted" is simply true.</description></item>
/// <item><description><b>Refused.</b> Memory, handles, licence type, program type,
/// program name and program path. Some are facts about the host process rather than
/// the engine; the rest are things only the host knows. Either way this type has no
/// truthful answer, and a plausible-looking one would be read as fact.</description></item>
/// </list>
///
/// A host that knows better overrides <see cref="IMql5MarketContext.MqlInfoInteger"/>
/// or <see cref="IMql5MarketContext.MqlInfoString"/> and delegates whatever it does not
/// want to own back to the methods here.
///
/// The property numbers are measured, not copied from the reference, which publishes
/// none of them: each was confirmed against the MetaEditor compiler by pairing the
/// named constant with the number in one <c>switch</c> and checking that it reports
/// "case value already used". Note that <c>ENUM_MQL_INFO_STRING</c> and
/// <c>ENUM_MQL_INFO_INTEGER</c> share one numbering space - the string properties take
/// 0 and 1, and the integer properties start at 2 - so the two functions never collide.
/// </summary>
public static class Mql5ProgramInfo
{
    // ENUM_MQL_INFO_STRING.
    private const int ProgramName = 0;
    private const int ProgramPath = 1;

    // ENUM_MQL_INFO_INTEGER.
    private const int ProgramType = 2;
    private const int DllsAllowed = 3;
    private const int TradeAllowed = 4;
    private const int Debug = 5;
    private const int Tester = 6;
    private const int Optimization = 7;
    private const int VisualMode = 8;
    private const int LicenseType = 9;
    private const int Profiler = 10;
    private const int MemoryUsed = 11;
    private const int FrameMode = 12;
    private const int MemoryLimit = 13;
    private const int SignalsAllowed = 14;
    private const int Codepage = 15;
    private const int Forward = 16;
    private const int HandlesUsed = 17;
    private const int StartedFromConfig = 18;
    private const int GlobalCounter = 19;

    /// <summary>
    /// The default <c>MQLInfoInteger</c>. Answers the properties that follow from what
    /// this engine is; throws <see cref="Mql5UnsupportedOperationException"/> for the
    /// rest, naming the property.
    /// </summary>
    public static long InfoInteger(int propertyId) => propertyId switch
    {
        // This engine is a strategy tester. It has no live mode, no terminal and no
        // broker connection - it replays recorded bars against a simulated book - so
        // this is not a convenient answer, it is the only true one.
        Tester => 1,

        // One pass over one set of bars. An optimization sweep, its forward half and
        // the frame-gathering mode an optimizing expert runs in are all things a host
        // would have to arrange deliberately; a host that arranges one overrides this.
        Optimization => 0,
        Forward => 0,
        FrameMode => 0,

        // Nothing is drawn. The Object and Chart families are recording stubs, so there
        // is no visual pass for a strategy to pace itself against.
        VisualMode => 0,

        // The engine accepts orders: OrderSend reaches the simulated broker.
        TradeAllowed => 1,

        // Refused elsewhere in this runtime, so "not permitted" is the honest answer
        // rather than a restriction being invented here. There is no DLL import surface
        // and no Signals subsystem.
        DllsAllowed => 0,
        SignalsAllowed => 0,

        // MetaEditor's debugger and profiler attach to a running terminal. Neither
        // exists here, and neither is something a host could switch on.
        Debug => 0,
        Profiler => 0,

        // Launched by a host calling into this library, never from the StartUp section
        // of a terminal configuration file.
        StartedFromConfig => 0,

        // Only the host knows whether it converted an expert or an indicator. The
        // generated contract is expert-shaped, but IMql5MarketContext also carries
        // SetIndexBuffer, so guessing PROGRAM_EXPERT here would misroute a program that
        // branches on the answer. No corpus file asks.
        ProgramType => throw Unsupported(
            nameof(IMql5MarketContext.MqlInfoInteger),
            "MQL_PROGRAM_TYPE says whether an expert, an indicator or a script is running, which only the host that loaded the strategy knows - override IMql5MarketContext.MqlInfoInteger to state it"),

        // ENUM_LICENSE_TYPE describes how an EX5 was bought from the MQL5 Market. No
        // module here was bought from anywhere, so no member of that enumeration is
        // true of it - including LICENSE_FREE, which still asserts a Market listing.
        LicenseType => throw Unsupported(
            nameof(IMql5MarketContext.MqlInfoInteger),
            "MQL_LICENSE_TYPE describes an MQL5 Market purchase, and a converted strategy was never bought from the Market, so no ENUM_LICENSE_TYPE member is true of it"),

        // Facts about the host process, not the engine. Answering would make a backtest
        // depend on the machine it ran on, which is the same objection that keeps
        // TERMINAL_MEMORY_* and its neighbours refused.
        MemoryUsed or MemoryLimit or HandlesUsed => throw Unsupported(
            nameof(IMql5MarketContext.MqlInfoInteger),
            "MQL_MEMORY_USED, MQL_MEMORY_LIMIT and MQL_HANDLES_USED measure the host process rather than the engine, so answering would make a backtest depend on the machine it ran on"),

        // Present in the compiler, absent from the published reference. A property with
        // no documented semantics has nothing to be faithful to.
        Codepage or GlobalCounter => throw Unsupported(
            nameof(IMql5MarketContext.MqlInfoInteger),
            "MQL_CODEPAGE and MQL_GLOBAL_COUNTER are accepted by the MQL5 compiler but carry no published meaning, and a property with no documented semantics cannot be answered faithfully"),

        _ => throw Unsupported(
            nameof(IMql5MarketContext.MqlInfoInteger),
            $"ENUM_MQL_INFO_INTEGER property {propertyId} is not one this engine can answer about itself; the run-mode and permission properties are answered, the rest describe the host process or the MQL5 Market")
    };

    /// <summary>
    /// The default <c>MQLInfoString</c>. Both published properties are host knowledge,
    /// so both throw <see cref="Mql5UnsupportedOperationException"/> rather than hand
    /// back the empty string, which MQL5 uses to mean failure and which a strategy will
    /// happily concatenate into a name it then relies on.
    /// </summary>
    public static string InfoString(int propertyId) => propertyId switch
    {
        ProgramName => throw Unsupported(
            nameof(IMql5MarketContext.MqlInfoString),
            "MQL_PROGRAM_NAME is the name the host gave the converted strategy, which this library never sees - override IMql5MarketContext.MqlInfoString to supply it"),

        // A path into the terminal's MQL5 folder. That folder is the same sandbox the
        // whole File family is refused over: it does not exist here, and a strategy
        // handed a path will try to open it.
        ProgramPath => throw Unsupported(
            nameof(IMql5MarketContext.MqlInfoString),
            "MQL_PROGRAM_PATH points into a terminal MQL5 folder that does not exist here, and handing back a path invites the file I/O this runtime refuses"),

        _ => throw Unsupported(
            nameof(IMql5MarketContext.MqlInfoString),
            $"ENUM_MQL_INFO_STRING property {propertyId} names something only the host that loaded the strategy knows")
    };

    private static Mql5UnsupportedOperationException Unsupported(string function, string reason)
        => Mql5UnsupportedOperationException.For(function, reason);
}
