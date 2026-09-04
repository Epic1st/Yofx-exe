using System.Text;

namespace YO4X.Mql5.Runtime;

/// <summary>
/// MQL5 common functions: diagnostics, timing, error state and the terminal I/O
/// surface this runtime refuses.
///
/// <c>Print</c> and <c>PrintFormat</c> are <b>Native</b>: they are honoured, but they
/// go to an injected sink. The runtime touches no console, no file and no socket
/// anywhere, because a converted strategy is derived from untrusted third-party
/// source and the only safe assumption is that it will try.
///
/// The tick counters are deliberately not wall-clock readings. MQL5 code uses them to
/// throttle - "do not resend within 2000 ms" - and a wall clock makes that behave
/// differently on every replay while a bar-time-derived counter advances with the
/// simulation and reproduces exactly. They report simulated milliseconds since the
/// runtime was constructed.
///
/// <c>Sleep</c> is refused rather than treated as a no-op. Dropping it silently would
/// change the meaning of every retry loop that uses it, and a backtest has no clock to
/// sleep against; the conversion has to restructure those loops instead.
///
/// <c>TerminalInfoInteger</c> is split rather than refused whole. The enumeration mixes
/// two unrelated kinds of question: "am I allowed to trade / send mail / load a DLL",
/// which is a question about the environment the strategy is running in and which this
/// engine can answer about itself; and "how much memory, which build, how many CPU
/// cores", which is a question about a MetaTrader installation and the machine under
/// it. The first kind is answered truthfully, the second refused. Refusing both was
/// broader than the reason for refusing either.
/// </summary>
public partial interface IMql5Runtime
{
    /// <summary>
    /// MQL5 <c>Print</c>. Concatenates its arguments with no separator, rendering
    /// doubles with up to 16 significant digits in whichever form is more compact, and
    /// writes the result to the log sink. Native.
    /// </summary>
    void Print(params object?[]? arguments);

    /// <summary>MQL5 <c>PrintFormat</c>. printf grammar, routed to the log sink. Native.</summary>
    void PrintFormat(string? format, params object?[]? arguments);

    /// <summary>
    /// MQL5 <c>Comment</c>. Paints the chart comment area, so it is visual only:
    /// recorded on the log sink and otherwise inert. ChartStub.
    /// </summary>
    void Comment(params object?[]? arguments);

    /// <summary>
    /// MQL5 <c>Alert</c>. Opens a terminal dialog, so it is visual only: recorded on
    /// the log sink and otherwise inert. ChartStub.
    /// </summary>
    void Alert(params object?[]? arguments);

    /// <summary>MQL5 <c>PlaySound</c>. Visual and audible only; recorded and inert. ChartStub.</summary>
    bool PlaySound(string? filename);

    /// <summary>
    /// MQL5 <c>GetTickCount</c>. Milliseconds of simulated time since the runtime was
    /// constructed, not wall-clock milliseconds. Native.
    /// </summary>
    uint GetTickCount();

    /// <summary>MQL5 <c>GetTickCount64</c>. See <see cref="GetTickCount"/>. Native.</summary>
    ulong GetTickCount64();

    /// <summary>MQL5 <c>GetMicrosecondCount</c>. See <see cref="GetTickCount"/>. Native.</summary>
    ulong GetMicrosecondCount();

    /// <summary>MQL5 <c>ZeroMemory</c> over a scalar. Native.</summary>
    void ZeroMemory<T>(ref T variable);

    /// <summary>MQL5 <c>ZeroMemory</c> over an array. Native.</summary>
    void ZeroMemoryArray<T>(T[]? array);

    /// <summary>
    /// MQL5 <c>GetLastError</c>. Reads the code the last failing built-in left behind,
    /// which is how a supported built-in reports failure without throwing. EngineBound.
    /// </summary>
    int GetLastError();

    /// <summary>MQL5 <c>ResetLastError</c>. EngineBound.</summary>
    void ResetLastError();

    /// <summary>MQL5 <c>IsStopped</c>. EngineBound.</summary>
    bool IsStopped();

    /// <summary>MQL5 <c>UninitializeReason</c>. EngineBound.</summary>
    int UninitializeReason();

    /// <summary>
    /// MQL5 <c>MQLInfoInteger</c>. EngineBound. The engine answers; the default context
    /// answers the run-mode and permission properties truthfully and refuses the rest.
    /// </summary>
    long MqlInfoInteger(int propertyId);

    /// <summary>MQL5 <c>MQLInfoString</c>. EngineBound; refused unless the engine supplies it.</summary>
    string MqlInfoString(int propertyId);

