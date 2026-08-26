using System.Globalization;
using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text;
using YO4X.LocalSecrets.Windows;
using YO4X.Trading.Abstractions;

namespace YO4X.Mt5.ConnectionProbe.Windows;

public sealed record Mt5NetApiConnectionEndpoint(
    string BrokerCompany,
    string ServerName,
    string Host,
    int Port,
    byte[] CertificatePfx,
    string CertificatePassword)
{
    public Mt5NetApiConnectionEndpoint Snapshot()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(BrokerCompany);
        ArgumentException.ThrowIfNullOrWhiteSpace(ServerName);
        ArgumentException.ThrowIfNullOrWhiteSpace(Host);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(Port);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(Port, ushort.MaxValue);
        ArgumentNullException.ThrowIfNull(CertificatePfx);
        ArgumentNullException.ThrowIfNull(CertificatePassword);
        return this with { CertificatePfx = CertificatePfx.ToArray() };
    }
}

public interface IMt5NetApiConnectionClient : IDisposable
{
    bool Connected { get; }

    ulong User { get; }

    string? AccountCompanyName { get; }

    string? AccountCurrency { get; }

    object? AccountMethod { get; }

    void Connect();

    void Disconnect();
}

public interface IMt5NetApiConnectionClientFactory
{
    IMt5NetApiConnectionClient Create(
        ulong login,
        string password,
        string host,
        int port,
        byte[] certificatePfx,
        string certificatePassword);
}

/// <summary>
/// Dynamically loads only the exact reviewed vendor bytes. Hash verification occurs
/// before AssemblyLoadContext sees the artifact, preventing an unpinned assembly from
/// running a module initializer or vendor constructor.
/// </summary>
public sealed class PinnedMt5NetApiConnectionClientFactory
    : IMt5NetApiConnectionClientFactory
{
    public const string ApprovedArtifactSha256 =
        "EB238C958A4D9F80C8A3EEACA07636AE53BC5A78A093BC3FE63923FA50A309C6";

    private readonly string artifactPath;

    public PinnedMt5NetApiConnectionClientFactory(string artifactPath)
    {
        this.artifactPath = NormalizeArtifactPath(artifactPath);
        using FileStream verification = OpenVerifiedArtifact();
    }

    public IMt5NetApiConnectionClient Create(
        ulong login,
        string password,
        string host,
        int port,
        byte[] certificatePfx,
        string certificatePassword)
    {
        using FileStream artifact = OpenVerifiedArtifact();
        Assembly assembly = AssemblyLoadContext.Default.LoadFromStream(artifact);
        Type apiType = assembly.GetType("mtapi.mt5.MT5API", throwOnError: true, ignoreCase: false)!;
        object instance = CreateVendorClient(
            apiType,
            login,
            password,
            host,
            port,
            certificatePfx,
            certificatePassword);
        return new ReflectionMt5NetApiConnectionClient(apiType, instance);
    }

    internal static object CreateVendorClient(
        Type apiType,
        ulong login,
        string password,
        string host,
        int port,
        byte[] certificatePfx,
        string certificatePassword)
    {
        ArgumentNullException.ThrowIfNull(apiType);
        ArgumentNullException.ThrowIfNull(password);
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(certificatePfx);
        ArgumentNullException.ThrowIfNull(certificatePassword);

        if (certificatePfx.Length == 0)
        {
            ConstructorInfo constructor = apiType.GetConstructor(
                [typeof(ulong), typeof(string), typeof(string), typeof(int)])
                ?? throw new MissingMethodException(
                    apiType.FullName,
                    ".ctor(ulong,string,string,int)");
            return constructor.Invoke([login, password, host, port]);
        }

        ConstructorInfo certificateConstructor = apiType.GetConstructor(
            [typeof(ulong), typeof(string), typeof(string), typeof(int), typeof(byte[]), typeof(string)])
            ?? throw new MissingMethodException(
                apiType.FullName,
                ".ctor(ulong,string,string,int,byte[],string)");
        return certificateConstructor.Invoke(
            [login, password, host, port, certificatePfx, certificatePassword]);
    }

    private FileStream OpenVerifiedArtifact()
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
            if (!string.Equals(actual, ApprovedArtifactSha256, StringComparison.Ordinal))
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

    private static string NormalizeArtifactPath(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return Path.GetFullPath(value);
    }

    private sealed class ReflectionMt5NetApiConnectionClient(
        Type apiType,
        object instance)
        : IMt5NetApiConnectionClient
    {
        public bool Connected => Read<bool>(nameof(IMt5NetApiConnectionClient.Connected));

        public ulong User => Convert.ToUInt64(
            Read<object>(nameof(IMt5NetApiConnectionClient.User)),
            CultureInfo.InvariantCulture);

        public string? AccountCompanyName =>
            Read<string?>(nameof(IMt5NetApiConnectionClient.AccountCompanyName));

        public string? AccountCurrency =>
            Read<string?>(nameof(IMt5NetApiConnectionClient.AccountCurrency));

        public object? AccountMethod =>
            Read<object?>(nameof(IMt5NetApiConnectionClient.AccountMethod));

        public void Connect() => Invoke(nameof(IMt5NetApiConnectionClient.Connect));

        public void Disconnect() => Invoke(nameof(IMt5NetApiConnectionClient.Disconnect));

        public void Dispose()
        {
            if (instance is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }

        private T Read<T>(string propertyName)
        {
            PropertyInfo property = apiType.GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public)
                ?? throw new MissingMemberException(apiType.FullName, propertyName);
            return (T)property.GetValue(instance)!;
        }

        private void Invoke(string methodName)
        {
            MethodInfo method = apiType.GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.Public,
                binder: null,
                types: Type.EmptyTypes,
                modifiers: null)
                ?? throw new MissingMethodException(apiType.FullName, methodName);
            _ = method.Invoke(instance, null);
        }
    }
}

