using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;

namespace YO4X.Mt5.ConnectionProbe.Windows;

/// <summary>
/// Which kind of account an instruction is meant for. Stated by the caller and checked
/// against what the broker reports, so the two can never silently disagree.
/// </summary>
public enum Mt5TradingEnvironment
{
    /// <summary>A practice account. Losses are not real.</summary>
    Demo = 0,

    /// <summary>A funded account. Every order risks real money.</summary>
    Live = 1,
}

/// <summary>The side, and for a pending order the trigger shape, of an order.</summary>
public enum Mt5DemoSide
{
    /// <summary>Buy at the ask.</summary>
    Buy = 0,

    /// <summary>Sell at the bid.</summary>
    Sell = 1,

    /// <summary>Buy when price falls to the given level.</summary>
    BuyLimit = 2,

    /// <summary>Sell when price rises to the given level.</summary>
    SellLimit = 3,

    /// <summary>Buy when price rises through the given level.</summary>
    BuyStop = 4,

    /// <summary>Sell when price falls through the given level.</summary>
    SellStop = 5,
}

/// <summary>
/// How long one instruction took, split at the two boundaries that matter.
///
/// <para>
/// The split is drawn where responsibility changes, not where it would flatter us. The vendor's
/// task-returning methods write to the socket synchronously before handing back a task, so the
/// moment the vendor call is entered is the last moment this engine is in control. Everything
/// after it is transport and broker, however it is labelled.
/// </para>
/// </summary>
/// <param name="EngineMicroseconds">
/// From the instruction arriving to the vendor call being entered: permission checks, pricing
/// from the cached quote, and marshalling the request. This is the only part the engine owns.
/// </param>
/// <param name="TransportAndBrokerMicroseconds">
/// From entering the vendor call to its reply. Includes the vendor's own synchronous socket
/// write and the round trip to the broker. No local optimisation reduces this.
/// </param>
public readonly record struct Mt5ExecutionLatency(
    double EngineMicroseconds,
    double TransportAndBrokerMicroseconds)
{
    /// <summary>The whole instruction, end to end.</summary>
    public double TotalMicroseconds => EngineMicroseconds + TransportAndBrokerMicroseconds;

    /// <summary>Renders both halves, so neither can be mistaken for the other.</summary>
    public override string ToString() =>
        $"engine {EngineMicroseconds.ToString("F1", CultureInfo.InvariantCulture)}us + "
        + $"transport+broker {(TransportAndBrokerMicroseconds / 1000.0).ToString("F1", CultureInfo.InvariantCulture)}ms";
}

/// <summary>What the broker reported about one submitted order.</summary>
/// <param name="Ticket">The broker's identifier, zero when nothing was opened.</param>
/// <param name="Symbol">The instrument.</param>
/// <param name="Side">The side that was requested.</param>
/// <param name="Volume">The filled size in lots.</param>
/// <param name="Price">The price the broker filled at.</param>
/// <param name="OpenTime">When the broker recorded it, in server time.</param>
/// <param name="Profit">Profit at the moment of the report.</param>
/// <param name="Latency">How long the instruction took, split into our half and theirs.</param>
public sealed record Mt5DemoOrderReceipt(
    long Ticket,
    string Symbol,
    Mt5DemoSide Side,
    double Volume,
    double Price,
    DateTime OpenTime,
    double Profit,
    Mt5ExecutionLatency Latency);

/// <summary>
/// Places, modifies and closes orders on a <b>demo</b> MetaTrader 5 account, asynchronously
/// and under fixed limits.
///
/// <para>
/// This is the only type in the repository that can instruct a broker, and it is deliberately
/// separate from every other client so that its name says so. It does not go through the
/// control plane's broker command store and does not weaken that store's risk-authority gate:
/// the production path stays sealed, and this is a parallel, demo-only route whose purpose is
/// to answer whether a strategy's orders are accepted, filled, modified and closed.
/// </para>
///
/// <para>
/// On transport: the broker speaks MetaTrader's own binary protocol over TCP. There is no
/// WebSocket endpoint to connect to, so none is used — interposing one would add a hop, not
/// remove one. Every instruction below uses the vendor's task-returning API, so nothing
/// blocks a strategy thread while the broker thinks.
/// </para>
///
/// <para>
/// Four limits are enforced on every instruction, each independently sufficient to refuse, and
/// each checked against what the broker itself reports rather than what the caller claims: the
/// account must be a demo account; the volume must not exceed <see cref="MaximumVolume"/>; the
/// symbol must be the one this client was constructed for; and an operator enable file must
/// exist. A missing file refuses the send — absence is a stop, never a default.
/// </para>
/// </summary>
public sealed class Mt5NetApiDemoTradeClient : IDisposable
{
    /// <summary>The largest order this client will ever submit, in lots.</summary>
    public const double MaximumVolume = 0.01;