    /// <summary>MQL5 <c>TesterStatistics</c>. EngineBound.</summary>
    double TesterStatistics(int statisticId);

    /// <summary>MQL5 <c>TesterHideIndicators</c>. Visual only. ChartStub.</summary>
    void TesterHideIndicators(bool hide);

    /// <summary>MQL5 <c>TesterStop</c>. EngineBound.</summary>
    void TesterStop();

    /// <summary>MQL5 <c>TesterWithdrawal</c>. EngineBound.</summary>
    bool TesterWithdrawal(double money);

    /// <summary>MQL5 <c>ExpertRemove</c>. EngineBound.</summary>
    void ExpertRemove();

    /// <summary>
    /// MQL5 <c>Sleep</c>. Unsupported: a backtest has no clock to sleep against, and
    /// treating it as a no-op would change the meaning of the retry loops that use it.
    /// Throws <see cref="Mql5UnsupportedOperationException"/>.
    /// </summary>
    void Sleep(int milliseconds);

    /// <summary>MQL5 <c>MessageBox</c>. Unsupported: there is no operator to answer a modal dialog.</summary>
    int MessageBox(string? text, string? caption = null, int flags = 0);

    /// <summary>
    /// MQL5 <c>TerminalInfoInteger</c>. <b>Native</b> for the properties that are
    /// questions about the execution environment - trade permission, connection, the
    /// DLL / e-mail / FTP / notification permissions, OpenCL, keyboard state - which
    /// this engine can answer truthfully about itself. Every other property describes a
    /// real MetaTrader installation or the machine under it and stays Unsupported.
    /// </summary>
    long TerminalInfoInteger(int propertyId);

    /// <summary>MQL5 <c>TerminalInfoDouble</c>. Unsupported: no terminal state exists here.</summary>
    double TerminalInfoDouble(int propertyId);

    /// <summary>MQL5 <c>TerminalInfoString</c>. Unsupported: no terminal paths exist here.</summary>
    string TerminalInfoString(int propertyId);

    /// <summary>MQL5 <c>TerminalClose</c>. Unsupported.</summary>
    bool TerminalClose(int returnCode);

    /// <summary>MQL5 <c>WebRequest</c>. Unsupported: this library performs no network access.</summary>
    int WebRequest(string? method, string? url, string? headers, int timeout, byte[]? data, ref byte[]? result, ref string resultHeaders);

    /// <summary>MQL5 <c>WebRequest</c> over signed <c>char</c> buffers. Unsupported, as above.</summary>
    int WebRequest(string? method, string? url, string? headers, int timeout, sbyte[]? data, ref sbyte[]? result, ref string resultHeaders);

    /// <summary>MQL5 <c>SendMail</c>. Unsupported: reaches outside the process.</summary>
    bool SendMail(string? subject, string? text);

    /// <summary>MQL5 <c>SendNotification</c>. Unsupported: reaches outside the process.</summary>
    bool SendNotification(string? text);

    /// <summary>MQL5 <c>SendFTP</c>. Unsupported: reaches outside the process.</summary>
    bool SendFtp(string? filename, string? ftpPath = null);

    /// <summary>MQL5 <c>ResourceCreate</c>. Unsupported: reads a file from the terminal sandbox.</summary>
    bool ResourceCreate(string? resourceName, string? path);

    /// <summary>MQL5 <c>ResourceCreate</c> dynamic pixel buffer.</summary>
    bool ResourceCreate(string? resourceName, uint[]? data, uint width, uint height, uint dataXOffset, uint dataYOffset, uint dataWidth, uint colorFormat);

    /// <summary>MQL5 <c>ResourceReadImage</c>. Unsupported.</summary>
    bool ResourceReadImage(string? resourceName, ref uint[]? data, ref uint width, ref uint height);

    /// <summary>MQL5 <c>ResourceFree</c>. Unsupported.</summary>
    bool ResourceFree(string? resourceName);

    /// <summary>MQL5 <c>ResourceSave</c>. Unsupported: writes a file.</summary>
    bool ResourceSave(string? resourceName, string? fileName);

    /// <summary>MQL5 <c>DebugBreak</c>. Unsupported: meaningless outside MetaEditor.</summary>
    void DebugBreak();

    /// <summary>MQL5 <c>TranslateKey</c>. Unsupported: reads the operating system keyboard layout.</summary>
    short TranslateKey(int keyCode);

    /// <summary>MQL5 <c>CryptEncode</c>. Unsupported.</summary>
    int CryptEncode(int method, byte[]? data, byte[]? key, ref byte[]? result);

