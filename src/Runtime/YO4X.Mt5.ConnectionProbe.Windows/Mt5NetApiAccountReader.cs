using System.Collections;
using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;

namespace YO4X.Mt5.ConnectionProbe.Windows;

/// <summary>One position or order the broker currently holds open.</summary>
/// <param name="Ticket">The broker's identifier for it.</param>
/// <param name="Symbol">The instrument.</param>
/// <param name="Type">The order type, as the broker names it.</param>
/// <param name="Volume">The size in lots.</param>
/// <param name="OpenPrice">The price it opened at.</param>
/// <param name="StopLoss">The stop price, zero when none is set.</param>
/// <param name="TakeProfit">The target price, zero when none is set.</param>
/// <param name="Profit">The floating profit the broker reports.</param>
/// <param name="OpenTime">When it opened, in broker server time.</param>
/// <param name="Comment">Whatever comment the order carries.</param>
public sealed record Mt5OpenOrder(
    long Ticket,
    string Symbol,
    string Type,
    double Volume,
    double OpenPrice,
    double StopLoss,
    double TakeProfit,
    double Profit,
    DateTime OpenTime,
    string Comment);

/// <summary>What the broker reports about the account itself.</summary>
/// <param name="Login">The account login.</param>
/// <param name="Company">The broker's name.</param>
/// <param name="Currency">The deposit currency.</param>
/// <param name="Balance">Closed-trade balance.</param>
/// <param name="Equity">Balance plus floating profit.</param>
/// <param name="Margin">Margin currently in use.</param>
/// <param name="FreeMargin">Margin still available.</param>
/// <param name="Profit">Total floating profit.</param>
/// <param name="ServerTimeZoneInMinutes">The server's offset from UTC, as reported.</param>
/// <param name="AccountType">The account group the broker reports, which states demo or live.</param>
/// <param name="ServerName">The server name the vendor holds for this session.</param>
public sealed record Mt5AccountState(
    ulong Login,
    string Company,
    string Currency,
    double Balance,
    double Equity,
    double Margin,
    double FreeMargin,
    double Profit,
    int? ServerTimeZoneInMinutes,
    string AccountType,
    string ServerName);

/// <summary>
/// Reads account state and open orders from a live broker session.
///
/// <para>
/// This type is deliberately read-only. The vendor assembly also exposes <c>OrderSend</c>,
/// <c>OrderClose</c> and <c>OrderModify</c>; none of them is reachable from here, and that is
/// the point — observing an account and instructing it are different powers, and only the
/// first one is needed to answer whether a strategy's trades exist and how they are behaving.
/// </para>
/// </summary>
public sealed class Mt5NetApiAccountReader : IDisposable
{
    private readonly Type apiType;
    private readonly object instance;
    private bool connected;

    private Mt5NetApiAccountReader(Type apiType, object instance)
    {
        this.apiType = apiType;
        this.instance = instance;
    }

    /// <summary>Verifies the vendor artifact, loads it, and constructs a reader for one account.</summary>
    /// <param name="artifactPath">Path to the pinned vendor assembly.</param>
    /// <param name="login">The account login.</param>
    /// <param name="password">The account password, used only for the vendor constructor.</param>
    /// <param name="host">The broker access-server host.</param>
    /// <param name="port">The broker access-server port.</param>
    public static Mt5NetApiAccountReader Create(
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
        return new Mt5NetApiAccountReader(apiType, instance);
    }

    /// <summary>Whether the vendor reports an established session.</summary>
    public bool Connected => Property<bool?>("Connected") ?? false;

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

    /// <summary>Reads what the broker reports about the account.</summary>
    public Mt5AccountState ReadAccount()
    {
        RequireConnected();
        return new Mt5AccountState(
            Property<ulong?>("User") ?? 0UL,
            Property<string>("AccountCompanyName") ?? string.Empty,
            Property<string>("AccountCurrency") ?? string.Empty,
            Property<double?>("AccountBalance") ?? Property<double?>("AccountEquity") ?? 0d,
            Property<double?>("AccountEquity") ?? 0d,
            Property<double?>("AccountMargin") ?? 0d,
            Property<double?>("AccountFreeMargin") ?? 0d,
            Property<double?>("AccountProfit") ?? 0d,
            Property<int?>("ServerTimeZoneInMinutes"),
            ReadAccountType(),
            apiType.GetField("Server")?.GetValue(instance) as string ?? string.Empty);
    }

    /// <summary>
    /// Reads every order the broker currently holds open for this account.
    /// </summary>
    public IReadOnlyList<Mt5OpenOrder> ReadOpenOrders()
    {
        RequireConnected();
        MethodInfo? opened = apiType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .FirstOrDefault(method => method.Name == "GetOpenedOrders"
                && method.GetParameters().Length == 2);
        if (opened is null)
        {
            throw new MissingMethodException(apiType.FullName, "GetOpenedOrders");
        }

        ParameterInfo[] parameters = opened.GetParameters();
        object sort = Enum.ToObject(parameters[0].ParameterType, 0);
        if (opened.Invoke(instance, [sort, true]) is not IList orders)
        {
            return [];
        }

        var result = new List<Mt5OpenOrder>(orders.Count);
        foreach (object? order in orders)
        {
            if (order is not null)
            {
                result.Add(ReadOrder(order));
            }
        }

        return result;
    }