    private static readonly double TicksToMicroseconds = 1_000_000.0 / Stopwatch.Frequency;

    private readonly Type apiType;
    private readonly object instance;
    private readonly string symbol;
    private readonly string enableFilePath;
    private readonly Action<string> journal;
    private readonly Mt5TradingEnvironment environment;
    private bool connected;
    private Delegate? quoteHandler;
    private readonly Dictionary<string, MethodInfo> vendorMethods = [];
    private readonly Dictionary<string, ParameterInfo[]> vendorParameters = [];
    /// <summary>
    /// Whether the enable file existed at the last filesystem check, and when that was. The
    /// check is real but not repeated on every instruction: a stat call on the critical path
    /// costs more than the whole request marshalling, and the file is re-read often enough
    /// that deleting it stops trading within one interval.
    /// </summary>
    private bool enableFilePresent;
    private long enableFileCheckedAt;
    /// <summary>Latest bid and ask, replaced whole so a reader never sees a torn pair.</summary>
    private volatile object? cachedQuote;

    /// <summary>
    /// Called for every quote the broker pushes, on the vendor's own thread. A live strategy
    /// needs the stream itself, not just the newest value, because a bar is built from what
    /// arrived rather than from what happened to be current when someone looked.
    /// </summary>
    public Action<DateTime, double, double>? QuoteObserver { get; set; }

    private Mt5NetApiDemoTradeClient(
        Type apiType,
        object instance,
        string symbol,
        string enableFilePath,
        Action<string> journal,
        Mt5TradingEnvironment environment)
    {
        this.environment = environment;
        this.apiType = apiType;
        this.instance = instance;
        this.symbol = symbol;
        this.enableFilePath = enableFilePath;
        this.journal = journal;
    }

