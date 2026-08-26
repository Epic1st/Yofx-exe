using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;

namespace YO4X.Mt5.ConnectionProbe.Windows;

/// <summary>
/// MetaTrader 5's own tick-update mask, as the vendor encodes it on a history tick.
///
/// <para>
/// These are the <c>TICK_FLAG_*</c> values. They matter because they are the only way to tell
/// a genuine quote change from a tick that carries no new bid or ask, which is exactly the
/// distinction "every tick based on real ticks" turns on.
/// </para>
/// </summary>
[Flags]
public enum Mt5TickUpdate : ulong
{
    /// <summary>The vendor reported no flags for this tick.</summary>
    None = 0,

    /// <summary>The bid changed.</summary>
    Bid = 0x02,

    /// <summary>The ask changed.</summary>
    Ask = 0x04,

    /// <summary>The last trade price changed.</summary>
    Last = 0x08,

    /// <summary>The volume changed.</summary>
    Volume = 0x10,

    /// <summary>The tick was a buy.</summary>
    Buy = 0x20,

    /// <summary>The tick was a sell.</summary>
    Sell = 0x40,
}

/// <summary>One downloaded tick, copied out of the vendor type so nothing vendor-shaped escapes.</summary>
/// <param name="Time">Broker server time. Reported as received; never shifted here.</param>
/// <param name="Bid">The bid at this tick.</param>
/// <param name="Ask">The ask at this tick.</param>
/// <param name="Last">The last trade price, zero on a pure quote tick.</param>
/// <param name="Volume">The tick volume the server reported.</param>
/// <param name="Flags">Which fields this tick actually updated.</param>
public readonly record struct Mt5HistoryTick(
    DateTime Time,
    double Bid,
    double Ask,
    double Last,
    long Volume,
    Mt5TickUpdate Flags)
{
    /// <summary>True when this tick carried a new bid or ask, rather than only trade data.</summary>
    public bool IsQuote => (Flags & (Mt5TickUpdate.Bid | Mt5TickUpdate.Ask)) != 0;
}

/// <summary>
/// Downloads real tick history from a broker through the pinned MetaTrader 5 network API.
///
/// <para>
/// The vendor offers no blocking tick download. Its one blocking wrapper,
/// <c>GetTimesAndSalesHistory</c>, discards every tick whose last-trade price is zero, which
/// is every tick a foreign-exchange symbol has — so it returns nothing at all for EURUSD.
/// The only usable path is the raw request plus its event, and that path has three traps this
/// class exists to close: batches are delivered on pool threads and may arrive out of order,
/// handler exceptions are swallowed by the vendor, and there is no timeout of any kind.
/// </para>
/// </summary>
public sealed class Mt5NetApiTickHistoryClient : IDisposable
{
    /// <summary>
    /// The internal field the vendor stores the tick flag mask on. Its name is one character
    /// from the Unicode private-use area, U+E000, produced by the vendor's obfuscator — so the
    /// string literal below looks empty in every editor and is not. It is the reason the flags
    /// have to be read reflectively: the vendor exposes the same values publicly elsewhere but
    /// never on a history tick.
    /// </summary>
    private const string FlagsFieldName = "";

    private readonly Type apiType;
    private readonly object instance;
    private bool connected;

    private Mt5NetApiTickHistoryClient(Type apiType, object instance)
    {
        this.apiType = apiType;
        this.instance = instance;
    }