    /// <summary>
    /// Reads every instrument the broker offers this account.
    ///
    /// <para>
    /// The list is only populated after a successful connect — the vendor fills it from the
    /// server's own catalogue during login — so this must not be called before Connect.
    /// </para>
    /// </summary>
    public IReadOnlyList<Mt5BrokerSymbol> ReadSymbols()
    {
        RequireConnected();
        object? catalogue = apiType.GetField("Symbols")?.GetValue(instance)
            ?? apiType.GetProperty("Symbols")?.GetValue(instance);
        if (catalogue is null)
        {
            return [];
        }

        Type catalogueType = catalogue.GetType();
        if ((catalogueType.GetProperty("Names")?.GetValue(catalogue)) is not string[] names)
        {
            return [];
        }

        System.Reflection.MethodInfo? info = catalogueType.GetMethod("GetInfo", [typeof(string)]);
        var symbols = new List<Mt5BrokerSymbol>(names.Length);
        foreach (string name in names)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            object? detail = null;
            try
            {
                detail = info?.Invoke(catalogue, [name]);
            }
            catch (System.Reflection.TargetInvocationException)
            {
                // A symbol the broker lists but will not describe is still a symbol. It is
                // recorded by name with everything else left absent, rather than dropped.
            }

            symbols.Add(detail is null
                ? new Mt5BrokerSymbol(name, null, null, null, null, null, null)
                : ReadSymbol(name, detail));
        }

        return symbols;
    }

    private static Mt5BrokerSymbol ReadSymbol(string name, object detail)
    {
        Type type = detail.GetType();
        return new Mt5BrokerSymbol(
            name,
            NullIfBlank(Member<string>(type, detail, "Description")),
            Positive(Member<int>(type, detail, "Digits")),
            Positive((decimal)Member<double>(type, detail, "ContractSize")),
            CurrencyCode(type, detail),
            Positive((decimal)Member<double>(type, detail, "TickSize")),
            Positive((decimal)Member<double>(type, detail, "TickValue")));
    }

    /// <summary>
    /// The instrument's quote currency, but only when the broker really reports one.
    ///
    /// <para>
    /// This vendor's <c>Currency</c> field does not hold a currency for every instrument — on
    /// equities it repeats the symbol, so a share called MARA reports "MARA". Storing that
    /// would put a non-currency in a currency column and, downstream, price a position in an
    /// invented unit. So <c>ProfitCurrency</c> is preferred and anything that is not three
    /// letters is discarded rather than passed along.
    /// </para>
    /// </summary>
    private static string? CurrencyCode(Type type, object detail)
    {
        foreach (string field in (string[])["ProfitCurrency", "MarginCurrency", "Currency"])
        {
            string? candidate = NullIfBlank(Member<string>(type, detail, field));
            if (candidate is { Length: 3 } code && code.All(char.IsAsciiLetterUpper))
            {
                return code;
            }
        }

        return null;
    }

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static int? Positive(int value) => value > 0 ? value : null;

    private static decimal? Positive(decimal value) => value > 0 ? value : null;

    private static Mt5OpenOrder ReadOrder(object order)
    {
        Type type = order.GetType();
        return new Mt5OpenOrder(
            Member<long>(type, order, "Ticket"),
            Member<string>(type, order, "Symbol") ?? string.Empty,
            Member<object>(type, order, "Type")?.ToString() ?? string.Empty,
            Member<double>(type, order, "Lots"),
            Member<double>(type, order, "OpenPrice"),
            Member<double>(type, order, "StopLoss"),
            Member<double>(type, order, "TakeProfit"),
            Member<double>(type, order, "Profit"),
            Member<DateTime>(type, order, "OpenTime"),
            Member<string>(type, order, "Comment") ?? string.Empty);
    }

    /// <summary>
    /// Reads a member that the vendor may expose as either a field or a property, and returns
    /// the type's default when it exposes neither. Order shapes differ across vendor builds,
    /// so a missing member is reported as absent rather than crashing the read.
    /// </summary>
    private static T? Member<T>(Type type, object instance, string name)
    {
        object? value = type.GetProperty(name)?.GetValue(instance)
            ?? type.GetField(name)?.GetValue(instance);
        return value is T typed ? typed : default;
    }

    /// <summary>
    /// The account group string the broker reports. MetaTrader states demo or live status
    /// here, so it is read from the session rather than inferred from a server name.
    /// </summary>
    private string ReadAccountType()
    {
        object? record = apiType.GetProperty("Account")?.GetValue(instance);
        if (record is null)
        {
            return string.Empty;
        }

        Type type = record.GetType();
        return (type.GetField("Type")?.GetValue(record)
            ?? type.GetProperty("Type")?.GetValue(record)) as string ?? string.Empty;
    }

    private T? Property<T>(string name)
    {
        object? value = apiType.GetProperty(name)?.GetValue(instance);
        return value is T typed ? typed : default;
    }

    private void RequireConnected()
    {
        if (!connected)
        {
            throw new InvalidOperationException("Connect before reading the account.");
        }
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