    /// <summary>MQL5 <c>CryptDecode</c>. Unsupported.</summary>
    int CryptDecode(int method, byte[]? data, byte[]? key, ref byte[]? result);
}

public sealed partial class Mql5Runtime
{
    // The ENUM_TERMINAL_INFO_INTEGER members this runtime answers. MetaQuotes publishes
    // no numbers for the enumeration, so these are not copied out of the reference:
    // they are the measured values carried in the governance constant table, each one
    // re-confirmed against the MetaEditor compiler by pairing the named constant with
    // the number in a single switch and checking that it reports "case value already
    // used". A guessed ordinal would not fail - it would silently read a different
    // property - which is why they are pinned this way and nowhere else.
    private const int TerminalConnected = 6;
    private const int TerminalDllsAllowed = 7;
    private const int TerminalTradeAllowed = 8;
    private const int TerminalEmailEnabled = 9;
    private const int TerminalFtpEnabled = 10;
    private const int TerminalOpenClSupport = 19;
    private const int TerminalMqid = 22;
    private const int TerminalCommunityAccount = 23;
    private const int TerminalCommunityConnection = 24;
    private const int TerminalNotificationsEnabled = 26;
    private const int TerminalVps = 38;

    // The keyboard-state block is 1000 plus the Windows virtual key code.
    private const int TerminalKeystateTab = 1009;
    private const int TerminalKeystateEnter = 1013;
    private const int TerminalKeystateShift = 1016;
    private const int TerminalKeystateControl = 1017;
    private const int TerminalKeystateMenu = 1018;
    private const int TerminalKeystateCapslock = 1020;
    private const int TerminalKeystateEscape = 1027;
    private const int TerminalKeystatePageUp = 1033;
    private const int TerminalKeystatePageDown = 1034;
    private const int TerminalKeystateEnd = 1035;
    private const int TerminalKeystateHome = 1036;
    private const int TerminalKeystateLeft = 1037;
    private const int TerminalKeystateUp = 1038;
    private const int TerminalKeystateRight = 1039;
    private const int TerminalKeystateDown = 1040;
    private const int TerminalKeystateInsert = 1045;
    private const int TerminalKeystateDelete = 1046;
    private const int TerminalKeystateNumlock = 1144;
    private const int TerminalKeystateScrlock = 1145;

    /// <inheritdoc />
    public void Print(params object?[]? arguments) => Emit(Mql5LogChannel.Print, Join(arguments));

    /// <inheritdoc />
    public void PrintFormat(string? format, params object?[]? arguments)
        => Emit(Mql5LogChannel.Print, Mql5Format.Format(format, arguments));

    /// <inheritdoc />
    public void Comment(params object?[]? arguments) => Emit(Mql5LogChannel.Comment, Join(arguments));

    /// <inheritdoc />
    public void Alert(params object?[]? arguments) => Emit(Mql5LogChannel.Alert, Join(arguments));

    /// <inheritdoc />
    public bool PlaySound(string? filename)
    {
        Emit(Mql5LogChannel.Sound, filename ?? string.Empty);
        return true;
    }

    /// <inheritdoc />
    public uint GetTickCount() => (uint)(ElapsedMilliseconds() & 0xFFFFFFFF);

    /// <inheritdoc />
    public ulong GetTickCount64() => (ulong)ElapsedMilliseconds();

    /// <inheritdoc />
    public ulong GetMicrosecondCount() => (ulong)ElapsedMilliseconds() * 1000UL;

    /// <inheritdoc />
    public void ZeroMemory<T>(ref T variable)
    {
        if (typeof(T).IsValueType || typeof(T) == typeof(string))
        {
            variable = default!;
            return;
        }

        // An MQL5 structure is a value type whose fields ZeroMemory sets to zero; the variable
        // itself stays usable, and the very next line is normally a field assignment. Structures
        // arrive here as CLR classes, where `default` is null — so the literal reading would hand
        // back a null that the caller dereferences immediately. A fresh instance is what the MQL5
        // semantics actually describe, because every field of a newly constructed one is zero.
        variable = Mql5ZeroedInstance<T>.Create();
    }

    /// <inheritdoc />
    public void ZeroMemoryArray<T>(T[]? array)
    {
        if (array is not null)
        {
            Array.Clear(array);
        }
    }

    /// <inheritdoc />
    public int GetLastError() => LastError;

    /// <inheritdoc />
    public void ResetLastError() => LastError = Mql5ErrorCodes.Success;

    /// <inheritdoc />
    public bool IsStopped() => context.IsStopped();