    /// <summary>Verifies the vendor artifact, loads it, and constructs a client for one account.</summary>
    /// <param name="artifactPath">Path to the pinned vendor assembly.</param>
    /// <param name="login">The account login.</param>
    /// <param name="password">The account password, used only for the vendor constructor.</param>
    /// <param name="host">The broker access-server host.</param>
    /// <param name="port">The broker access-server port.</param>
    public static Mt5NetApiTickHistoryClient Create(
        string artifactPath,
        ulong login,
        string password,
        string host,
        int port)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactPath);
        ArgumentNullException.ThrowIfNull(password);
        ArgumentException.ThrowIfNullOrWhiteSpace(host);

        using FileStream artifact = OpenVerifiedArtifact(Path.GetFullPath(artifactPath));
        Assembly assembly = AssemblyLoadContext.Default.LoadFromStream(artifact);
        Type apiType = assembly.GetType("mtapi.mt5.MT5API", throwOnError: true, ignoreCase: false)!;
        object instance = PinnedMt5NetApiConnectionClientFactory.CreateVendorClient(
            apiType,
            login,
            password,
            host,
            port,
            [],
            string.Empty);
        return new Mt5NetApiTickHistoryClient(apiType, instance);
    }

    /// <summary>Whether the vendor reports an established session.</summary>
    public bool Connected => apiType.GetProperty("Connected")?.GetValue(instance) is true;

    /// <summary>The broker's name, as the server reports it.</summary>
    public string? AccountCompanyName =>
        apiType.GetProperty("AccountCompanyName")?.GetValue(instance) as string;

    /// <summary>
    /// The broker's offset from UTC in minutes. Tick timestamps arrive in server time and the
    /// vendor documents no timezone for them, so this is reported rather than applied.
    /// </summary>
    public int? ServerTimeZoneInMinutes =>
        apiType.GetProperty("ServerTimeZoneInMinutes")?.GetValue(instance) as int?;

    /// <summary>Sets the vendor connect timeout, in milliseconds, before connecting.</summary>
    /// <param name="milliseconds">The timeout to apply.</param>
    public void SetConnectTimeout(int milliseconds)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(milliseconds, 0);
        apiType.GetField("ConnectTimeout")?.SetValue(instance, milliseconds);
    }

    /// <summary>Opens the session.</summary>
    public void Connect()
    {
        MethodInfo connect = apiType.GetMethod("Connect", Type.EmptyTypes)
            ?? throw new MissingMethodException(apiType.FullName, "Connect");
        connect.Invoke(instance, null);
        connected = true;
    }

    /// <summary>
    /// Downloads every tick the broker holds for one calendar day.
    ///
    /// <para>
    /// The vendor's request covers exactly one day despite its parameter names, and signals
    /// that it has finished by raising its event with an empty batch. Batches before that
    /// terminator can be delivered concurrently, so they are accumulated under a lock and
    /// sorted afterwards rather than trusted to arrive in order.
    /// </para>
    /// </summary>
    /// <param name="symbol">The instrument to download.</param>
    /// <param name="day">The calendar day, in broker server time.</param>
    /// <param name="timeout">How long to wait for the terminator before giving up.</param>
    public IReadOnlyList<Mt5HistoryTick> DownloadDay(string symbol, DateOnly day, TimeSpan timeout)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(timeout, TimeSpan.Zero);
        if (!connected)
        {
            throw new InvalidOperationException("Connect before downloading tick history.");
        }

        EventInfo tickHistory = apiType.GetEvent("OnTickHistory")
            ?? throw new MissingMemberException(apiType.FullName, "OnTickHistory");
        FieldInfo? flagsField = null;

        var ticks = new List<Mt5HistoryTick>();
        var gate = new Lock();
        using var finished = new ManualResetEventSlim(false);
        string? handlerFault = null;

        void OnBatch(object? sender, object args)
        {
            // The vendor swallows anything thrown from here, so a fault is recorded and
            // re-raised on the calling thread instead of vanishing into a log nobody reads.
            try
            {
                Type argsType = args.GetType();
                string? batchSymbol = argsType.GetField("Symbol")?.GetValue(args) as string;
                if (!string.Equals(batchSymbol, symbol, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                if (argsType.GetField("Bars")?.GetValue(args) is not IList bars || bars.Count == 0)
                {
                    finished.Set();
                    return;
                }

                foreach (object? bar in bars)
                {
                    if (bar is null)
                    {
                        continue;
                    }

                    flagsField ??= bar.GetType().GetField(
                        FlagsFieldName,
                        BindingFlags.NonPublic | BindingFlags.Instance);
                    Mt5HistoryTick tick = ReadTick(bar, flagsField);
                    lock (gate)
                    {
                        ticks.Add(tick);
                    }
                }
            }
            catch (Exception exception) when (exception is InvalidCastException
                or MissingFieldException
                or NullReferenceException
                or OverflowException)
            {
                handlerFault = exception.Message;
                finished.Set();
            }
        }

        Delegate handler = BuildHandler(tickHistory.EventHandlerType!, OnBatch);
        tickHistory.AddEventHandler(instance, handler);
        try
        {
            Invoke("TickHistoryRequest", [symbol, day.Year, day.Month, day.Day]);
            if (!finished.Wait(timeout))
            {
                // The per-symbol slot is only freed by the terminator. Left in place, the next
                // request for this symbol is rejected outright, so it is released explicitly.
                TryStop(symbol);
                throw new TimeoutException(
                    $"The broker did not finish sending {symbol} ticks for "
                    + day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                    + $" within {timeout.TotalSeconds:F0} seconds.");
            }

            if (handlerFault is { Length: > 0 })
            {
                TryStop(symbol);
                throw new InvalidDataException(
                    "The broker's tick batch could not be read: " + handlerFault);
            }
        }
        finally
        {
            tickHistory.RemoveEventHandler(instance, handler);
        }

        lock (gate)
        {
            // Equal timestamps are ordinary in tick data, so ordering is stabilised rather
            // than deduplicated: two ticks in the same millisecond are two real ticks.
            ticks.Sort(static (left, right) => left.Time.CompareTo(right.Time));
            return Validate(ticks, symbol, day);
        }
    }

    private static List<Mt5HistoryTick> Validate(
        List<Mt5HistoryTick> ticks,
        string symbol,
        DateOnly day)
    {
        foreach (Mt5HistoryTick tick in ticks)
        {
            if (tick.Bid <= 0 || tick.Ask <= 0 || tick.Ask < tick.Bid)
            {
                throw new InvalidDataException(
                    $"The broker returned an incoherent {symbol} tick at "
                    + tick.Time.ToString("O", CultureInfo.InvariantCulture)
                    + $" on {day:yyyy-MM-dd}: bid {tick.Bid}, ask {tick.Ask}.");
            }
        }

        return ticks;
    }

    private static Mt5HistoryTick ReadTick(object bar, FieldInfo? flagsField)
    {
        Type type = bar.GetType();
        ulong mask = flagsField?.GetValue(bar) is ulong raw ? raw : 0UL;
        return new Mt5HistoryTick(
            Field<DateTime>(type, bar, "Time"),
            Field<double>(type, bar, "Bid"),
            Field<double>(type, bar, "Ask"),
            Field<double>(type, bar, "Last"),
            checked((long)Field<ulong>(type, bar, "Volume")),
            (Mt5TickUpdate)mask);
    }

    /// <summary>
    /// Builds a delegate of the vendor's own event type over a plain callback. The event is
    /// typed as a vendor delegate this assembly cannot name, so it is created by reflection.
    /// </summary>
    private static Delegate BuildHandler(Type handlerType, Action<object?, object> callback)
    {
        MethodInfo invoke = handlerType.GetMethod("Invoke")
            ?? throw new MissingMethodException(handlerType.FullName, "Invoke");
        ParameterInfo[] parameters = invoke.GetParameters();
        if (parameters.Length != 2)
        {
            throw new MissingMethodException(handlerType.FullName, "Invoke(sender, args)");
        }

        MethodInfo shim = typeof(Mt5NetApiTickHistoryClient)
            .GetMethod(nameof(Dispatch), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(parameters[0].ParameterType, parameters[1].ParameterType);
        return Delegate.CreateDelegate(handlerType, callback, shim);
    }

    private static void Dispatch<TSender, TArgs>(
        Action<object?, object> callback,
        TSender sender,
        TArgs args)
    {
        if (args is not null)
        {
            callback(sender, args);
        }
    }

    private void TryStop(string symbol)
    {
        try
        {
            Invoke("TickHistoryStop", [symbol]);
        }
        catch (TargetInvocationException)
        {
            // Releasing the slot is best effort; the caller is already handling a failure.
        }
        catch (MissingMethodException)
        {
        }
    }

    private static T Field<T>(Type type, object instance, string name)
    {
        FieldInfo field = type.GetField(name)
            ?? throw new MissingFieldException(type.FullName, name);
        return (T)field.GetValue(instance)!;
    }

    private void Invoke(string name, object?[] arguments)
    {
        Type[] signature = new Type[arguments.Length];
        for (int index = 0; index < arguments.Length; index++)
        {
            signature[index] = arguments[index]?.GetType() ?? typeof(object);
        }

        MethodInfo method = apiType.GetMethod(name, signature)
            ?? throw new MissingMethodException(apiType.FullName, name);
        method.Invoke(instance, arguments);
    }

    private static FileStream OpenVerifiedArtifact(string artifactPath)
    {
        var stream = new FileStream(
            artifactPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            FileOptions.SequentialScan);
        try
        {
            string actual = Convert.ToHexString(SHA256.HashData(stream));
            if (!string.Equals(
                actual,
                PinnedMt5NetApiConnectionClientFactory.ApprovedArtifactSha256,
                StringComparison.Ordinal))
            {
                throw new InvalidDataException("The MT5 vendor artifact does not match the approved SHA-256.");
            }

            stream.Position = 0;
            return stream;
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    /// <summary>Closes the session if one was opened.</summary>
    public void Dispose()
    {
        if (!connected)
        {
            return;
        }

        try
        {
            Invoke("Disconnect", []);
        }
        catch (TargetInvocationException)
        {
            // Nothing to clean up on this side, and throwing from Dispose would mask whatever
            // the caller was already handling.
        }
        catch (MissingMethodException)
        {
        }
        finally
        {
            connected = false;
        }
    }
}
