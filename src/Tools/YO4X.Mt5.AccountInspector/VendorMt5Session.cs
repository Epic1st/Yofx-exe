using System.Collections;
using System.Globalization;
using System.Reflection;

namespace YO4X.Mt5.AccountInspector;

/// <summary>What the broker reports once a session is established.</summary>
/// <param name="Company">The broker's own name for itself.</param>
/// <param name="Currency">The deposit currency.</param>
/// <param name="AccountGroup">
/// The account group string. MetaTrader states demo or live status here, so it is read from
/// the live session rather than guessed from a server name that merely says "Demo".
/// </param>
/// <param name="Balance">Closed-trade balance.</param>
/// <param name="Equity">Balance plus floating profit.</param>
/// <param name="OpenOrderCount">How many orders the broker currently holds open.</param>
/// <param name="ServerName">The server name the vendor holds for this session.</param>
/// <param name="Host">The access node the vendor actually attached to.</param>
/// <param name="Port">The access port the vendor actually attached to.</param>
internal sealed record VendorAccountSnapshot(
    string Company,
    string Currency,
    string AccountGroup,
    double Balance,
    double Equity,
    int OpenOrderCount,
    string ServerName,
    string Host,
    int Port);

/// <summary>
/// A read-only session against one broker account.
///
/// <para>
/// The vendor assembly also exposes <c>OrderSend</c> and friends. None of them is reachable
/// from here on purpose: this tool answers whether an account can be reached and what state
/// it is in, and that question does not need the power to trade.
/// </para>
/// </summary>
internal sealed class VendorMt5Session : IDisposable
{
    private readonly Type apiType;
    private readonly object instance;
    private bool connected;

    private VendorMt5Session(Type apiType, object instance)
    {
        this.apiType = apiType;
        this.instance = instance;
    }

    /// <summary>
    /// Builds a session that resolves its own access node from the broker server name, using
    /// the vendor constructor that takes a server rather than a host and port.
    /// </summary>
    /// <param name="apiType">The loaded vendor client type.</param>
    /// <param name="login">The account login.</param>
    /// <param name="password">The account password.</param>
    /// <param name="server">The broker server name, for example <c>PUPrime-Demo</c>.</param>
    internal static VendorMt5Session ForServerName(
        Type apiType,
        ulong login,
        string password,
        string server)
    {
        ArgumentNullException.ThrowIfNull(apiType);
        ArgumentNullException.ThrowIfNull(password);
        ArgumentException.ThrowIfNullOrWhiteSpace(server);

        ConstructorInfo constructor = apiType.GetConstructor(
            [typeof(ulong), typeof(string), typeof(string), typeof(byte[]), typeof(string)])
            ?? throw new MissingMethodException(apiType.FullName, ".ctor(ulong,string,string,byte[],string)");
        object created = constructor.Invoke([login, password, server, Array.Empty<byte>(), string.Empty]);
        return new VendorMt5Session(apiType, created);
    }

    /// <summary>Builds a session against one explicit access node.</summary>
    /// <param name="apiType">The loaded vendor client type.</param>
    /// <param name="login">The account login.</param>
    /// <param name="password">The account password.</param>
    /// <param name="host">The access-server host.</param>
    /// <param name="port">The access-server port.</param>
    internal static VendorMt5Session ForEndpoint(
        Type apiType,
        ulong login,
        string password,
        string host,
        int port)
    {
        ArgumentNullException.ThrowIfNull(apiType);
        ArgumentNullException.ThrowIfNull(password);
        ArgumentException.ThrowIfNullOrWhiteSpace(host);

        ConstructorInfo constructor = apiType.GetConstructor(
            [typeof(ulong), typeof(string), typeof(string), typeof(int)])
            ?? throw new MissingMethodException(apiType.FullName, ".ctor(ulong,string,string,int)");
        object created = constructor.Invoke([login, password, host, port]);
        return new VendorMt5Session(apiType, created);
    }

    /// <summary>Whether the vendor reports an established session.</summary>
    internal bool Connected => Property<bool?>("Connected") ?? false;

    /// <summary>Sets the vendor connect timeout, in milliseconds, before connecting.</summary>
    /// <param name="milliseconds">The timeout to apply.</param>
    internal void SetConnectTimeout(int milliseconds)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(milliseconds, 0);
        apiType.GetField("ConnectTimeout")?.SetValue(instance, milliseconds);
        apiType.GetField("ConnectTimeoutForOneClusterMember")?.SetValue(instance, milliseconds);
    }

    /// <summary>Opens the session, surfacing the vendor failure itself rather than a wrapper.</summary>
    internal void Connect()
    {
        MethodInfo connect = apiType.GetMethod("Connect", Type.EmptyTypes)
            ?? throw new MissingMethodException(apiType.FullName, "Connect");
        try
        {
            connect.Invoke(instance, null);
        }
        catch (TargetInvocationException invocation) when (invocation.InnerException is not null)
        {
            throw invocation.InnerException;
        }

        connected = true;
    }

    /// <summary>Reads everything the broker reports for this account.</summary>
    internal VendorAccountSnapshot Read()
    {
        if (!connected)
        {
            throw new InvalidOperationException("Connect before reading the account.");
        }

        object? account = apiType.GetProperty("Account")?.GetValue(instance);
        return new VendorAccountSnapshot(
            Property<string>("AccountCompanyName") ?? string.Empty,
            Property<string>("AccountCurrency") ?? string.Empty,
            ReadRecordMember<string>(account, "Type") ?? string.Empty,
            ReadRecordMember<double?>(account, "Balance") ?? Property<double?>("AccountEquity") ?? 0d,
            Property<double?>("AccountEquity") ?? 0d,
            CountOpenOrders(),
            apiType.GetField("Server")?.GetValue(instance) as string ?? string.Empty,
            Property<string>("Host") ?? string.Empty,
            Property<int?>("Port") ?? 0);
    }

    private int CountOpenOrders()
    {
        MethodInfo? opened = apiType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .FirstOrDefault(method => method.Name == "GetOpenedOrders"
                && method.GetParameters().Length == 2);
        if (opened is null)
        {
            return 0;
        }

        ParameterInfo[] parameters = opened.GetParameters();
        object sort = Enum.ToObject(parameters[0].ParameterType, 0);
        try
        {
            return opened.Invoke(instance, [sort, true]) is IList orders ? orders.Count : 0;
        }
        catch (TargetInvocationException invocation) when (invocation.InnerException is not null)
        {
            throw invocation.InnerException;
        }
    }

    /// <summary>
    /// Reads a member the vendor may expose as either a field or a property. Record shapes
    /// differ across vendor builds, so an absent member is reported as absent rather than
    /// taking the whole read down.
    /// </summary>
    private static T? ReadRecordMember<T>(object? record, string name)
    {
        if (record is null)
        {
            return default;
        }

        Type type = record.GetType();
        object? value = type.GetField(name)?.GetValue(record)
            ?? type.GetProperty(name)?.GetValue(record);
        return value is T typed ? typed : default;
    }

    private T? Property<T>(string name)
    {
        object? value = apiType.GetProperty(name)?.GetValue(instance);
        if (value is T typed)
        {
            return typed;
        }

        if (value is not null && typeof(T) == typeof(string))
        {
            return (T)(object)Convert.ToString(value, CultureInfo.InvariantCulture)!;
        }

        return default;
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
            // The session is being abandoned either way.
        }
        finally
        {
            connected = false;
        }
    }
}