    /// <inheritdoc />
    public int UninitializeReason() => context.UninitializeReason();

    /// <inheritdoc />
    public long MqlInfoInteger(int propertyId) => context.MqlInfoInteger(propertyId);

    /// <inheritdoc />
    public string MqlInfoString(int propertyId) => context.MqlInfoString(propertyId);

    /// <inheritdoc />
    public double TesterStatistics(int statisticId) => context.TesterStatistics(statisticId);

    /// <inheritdoc />
    public void TesterHideIndicators(bool hide) => RecordChartCall(nameof(TesterHideIndicators));

    /// <inheritdoc />
    public void TesterStop() => context.TesterStop();

    /// <inheritdoc />
    public bool TesterWithdrawal(double money) => context.TesterWithdrawal(money);

    /// <inheritdoc />
    public void ExpertRemove() => context.ExpertRemove();

    /// <inheritdoc />
    public void Sleep(int milliseconds)
    {
        if (context is IMql5DelayContext delayContext)
        {
            delayContext.Delay(milliseconds);
            return;
        }

        throw Refuse(nameof(Sleep), "a backtest has no clock to sleep against, and dropping the call silently would change the meaning of the retry loop around it");
    }

    /// <inheritdoc />
    public int MessageBox(string? text, string? caption = null, int flags = 0)
        => throw Refuse(nameof(MessageBox), "it blocks on a modal dialog and there is no operator to answer it");

    /// <inheritdoc />
    public long TerminalInfoInteger(int propertyId) => propertyId switch
    {
        // Permissions and connectivity. These are questions about the environment the
        // strategy is running in, and this engine knows the answers about itself: it
        // accepts orders, it is always attached to its simulated broker, and every
        // outward channel - DLL imports, SendMail, SendFtp, SendNotification - is
        // refused, so the honest answer to each of those is "not permitted". Answering
        // truthfully is not fabrication; it is the same question MetaTrader is asked,
        // put to a different environment.
        TerminalTradeAllowed => 1,
        TerminalConnected => 1,
        TerminalDllsAllowed => 0,
        TerminalEmailEnabled => 0,
        TerminalFtpEnabled => 0,
        TerminalNotificationsEnabled => 0,

        // No MetaQuotes identity of any kind exists behind this engine, so the three
        // account flags and the virtual-hosting flag are all genuinely absent.
        TerminalMqid => 0,
        TerminalCommunityAccount => 0,
        TerminalCommunityConnection => 0,
        TerminalVps => 0,

        // MetaQuotes documents this one as "the version of the supported OpenCL in the
        // format of 0x00010002 = 1.2. '0' means that OpenCL is not supported", so 0 is
        // the published way to say what is true here.
        TerminalOpenClSupport => 0,

        // Nothing is attached to a keyboard. MQL5 hands back a GetKeyState word whose
        // high bit means "down" and low bit means "toggled"; 0 is "up and untoggled",
        // which is the state of every key on a headless run, on every run.
        TerminalKeystateTab or TerminalKeystateEnter or TerminalKeystateShift
            or TerminalKeystateControl or TerminalKeystateMenu or TerminalKeystateCapslock
            or TerminalKeystateEscape or TerminalKeystatePageUp or TerminalKeystatePageDown
            or TerminalKeystateEnd or TerminalKeystateHome or TerminalKeystateLeft
            or TerminalKeystateUp or TerminalKeystateRight or TerminalKeystateDown
            or TerminalKeystateInsert or TerminalKeystateDelete or TerminalKeystateNumlock
            or TerminalKeystateScrlock => 0,

        // Everything else. TERMINAL_BUILD is the one worth naming: experts routinely
        // gate features on it, so any number returned here silently selects a code path
        // on the strength of a terminal that does not exist. The memory, disk, CPU,
        // screen-geometry, window-position, ping and colour-theme properties are worse
        // still - they describe the machine, so answering would make a backtest depend
        // on what it was run on.
        _ => throw Refuse(
            nameof(TerminalInfoInteger),
            $"ENUM_TERMINAL_INFO_INTEGER property {propertyId} describes a real MetaTrader installation or the machine under it - build number, memory, disk, CPU, screen geometry, ping - and this engine has no truthful answer; the properties it can answer about itself (trade permission, connection, DLL/e-mail/FTP/notification permissions, OpenCL, keyboard state) are answered")
    };

    /// <inheritdoc />
    public double TerminalInfoDouble(int propertyId)
        => throw Refuse(nameof(TerminalInfoDouble), "it reports terminal state that has no counterpart in the engine");