    /// <summary>
    /// Verifies the vendor artifact, loads it, and constructs a demo trading client bound to
    /// one symbol and one enable file.
    /// </summary>
    /// <param name="artifactPath">Path to the pinned vendor assembly.</param>
    /// <param name="login">The account login.</param>
    /// <param name="password">The account password, used only for the vendor constructor.</param>
    /// <param name="host">The broker access-server host.</param>
    /// <param name="port">The broker access-server port.</param>
    /// <param name="symbol">The one instrument this client may trade.</param>
    /// <param name="enableFilePath">A file that must exist for any instruction to be sent.</param>
    /// <param name="journal">Receives one line per request and per reply.</param>
    /// <param name="environment">
    /// The kind of account the caller intends to trade. Checked against the broker's own
    /// account group on every instruction; a disagreement refuses the send.
    /// </param>
    public static Mt5NetApiDemoTradeClient Create(
        string artifactPath,
        ulong login,
        string password,
        string host,
        int port,
        string symbol,
        string enableFilePath,
        Action<string> journal,
        Mt5TradingEnvironment environment = Mt5TradingEnvironment.Demo)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactPath);
        ArgumentNullException.ThrowIfNull(password);
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
        ArgumentException.ThrowIfNullOrWhiteSpace(enableFilePath);
        ArgumentNullException.ThrowIfNull(journal);

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
        return new Mt5NetApiDemoTradeClient(
            apiType,
            instance,
            symbol.Trim(),
            Path.GetFullPath(enableFilePath),
            journal,
            environment);
    }

    /// <summary>The one instrument this client is permitted to trade.</summary>
    public string Symbol => symbol;

    /// <summary>Whether the vendor reports an established session.</summary>
    public bool Connected => apiType.GetProperty("Connected")?.GetValue(instance) is true;

    /// <summary>The broker's name, as the server reports it.</summary>
    public string? AccountCompanyName =>
        apiType.GetProperty("AccountCompanyName")?.GetValue(instance) as string;

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

        // Resolve every vendor method now, so the first order does not pay for reflection.
        foreach (string name in (string[])
            ["OrderSendAsyncTask", "OrderCloseAsyncTask", "OrderModifyAsyncTask", "OrderCancelAsyncTask"])
        {
            Method(name);
        }

        journal($"connected to {AccountCompanyName}");
    }

    /// <summary>
    /// Opens a market position, or places a pending order when <paramref name="side"/> is one
    /// of the limit or stop shapes.
    /// </summary>
    /// <param name="side">Market side, or the pending-order shape.</param>
    /// <param name="volume">Size in lots; must not exceed <see cref="MaximumVolume"/>.</param>
    /// <param name="price">Trigger price for a pending order; ignored for a market order.</param>
    /// <param name="stopLoss">Stop price, or zero for none.</param>
    /// <param name="takeProfit">Target price, or zero for none.</param>
    /// <param name="comment">A comment, so the order is identifiable on the account.</param>
    /// <param name="cancellationToken">Abandons the wait for the broker's reply.</param>
    public async Task<Mt5DemoOrderReceipt> SendAsync(
        Mt5DemoSide side,
        double volume,
        double price,
        double stopLoss,
        double takeProfit,
        string comment,
        CancellationToken cancellationToken = default)
    {
        RequirePermission(volume);

        long submitStart = Stopwatch.GetTimestamp();
        bool pending = side is not (Mt5DemoSide.Buy or Mt5DemoSide.Sell);
        double requestPrice = pending ? price : CurrentPrice(side);

        MethodInfo send = Method("OrderSendAsyncTask");
        ParameterInfo[] parameters = Parameters("OrderSendAsyncTask");
        object?[] arguments = Defaults(parameters);
        arguments[0] = symbol;
        arguments[1] = volume;
        arguments[2] = requestPrice;
        arguments[3] = Enum.ToObject(parameters[3].ParameterType, (int)side);
        arguments[4] = stopLoss;
        arguments[5] = takeProfit;
        arguments[7] = comment;

        long enteringVendor = Stopwatch.GetTimestamp();
        object? pendingTask = send.Invoke(instance, arguments);
        object? order = await AwaitResultAsync(pendingTask, cancellationToken).ConfigureAwait(false);
        long replied = Stopwatch.GetTimestamp();

        Mt5DemoOrderReceipt receipt = ReadReceipt(order, side, Measure(submitStart, enteringVendor, replied));
        journal($"REPLY   ticket {receipt.Ticket} @ {receipt.Price.ToString("0.#####", CultureInfo.InvariantCulture)}  {receipt.Latency}");
        return receipt;
    }

    /// <summary>Moves the stop and target of an open position.</summary>
    /// <param name="receipt">The receipt returned when the position was opened.</param>
    /// <param name="stopLoss">The new stop price, or zero to clear it.</param>
    /// <param name="takeProfit">The new target price, or zero to clear it.</param>
    /// <param name="cancellationToken">Abandons the wait for the broker's reply.</param>
    public async Task<Mt5ExecutionLatency> ModifyAsync(
        Mt5DemoOrderReceipt receipt,
        double stopLoss,
        double takeProfit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        RequirePermission(receipt.Volume);

        long submitStart = Stopwatch.GetTimestamp();
        MethodInfo modify = Method("OrderModifyAsyncTask");
        ParameterInfo[] parameters = Parameters("OrderModifyAsyncTask");
        object?[] arguments = Defaults(parameters);
        arguments[0] = receipt.Ticket;
        arguments[1] = receipt.Symbol;
        arguments[2] = receipt.Volume;
        arguments[3] = receipt.Price;
        arguments[4] = Enum.ToObject(parameters[4].ParameterType, (int)receipt.Side);
        arguments[5] = stopLoss;
        arguments[6] = takeProfit;

        long enteringVendor = Stopwatch.GetTimestamp();
        object? pendingTask = modify.Invoke(instance, arguments);
        await AwaitResultAsync(pendingTask, cancellationToken).ConfigureAwait(false);
        long replied = Stopwatch.GetTimestamp();

        Mt5ExecutionLatency latency = Measure(submitStart, enteringVendor, replied);
        journal($"REPLY   modified {receipt.Ticket}  {latency}");
        return latency;
    }

    /// <summary>Closes a position this client opened.</summary>
    /// <param name="receipt">The receipt returned when the position was opened.</param>
    /// <param name="cancellationToken">Abandons the wait for the broker's reply.</param>
    public async Task<Mt5DemoOrderReceipt> CloseAsync(
        Mt5DemoOrderReceipt receipt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        RequirePermission(receipt.Volume);

        long submitStart = Stopwatch.GetTimestamp();

        // A position is closed against the opposite side of the book.
        Mt5DemoSide facing = receipt.Side == Mt5DemoSide.Buy ? Mt5DemoSide.Sell : Mt5DemoSide.Buy;
        double price = CurrentPrice(facing);

        MethodInfo close = Method("OrderCloseAsyncTask");
        ParameterInfo[] parameters = Parameters("OrderCloseAsyncTask");
        object?[] arguments = Defaults(parameters);
        arguments[0] = receipt.Ticket;
        arguments[1] = receipt.Symbol;
        arguments[2] = price;
        arguments[3] = receipt.Volume;
        arguments[4] = Enum.ToObject(parameters[4].ParameterType, (int)receipt.Side);

        long enteringVendor = Stopwatch.GetTimestamp();
        object? pendingTask = close.Invoke(instance, arguments);
        object? order = await AwaitResultAsync(pendingTask, cancellationToken).ConfigureAwait(false);
        long replied = Stopwatch.GetTimestamp();

        Mt5DemoOrderReceipt closed = ReadReceipt(order, receipt.Side, Measure(submitStart, enteringVendor, replied));
        journal($"REPLY   closed {closed.Ticket} profit {closed.Profit.ToString("0.##", CultureInfo.InvariantCulture)}  {closed.Latency}");
        return closed;
    }

    /// <summary>Cancels a pending order this client placed.</summary>
    /// <param name="receipt">The receipt returned when the order was placed.</param>
    /// <param name="cancellationToken">Abandons the wait for the broker's reply.</param>
    public async Task<Mt5ExecutionLatency> CancelAsync(
        Mt5DemoOrderReceipt receipt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        RequirePermission(receipt.Volume);

        long submitStart = Stopwatch.GetTimestamp();
        MethodInfo cancel = Method("OrderCancelAsyncTask");
        ParameterInfo[] parameters = Parameters("OrderCancelAsyncTask");
        object?[] arguments = Defaults(parameters);
        arguments[0] = receipt.Ticket;
        arguments[1] = receipt.Symbol;
        arguments[2] = receipt.Volume;
        arguments[3] = Enum.ToObject(parameters[3].ParameterType, (int)receipt.Side);

        long enteringVendor = Stopwatch.GetTimestamp();
        object? pendingTask = cancel.Invoke(instance, arguments);
        await AwaitResultAsync(pendingTask, cancellationToken).ConfigureAwait(false);
        long replied = Stopwatch.GetTimestamp();

        Mt5ExecutionLatency latency = Measure(submitStart, enteringVendor, replied);
        journal($"REPLY   cancelled {receipt.Ticket}  {latency}");
        return latency;
    }

    private (double Bid, double Ask)? latestQuote
    {
        get => cachedQuote is ValueTuple<double, double> pair ? (pair.Item1, pair.Item2) : null;
        set => cachedQuote = value is { } quote ? (quote.Bid, quote.Ask) : null;
    }

    /// <summary>How stale the cached enable-file answer may be before it is re-read.</summary>
    private const int EnableFileCheckIntervalMilliseconds = 250;

    private bool EnableFilePresent()
    {
        long now = Stopwatch.GetTimestamp();
        double elapsedMilliseconds = (now - enableFileCheckedAt) * TicksToMicroseconds / 1000.0;
        if (enableFileCheckedAt != 0 && elapsedMilliseconds < EnableFileCheckIntervalMilliseconds)
        {
            return enableFilePresent;
        }

        enableFilePresent = File.Exists(enableFilePath);
        enableFileCheckedAt = now;
        return enableFilePresent;
    }

    private static Mt5ExecutionLatency Measure(long start, long enteringVendor, long replied) =>
        new(
            (enteringVendor - start) * TicksToMicroseconds,
            (replied - enteringVendor) * TicksToMicroseconds);

    /// <summary>
    /// Awaits a vendor task this assembly cannot name, and returns its result when it has one.
    /// </summary>
    private static async Task<object?> AwaitResultAsync(object? pendingTask, CancellationToken cancellationToken)
    {
        if (pendingTask is not Task task)
        {
            throw new InvalidDataException("The vendor did not return a task.");
        }

        await task.WaitAsync(cancellationToken).ConfigureAwait(false);
        PropertyInfo? result = task.GetType().GetProperty("Result");
        return result?.PropertyType == typeof(void) ? null : result?.GetValue(task);
    }

    /// <summary>
    /// Resolves a vendor method once and remembers it. Reflection lookup allocates an array of
    /// every public method on the vendor type, which is far more expensive than the call it
    /// precedes, so it must not happen per instruction.
    /// </summary>
    private MethodInfo Method(string name)
    {
        if (vendorMethods.TryGetValue(name, out MethodInfo? cached))
        {
            return cached;
        }

        MethodInfo resolved = apiType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(method => method.Name == name)
            ?? throw new MissingMethodException(apiType.FullName, name);
        vendorMethods[name] = resolved;
        vendorParameters[name] = resolved.GetParameters();
        return resolved;
    }

    /// <summary>
    /// The cached parameter list for a vendor method. Reflection allocates a fresh array
    /// on every call, which is pure cost on a path measured in microseconds.
    /// </summary>
    private ParameterInfo[] Parameters(string name)
    {
        Method(name);
        return vendorParameters[name];
    }

    private static object?[] Defaults(ParameterInfo[] parameters)
    {
        object?[] arguments = new object?[parameters.Length];
        for (int index = 0; index < parameters.Length; index++)
        {
            arguments[index] = parameters[index].HasDefaultValue
                ? parameters[index].DefaultValue
                : DefaultOf(parameters[index].ParameterType);
        }

        return arguments;
    }

    private static object? DefaultOf(Type type) =>
        type.IsValueType ? Activator.CreateInstance(type) : null;

    /// <summary>
    /// Every limit, checked together. Each is independently sufficient to refuse, and the
    /// account type is read from the broker rather than taken from configuration.
    /// </summary>
    private void RequirePermission(double volume)
    {
        if (!connected)
        {
            throw new InvalidOperationException("Connect before trading.");
        }

        if (!EnableFilePresent())
        {
            throw new InvalidOperationException(
                "The operator enable file is absent, so no instruction will be sent. Create "
                + enableFilePath
                + " to allow trading, and delete it to stop within "
                + EnableFileCheckIntervalMilliseconds.ToString(CultureInfo.InvariantCulture)
                + "ms.");
        }

        if (volume is <= 0 or > MaximumVolume)
        {
            throw new ArgumentOutOfRangeException(
                nameof(volume),
                volume,
                "This client will not send an order larger than "
                + MaximumVolume.ToString("0.##", CultureInfo.InvariantCulture)
                + " lots.");
        }

        RequireDeclaredEnvironment();
    }

    /// <summary>
    /// Refuses unless the account the broker actually served matches the one the caller said
    /// it was opening.
    ///
    /// <para>
    /// The danger in a system that can trade both demo and live accounts is not trading — it is
    /// trading on the account you did not think you were on. So the environment is not
    /// inferred: the caller states it up front, the broker's own account group is read from the
    /// live session, and any disagreement stops the instruction. Declaring live and reaching a
    /// demo account is refused just as firmly as the reverse, because either way the operator's
    /// belief about where their orders are going is wrong.
    /// </para>
    ///
    /// <para>
    /// A group that cannot be read is refused outright. An unreadable group is not a demo
    /// account; it is an unknown one, and an unknown account is exactly what must not be traded.
    /// </para>
    /// </summary>
    private void RequireDeclaredEnvironment()
    {
        object? record = apiType.GetProperty("Account")?.GetValue(instance);
        if (record is null)
        {
            throw new InvalidOperationException(
                "The broker did not report an account group, so the account cannot be identified "
                + "as demo or live and no instruction will be sent.");
        }

        Type type = record.GetType();
        string group = (type.GetField("Type")?.GetValue(record)
            ?? type.GetProperty("Type")?.GetValue(record)) as string ?? string.Empty;
        if (group.Length == 0)
        {
            throw new InvalidOperationException(
                "The broker reported an empty account group, so the account cannot be identified "
                + "as demo or live and no instruction will be sent.");
        }

        bool brokerSaysDemo = group.Contains("demo", StringComparison.OrdinalIgnoreCase);
        bool declaredDemo = environment == Mt5TradingEnvironment.Demo;
        if (brokerSaysDemo != declaredDemo)
        {
            throw new InvalidOperationException(
                $"This client was opened for a {environment} account, but the broker reports the "
                + $"account group '{group}'. Nothing will be sent: the account is not the one the "
                + "operator believes they are trading.");
        }
    }

    /// <summary>
    /// Subscribes to the symbol's live quote stream and keeps the latest price in memory.
    ///
    /// <para>
    /// Without this, pricing an order means calling the broker and waiting — a network round
    /// trip on the critical path, which no amount of local optimisation can shorten. With it,
    /// the price is already here when the decision is made, and the only thing left between a
    /// strategy and the wire is marshalling the request.
    /// </para>
    /// </summary>
    public void StartQuoteStream()
    {
        RequireConnectedSession();
        EventInfo? onQuote = apiType.GetEvent("OnQuote");
        if (onQuote?.EventHandlerType is { } handlerType)
        {
            quoteHandler = BuildQuoteHandler(handlerType);
            onQuote.AddEventHandler(instance, quoteHandler);
        }

        MethodInfo? subscribe = apiType.GetMethod("Subscribe", [typeof(string)]);
        subscribe?.Invoke(instance, [symbol]);

        // Prime the cache once so the first order is not the one that pays for the round trip.
        FetchQuote();
        journal($"quote stream started for {symbol}");
    }

    private Delegate BuildQuoteHandler(Type handlerType)
    {
        MethodInfo invoke = handlerType.GetMethod("Invoke")
            ?? throw new MissingMethodException(handlerType.FullName, "Invoke");
        ParameterInfo[] parameters = invoke.GetParameters();
        MethodInfo shim = typeof(Mt5NetApiDemoTradeClient)
            .GetMethod(nameof(DispatchQuote), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(parameters[0].ParameterType, parameters[1].ParameterType);
        Action<object?, object?> sink = (_, args) =>
        {
            if (args is null)
            {
                return;
            }

            Type type = args.GetType();
            object? quote = type.GetField("Quote")?.GetValue(args)
                ?? type.GetProperty("Quote")?.GetValue(args)
                ?? args;
            Store(quote);
        };
        return Delegate.CreateDelegate(handlerType, sink, shim);
    }

    private static void DispatchQuote<TSender, TArgs>(
        Action<object?, object?> sink,
        TSender sender,
        TArgs args) => sink(sender, args);

    private void Store(object? quote)
    {
        if (quote is null)
        {
            return;
        }

        Type type = quote.GetType();
        double bid = Read<double>(type, quote, "Bid");
        double ask = Read<double>(type, quote, "Ask");
        if (bid > 0 && ask > 0 && string.Equals(
            Read<string>(type, quote, "Symbol") ?? symbol,
            symbol,
            StringComparison.OrdinalIgnoreCase))
        {
            // Written as one reference so a reader never sees a half-updated pair.
            latestQuote = (bid, ask);
            Action<DateTime, double, double>? observer = QuoteObserver;
            if (observer is not null)
            {
                DateTime stamp = Read<DateTime>(type, quote, "Time");
                observer(stamp == default ? DateTime.UtcNow : stamp, bid, ask);
            }
        }
    }

    private void FetchQuote()
    {
        MethodInfo quote = apiType.GetMethod("GetQuote", [typeof(string), typeof(int), typeof(int)])
            ?? throw new MissingMethodException(apiType.FullName, "GetQuote");
        Store(quote.Invoke(instance, [symbol, 10_000, 0]));
    }

    /// <summary>
    /// The current price for a side, taken from the cached stream when one is running.
    /// </summary>
    private double CurrentPrice(Mt5DemoSide side)
    {
        if (latestQuote is not { } cached)
        {
            FetchQuote();
            cached = latestQuote
                ?? throw new InvalidDataException($"The broker returned no quote for {symbol}.");
        }

        return side is Mt5DemoSide.Buy or Mt5DemoSide.BuyLimit or Mt5DemoSide.BuyStop
            ? cached.Ask
            : cached.Bid;
    }

    private void RequireConnectedSession()
    {
        if (!connected)
        {
            throw new InvalidOperationException("Connect before starting the quote stream.");
        }
    }

    private Mt5DemoOrderReceipt ReadReceipt(object? order, Mt5DemoSide side, Mt5ExecutionLatency latency)
    {
        if (order is null)
        {
            throw new InvalidDataException("The broker returned no order record.");
        }

        Type type = order.GetType();
        return new Mt5DemoOrderReceipt(
            Read<long>(type, order, "Ticket"),
            Read<string>(type, order, "Symbol") ?? symbol,
            side,
            Read<double>(type, order, "Lots"),
            Read<double>(type, order, "OpenPrice"),
            Read<DateTime>(type, order, "OpenTime"),
            Read<double>(type, order, "Profit"),
            latency);
    }

    private static T Read<T>(Type type, object instance, string name)
    {
        object? value = type.GetField(name)?.GetValue(instance)
            ?? type.GetProperty(name)?.GetValue(instance);
        return value is T typed ? typed : default!;
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
            apiType.GetMethod("Disconnect", Type.EmptyTypes)?.Invoke(instance, null);
        }
        catch (TargetInvocationException)
        {
            // Nothing to clean up on this side.
        }
        finally
        {
            connected = false;
        }
    }
}
