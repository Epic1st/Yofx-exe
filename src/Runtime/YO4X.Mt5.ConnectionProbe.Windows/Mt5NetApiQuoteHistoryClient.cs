using System.Globalization;
using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;

namespace YO4X.Mt5.ConnectionProbe.Windows;

/// <summary>One downloaded bar, copied out of the vendor type so nothing vendor-shaped escapes.</summary>
public readonly record struct Mt5HistoryBar(
    DateTime Time,
    double Open,
    double High,
    double Low,
    double Close,
    long TickVolume,
    int Spread);

/// <summary>
/// The chart periods the vendor accepts, expressed the way it expresses them: in whole
/// minutes. The vendor exposes no chart-period enumeration of its own — the only period
/// type it ships, <c>EquityTimeframe</c>, is minute-valued (H1 = 60, H4 = 240, D1 = 1440),
/// so minutes is the vendor's own unit rather than an assumption imposed here. The value
/// is nonetheless checked against the returned bar spacing after a download, because a
/// misread unit would otherwise be indistinguishable from a broker with sparse history.
/// </summary>
public enum Mt5HistoryPeriod
{
    M1 = 1,
    M5 = 5,
    M15 = 15,
    M30 = 30,
    H1 = 60,
    H4 = 240,
    D1 = 1440,
    W1 = 10080,
}

/// <summary>
/// Downloads historical bars from a broker through the pinned MetaTrader 5 network API,
/// without a MetaTrader terminal anywhere in the path.
///
/// <para>
/// The vendor bytes are hash-verified before <see cref="AssemblyLoadContext"/> ever sees
/// them, and the client is built through the same construction helper the connection probe
/// uses, so there is exactly one place where an unpinned assembly could be admitted and it
/// is already guarded.
/// </para>
/// </summary>
public sealed class Mt5NetApiQuoteHistoryClient : IDisposable
{
    private readonly Type apiType;
    private readonly object instance;
    private bool connected;

    private Mt5NetApiQuoteHistoryClient(Type apiType, object instance)
    {
        this.apiType = apiType;
        this.instance = instance;
    }

    /// <summary>
    /// Verifies the vendor artifact, loads it, and constructs a client for one account.
    /// The password is handed straight to the vendor constructor and is not retained here.
    /// </summary>
    public static Mt5NetApiQuoteHistoryClient Create(
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
        return new Mt5NetApiQuoteHistoryClient(apiType, instance);
    }

    public bool Connected =>
        apiType.GetProperty("Connected")?.GetValue(instance) is true;

    public string? AccountCompanyName =>
        apiType.GetProperty("AccountCompanyName")?.GetValue(instance) as string;

    /// <summary>
    /// The broker's own offset from UTC, in minutes, as the server reports it.
    ///
    /// <para>
    /// Bar timestamps arrive in the broker's server time and the vendor documents no timezone
    /// for them, so this is the only handle on what they mean. It is reported rather than
    /// applied: silently shifting the series would bake an inferred offset into a file that
    /// later looks like measured data.
    /// </para>
    /// </summary>
    public int? ServerTimeZoneInMinutes =>
        apiType.GetProperty("ServerTimeZoneInMinutes")?.GetValue(instance) as int?;

    /// <summary>Sets the vendor connect timeout, in milliseconds, before connecting.</summary>
    public void SetConnectTimeout(int milliseconds)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(milliseconds, 0);
        apiType.GetField("ConnectTimeout")?.SetValue(instance, milliseconds);
    }

    public void Connect()
    {
        Invoke("Connect", []);
        connected = true;
    }

    /// <summary>
    /// Downloads every bar the broker will serve for the closed interval, then verifies the
    /// result before returning it: bars must be ordered, in range, and priced coherently
    /// (high is the highest, low the lowest, all strictly positive). A broker that serves a
    /// malformed series fails here rather than silently becoming a backtest input.
    /// </summary>
    public IReadOnlyList<Mt5HistoryBar> Download(
        string symbol,
        DateTime fromUtc,
        DateTime toUtc,
        Mt5HistoryPeriod period)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
        if (toUtc < fromUtc)
        {
            throw new ArgumentException("The end of the interval precedes its start.", nameof(toUtc));
        }

        if (!connected)
        {
            throw new InvalidOperationException("Connect before downloading history.");
        }

        MethodInfo download = apiType.GetMethod(
            "DownloadQuoteHistory",
            [typeof(string), typeof(DateTime), typeof(DateTime), typeof(int)])
            ?? throw new MissingMethodException(
                apiType.FullName,
                "DownloadQuoteHistory(string,DateTime,DateTime,int)");

        object? raw = download.Invoke(instance, [symbol, fromUtc, toUtc, (int)period]);
        if (raw is not Array array)
        {
            return [];
        }

        var bars = new List<Mt5HistoryBar>(array.Length);
        DateTime previous = DateTime.MinValue;
        foreach (object? item in array)
        {
            if (item is null)
            {
                continue;
            }

            Mt5HistoryBar bar = ReadBar(item);
            if (bar.Time <= previous
                || bar.Open <= 0
                || bar.High <= 0
                || bar.Low <= 0
                || bar.Close <= 0
                || bar.High < bar.Low
                || bar.High < Math.Max(bar.Open, bar.Close)
                || bar.Low > Math.Min(bar.Open, bar.Close))
            {
                throw new InvalidDataException(
                    "The broker returned a bar that is out of order or not internally coherent at "
                    + bar.Time.ToString("O", CultureInfo.InvariantCulture)
                    + ".");
            }

            previous = bar.Time;
            bars.Add(bar);
        }

        return bars;
    }

    private static Mt5HistoryBar ReadBar(object bar)
    {
        Type type = bar.GetType();
        return new Mt5HistoryBar(
            Field<DateTime>(type, bar, "Time"),
            Field<double>(type, bar, "OpenPrice"),
            Field<double>(type, bar, "HighPrice"),
            Field<double>(type, bar, "LowPrice"),
            Field<double>(type, bar, "ClosePrice"),
            checked((long)Field<ulong>(type, bar, "TickVolume")),
            Field<int>(type, bar, "Spread"));
    }

    private static T Field<T>(Type type, object instance, string name)
    {
        FieldInfo field = type.GetField(name)
            ?? throw new MissingFieldException(type.FullName, name);
        return (T)field.GetValue(instance)!;
    }

    private void Invoke(string name, object?[] arguments)
    {
        MethodInfo method = apiType.GetMethod(name, Type.EmptyTypes)
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
            // A disconnect that fails leaves nothing to clean up on this side, and throwing
            // from Dispose would mask whatever the caller was already handling.
        }
        finally
        {
            connected = false;
        }
    }
}