/// <summary>
/// Vendor-specific adapter limited to authentication, bounded account identity reads,
/// and disconnection. It contains no order, history, quote, or subscription surface.
/// </summary>
public sealed class Mt5NetApiConnectionOnlyTransport : IMt5ConnectionOnlyTransport
{
    private readonly Mt5NetApiConnectionEndpoint endpoint;
    private readonly IMt5NetApiConnectionClientFactory clientFactory;
    private readonly TimeProvider timeProvider;

    public Mt5NetApiConnectionOnlyTransport(
        Mt5NetApiConnectionEndpoint endpoint,
        IMt5NetApiConnectionClientFactory clientFactory,
        TimeProvider? timeProvider = null)
    {
        this.endpoint = (endpoint ?? throw new ArgumentNullException(nameof(endpoint))).Snapshot();
        this.clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
        this.timeProvider = timeProvider ?? TimeProvider.System;
    }

    public Task<Mt5ConnectionOnlyObservation> ConnectAndDisconnectAsync(
        LocalMt5Credential credential,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(credential);
        cancellationToken.ThrowIfCancellationRequested();
        if (!string.Equals(credential.Server, endpoint.ServerName, StringComparison.Ordinal))
        {
            throw new InvalidDataException("The credential is not bound to the approved MT5 endpoint.");
        }

        return Task.FromResult(credential.UsePassword(ConnectWithPassword));

        Mt5ConnectionOnlyObservation ConnectWithPassword(ReadOnlySpan<byte> passwordUtf8)
        {
            // The vendor constructor requires System.String. This unavoidable vendor-boundary
            // copy is never logged or returned and remains scoped to this single-use worker.
            string password = Encoding.UTF8.GetString(passwordUtf8);
            IMt5NetApiConnectionClient? client = null;
            bool disconnectConfirmed = false;
            try
            {
                client = clientFactory.Create(
                    credential.Login,
                    password,
                    endpoint.Host,
                    endpoint.Port,
                    endpoint.CertificatePfx,
                    endpoint.CertificatePassword);
                client.Connect();
                if (!client.Connected || client.User != credential.Login)
                {
                    throw new InvalidDataException("The MT5 connection identity was not confirmed.");
                }

                string company = RequireText(client.AccountCompanyName, "broker company");
                string currency = RequireCurrency(client.AccountCurrency);
                BrokerAccountMode accountMode = MapAccountMode(client.AccountMethod);
                client.Disconnect();
                disconnectConfirmed = !client.Connected;
                if (!disconnectConfirmed)
                {
                    throw new InvalidDataException("The MT5 client did not confirm disconnection.");
                }

                return new Mt5ConnectionOnlyObservation(
                    company,
                    endpoint.ServerName,
                    accountMode,
                    BrokerEnvironment.Demo,
                    BrokerTradingAccess.Unknown,
                    currency,
                    true,
                    timeProvider.GetUtcNow());
            }
            finally
            {
                if (client is not null)
                {
                    if (!disconnectConfirmed)
                    {
                        try
                        {
                            client.Disconnect();
                        }
                        catch
                        {
                            // The outer process supervisor remains the hard stop for a failed
                            // synchronous vendor disconnect. Never replace the original failure.
                        }
                    }

                    client.Dispose();
                }
            }
        }
    }

    private static string RequireText(string? value, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidDataException($"The MT5 {field} observation is missing.");
        }

        return value.Trim();
    }

    private static string RequireCurrency(string? value)
    {
        string currency = RequireText(value, "account currency").ToUpperInvariant();
        if (currency.Length != 3 || currency.Any(character => character is < 'A' or > 'Z'))
        {
            throw new InvalidDataException("The MT5 account currency observation is invalid.");
        }

        return currency;
    }

    private static BrokerAccountMode MapAccountMode(object? value)
    {
        string method = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
        if (method.Contains("hedg", StringComparison.OrdinalIgnoreCase))
        {
            return BrokerAccountMode.Hedging;
        }

        if (method.Contains("net", StringComparison.OrdinalIgnoreCase))
        {
            return BrokerAccountMode.Netting;
        }

        if (method.Contains("exchange", StringComparison.OrdinalIgnoreCase))
        {
            return BrokerAccountMode.Exchange;
        }

        return BrokerAccountMode.Unknown;
    }
}