    /// <inheritdoc />
    public string TerminalInfoString(int propertyId)
        => throw Refuse(nameof(TerminalInfoString), "it reports terminal paths and state that have no counterpart in the engine");

    /// <inheritdoc />
    public bool TerminalClose(int returnCode)
        => throw Refuse(nameof(TerminalClose), "it shuts the terminal down");

    /// <inheritdoc />
    public int WebRequest(string? method, string? url, string? headers, int timeout, byte[]? data, ref byte[]? result, ref string resultHeaders)
        => throw Refuse(nameof(WebRequest), "this library performs no network access");

    /// <inheritdoc />
    /// <remarks>MQL5 declares this one with signed <c>char</c> buffers and the conversion pair with
    /// unsigned ones, then accepts either at either. Both spellings are carried so a program that
    /// MQL5 compiles is refused here for the reason that actually applies — no network access —
    /// rather than for a buffer type mismatch that MQL5 does not make.</remarks>
    public int WebRequest(string? method, string? url, string? headers, int timeout, sbyte[]? data, ref sbyte[]? result, ref string resultHeaders)
        => throw Refuse(nameof(WebRequest), "this library performs no network access");

    /// <inheritdoc />
    public bool SendMail(string? subject, string? text)
        => throw Refuse(nameof(SendMail), "it reaches outside the process");

    /// <inheritdoc />
    public bool SendNotification(string? text)
        => throw Refuse(nameof(SendNotification), "it reaches outside the process");

    /// <inheritdoc />
    public bool SendFtp(string? filename, string? ftpPath = null)
        => throw Refuse(nameof(SendFtp), "it reaches outside the process");

    /// <inheritdoc />
    public bool ResourceCreate(string? resourceName, string? path)
        => throw Refuse(nameof(ResourceCreate), "it reads a file from the terminal sandbox");

    /// <inheritdoc />
    public bool ResourceCreate(string? resourceName, uint[]? data, uint width, uint height, uint dataXOffset, uint dataYOffset, uint dataWidth, uint colorFormat)
        => true;

    /// <inheritdoc />
    public bool ResourceReadImage(string? resourceName, ref uint[]? data, ref uint width, ref uint height)
    {
        // A missing optional bitmap is an ordinary false result in MQL5. Strategies commonly
        // use that result to fall back to text-only controls, so treating it as a runtime fault
        // would let cosmetic terminal UI prevent otherwise valid trading logic from starting.
        data = [];
        width = 0;
        height = 0;
        return false;
    }

    /// <inheritdoc />
    public bool ResourceFree(string? resourceName)
        => throw Refuse(nameof(ResourceFree), "there is no resource store outside the terminal");

    /// <inheritdoc />
    public bool ResourceSave(string? resourceName, string? fileName)
        => throw Refuse(nameof(ResourceSave), "it writes a file");

    /// <inheritdoc />
    public void DebugBreak()
        => throw Refuse(nameof(DebugBreak), "a debugger breakpoint is meaningless outside MetaEditor");

    /// <inheritdoc />
    public short TranslateKey(int keyCode)
        => throw Refuse(nameof(TranslateKey), "it reads the operating system keyboard layout");

    /// <inheritdoc />
    public int CryptEncode(int method, byte[]? data, byte[]? key, ref byte[]? result)
        => throw Refuse(nameof(CryptEncode), "cryptographic primitives are outside this runtime's remit");

    /// <inheritdoc />
    public int CryptDecode(int method, byte[]? data, byte[]? key, ref byte[]? result)
        => throw Refuse(nameof(CryptDecode), "cryptographic primitives are outside this runtime's remit");

    private static string Join(object?[]? arguments)
    {
        if (arguments is null || arguments.Length == 0)
        {
            return string.Empty;
        }

        if (arguments.Length == 1)
        {
            return Mql5Format.Describe(arguments[0]);
        }

        StringBuilder builder = new();
        foreach (object? argument in arguments)
        {
            builder.Append(Mql5Format.Describe(argument));
        }

        return builder.ToString();
    }

    private long ElapsedMilliseconds()
    {
        long now = Mql5Time.FromDateTime(context.TimeCurrent);
        clockBaseline ??= now;
        long elapsed = now - clockBaseline.Value;
        long marketMilliseconds = elapsed < 0 ? 0 : checked(elapsed * 1000);
        long virtualDelay = context is IMql5DelayContext delayContext
            ? delayContext.VirtualDelayMilliseconds
            : 0;
        return checked(marketMilliseconds + Math.Max(0, virtualDelay));
    }
}
