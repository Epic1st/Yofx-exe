using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using YO4X.LocalSecrets.Windows;

namespace YO4X.Mt5.AccountInspector;

/// <summary>One connection attempt that did not produce a session.</summary>
/// <param name="Route">How the endpoint was arrived at.</param>
/// <param name="Target">What was dialled.</param>
/// <param name="ExceptionType">The exception the vendor raised.</param>
/// <param name="Message">Its message, recorded verbatim.</param>
internal sealed record ConnectAttemptFailure(
    string Route,
    string Target,
    string ExceptionType,
    string Message);

/// <summary>
/// The verified connectivity verdict for one vaulted account.
///
/// <para>
/// <see cref="Connected"/> is set only when the vendor returned from <c>Connect</c> and then
/// reported an established session. Nothing here is inferred from a server name.
/// </para>
/// </summary>
/// <param name="CredentialKey">The vault key the credential is stored under.</param>
/// <param name="Server">The broker server the credential names.</param>
/// <param name="MaskedLogin">The login, masked to its final digits.</param>
/// <param name="Resolution">How the access node was found, or why it was not.</param>
/// <param name="Host">The access node reached, when one was.</param>
/// <param name="Port">The access port reached, when one was.</param>
/// <param name="Connected">Whether a session was actually established.</param>
/// <param name="AccountGroup">The account group the broker reports.</param>
/// <param name="AccountCompanyName">The broker's name for itself.</param>
/// <param name="Currency">The deposit currency.</param>
/// <param name="Balance">Closed-trade balance.</param>
/// <param name="Equity">Balance plus floating profit.</param>
/// <param name="OpenOrderCount">Orders the broker currently holds open.</param>
/// <param name="Failures">Every attempt that failed, with the vendor's own message.</param>
internal sealed record AccountConnectivityResult(
    string CredentialKey,
    string Server,
    string MaskedLogin,
    string Resolution,
    string Host,
    int Port,
    bool Connected,
    string AccountGroup,
    string AccountCompanyName,
    string Currency,
    double Balance,
    double Equity,
    int OpenOrderCount,
    IReadOnlyList<ConnectAttemptFailure> Failures)
{
    /// <summary>
    /// Whether the broker itself states this is a demo account. A server name containing
    /// "Demo" is not evidence; the account group read from the live session is.
    /// </summary>
    public bool AccountGroupIndicatesDemo =>
        Connected && AccountGroup.Contains("demo", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Whether a strategy runner may use this account: reachable, and stated demo by the
    /// broker. An account that connects but is not demo is deliberately excluded.
    /// </summary>
    public bool SafeForDemoExecution => Connected && AccountGroupIndicatesDemo;

    /// <summary>
    /// Why there is no session, in the terms a strategy runner needs to act on.
    ///
    /// <para>
    /// A broker that answers <c>INVALID_ACCOUNT</c> was reached and understood the request —
    /// it is the credential that is dead, not the route. That is a different problem from a
    /// socket that never opened, and conflating the two would send someone hunting for an
    /// endpoint that was never missing.
    /// </para>
    /// </summary>
    public string Diagnosis
    {
        get
        {
            if (Connected)
            {
                return AccountGroupIndicatesDemo
                    ? "connected-demo"
                    : "connected-but-not-demo";
            }

            if (Failures.Count == 0)
            {
                return "not-attempted";
            }

            bool anyBrokerAnswer = Failures.Any(failure =>
                failure.ExceptionType.Contains("ServerException", StringComparison.Ordinal));
            return anyBrokerAnswer
                ? "broker-rejected-credential"
                : "endpoint-unreachable";
        }
    }

    /// <summary>The access node reached, rendered for a report.</summary>
    public string RenderTarget() => Connected && Host.Length > 0
        ? Host + ":" + Port.ToString(CultureInfo.InvariantCulture)
        : "-";
}

/// <summary>
/// Walks every credential in the local vault and establishes, one account at a time, whether
/// the broker can actually be reached and what it says about the account.
/// </summary>
internal static class ConnectivitySweep
{
    private const string VendorServerNameRoute = "vendor-server-name-resolution";
    private const string DirectoryRoute = "broker-directory-endpoint";

    /// <summary>Runs the sweep over the whole vault.</summary>
    /// <param name="vaultRoot">The vault directory to enumerate.</param>
    /// <param name="artifactPath">The pinned vendor assembly.</param>
    /// <param name="connectTimeoutMilliseconds">Per-attempt vendor connect timeout.</param>
    /// <param name="directoryAttempts">How many directory endpoints to try per account.</param>
    /// <param name="cancellationToken">Cancels the sweep.</param>
    internal static async Task<IReadOnlyList<AccountConnectivityResult>> RunAsync(
        string vaultRoot,
        string artifactPath,
        int connectTimeoutMilliseconds,
        int directoryAttempts,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(vaultRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactPath);

        Type apiType = PinnedVendorAssembly.LoadApiType(artifactPath);
        var vault = new DpapiLocalMt5CredentialVault(vaultRoot);
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

        List<string> keys = Directory
            .EnumerateFiles(vaultRoot, "*.yo4xcred", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileNameWithoutExtension)
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Select(key => key!)
            .Order(StringComparer.Ordinal)
            .ToList();

        var results = new List<AccountConnectivityResult>(keys.Count);
        foreach (string key in keys)
        {
            cancellationToken.ThrowIfCancellationRequested();
            results.Add(await InspectAsync(
                vault,
                client,
                apiType,
                key,
                connectTimeoutMilliseconds,
                directoryAttempts,
                cancellationToken).ConfigureAwait(false));
        }

        return results;
    }

    private static async Task<AccountConnectivityResult> InspectAsync(
        DpapiLocalMt5CredentialVault vault,
        HttpClient client,
        Type apiType,
        string credentialKey,
        int connectTimeoutMilliseconds,
        int directoryAttempts,
        CancellationToken cancellationToken)
    {
        using LocalMt5Credential? credential = await vault
            .OpenAsync(credentialKey, cancellationToken)
            .ConfigureAwait(false);
        if (credential is null)
        {
            return Unreachable(
                credentialKey,
                server: string.Empty,
                maskedLogin: string.Empty,
                [new ConnectAttemptFailure("vault", credentialKey, "MissingCredential", "No credential is stored under that key.")]);
        }

        LocalMt5CredentialDescriptor descriptor = credential.Describe();
        Console.WriteLine(
            $"[{credentialKey[..12]}…] {descriptor.Server} login {descriptor.MaskedLogin}");

        // Directory lookup happens before the password is unsealed: the password reader is
        // synchronous by design, and no network wait should be held open across it.
        IReadOnlyList<BrokerAccessNode> nodes = [];
        ConnectAttemptFailure? directoryFailure = null;
        try
        {
            nodes = await BrokerEndpointDirectory
                .ResolveAsync(client, descriptor.Server, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is HttpRequestException
            or TaskCanceledException
            or JsonException)
        {
            directoryFailure = new ConnectAttemptFailure(
                DirectoryRoute,
                BrokerEndpointDirectory.DescribeQuery(descriptor.Server),
                exception.GetType().Name,
                exception.Message);
        }

        List<BrokerAccessNode> candidates = nodes
            .Where(node => node.IsPubliclyRoutable())
            .Take(directoryAttempts)
            .ToList();
        Console.WriteLine(
            $"    directory: {nodes.Count} node(s) published, {candidates.Count} dialable");

        return credential.UsePassword(utf8 => Attempt(
            apiType,
            credentialKey,
            descriptor,
            credential.Login,
            Encoding.UTF8.GetString(utf8),
            candidates,
            directoryFailure,
            connectTimeoutMilliseconds));
    }

    private static AccountConnectivityResult Attempt(
        Type apiType,
        string credentialKey,
        LocalMt5CredentialDescriptor descriptor,
        ulong login,
        string password,
        IReadOnlyList<BrokerAccessNode> candidates,
        ConnectAttemptFailure? directoryFailure,
        int connectTimeoutMilliseconds)
    {
        var failures = new List<ConnectAttemptFailure>();

        AccountConnectivityResult? byName = TryRoute(
            credentialKey,
            descriptor,
            VendorServerNameRoute,
            descriptor.Server,
            () => VendorMt5Session.ForServerName(apiType, login, password, descriptor.Server),
            connectTimeoutMilliseconds,
            failures);
        if (byName is not null)
        {
            return byName with { Failures = failures };
        }

        if (directoryFailure is not null)
        {
            failures.Add(directoryFailure);
        }

        foreach (BrokerAccessNode node in candidates)
        {
            AccountConnectivityResult? byNode = TryRoute(
                credentialKey,
                descriptor,
                DirectoryRoute,
                node.ToString(),
                () => VendorMt5Session.ForEndpoint(apiType, login, password, node.Host, node.Port),
                connectTimeoutMilliseconds,
                failures);
            if (byNode is not null)
            {
                return byNode with { Failures = failures };
            }
        }

        return Unreachable(credentialKey, descriptor.Server, descriptor.MaskedLogin, failures);
    }

    /// <summary>
    /// Runs one attempt. A vendor call can fail in any number of ways — socket, timeout,
    /// authentication, or an internal vendor fault — and the point of this sweep is to record
    /// which, not to let the first one end the run, so the catch is deliberately broad and the
    /// message is kept verbatim.
    /// </summary>
    private static AccountConnectivityResult? TryRoute(
        string credentialKey,
        LocalMt5CredentialDescriptor descriptor,
        string route,
        string target,
        Func<VendorMt5Session> open,
        int connectTimeoutMilliseconds,
        List<ConnectAttemptFailure> failures)
    {
        Console.WriteLine($"    -> {route} {target}");
        VendorMt5Session? session = null;
        try
        {
            session = open();
            session.SetConnectTimeout(connectTimeoutMilliseconds);
            session.Connect();
            if (!session.Connected)
            {
                failures.Add(new ConnectAttemptFailure(
                    route,
                    target,
                    "NotConnected",
                    "Connect returned but the vendor reported no established session."));
                return null;
            }

            VendorAccountSnapshot snapshot = session.Read();
            Console.WriteLine(
                $"       connected: group '{snapshot.AccountGroup}' {snapshot.Balance.ToString("F2", CultureInfo.InvariantCulture)} {snapshot.Currency}");
            return new AccountConnectivityResult(
                credentialKey,
                descriptor.Server,
                descriptor.MaskedLogin,
                route,
                snapshot.Host.Length > 0 ? snapshot.Host : target,
                snapshot.Port,
                Connected: true,
                snapshot.AccountGroup,
                snapshot.Company,
                snapshot.Currency,
                snapshot.Balance,
                snapshot.Equity,
                snapshot.OpenOrderCount,
                []);
        }
#pragma warning disable CA1031 // Every failure mode is evidence here and must be recorded, not thrown.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            Console.WriteLine($"       failed: {exception.GetType().Name}: {exception.Message}");
            failures.Add(new ConnectAttemptFailure(
                route,
                target,
                exception.GetType().Name,
                exception.Message));
            return null;
        }
        finally
        {
            session?.Dispose();
        }
    }

    private static AccountConnectivityResult Unreachable(
        string credentialKey,
        string server,
        string maskedLogin,
        IReadOnlyList<ConnectAttemptFailure> failures) =>
        new(
            credentialKey,
            server,
            maskedLogin,
            Resolution: "unresolved",
            Host: string.Empty,
            Port: 0,
            Connected: false,
            AccountGroup: string.Empty,
            AccountCompanyName: string.Empty,
            Currency: string.Empty,
            Balance: 0d,
            Equity: 0d,
            OpenOrderCount: 0,
            failures);

    /// <summary>Renders the sweep as canonical JSON evidence.</summary>
    /// <param name="results">The sweep results.</param>
    /// <param name="vendorArtifactSha256">The vendor artifact the sweep ran against.</param>
    /// <param name="observedAtUtc">When the sweep ran.</param>
    internal static string RenderJson(
        IReadOnlyList<AccountConnectivityResult> results,
        string vendorArtifactSha256,
        DateTimeOffset observedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(results);

        var accounts = new JsonArray();
        foreach (AccountConnectivityResult result in results.OrderBy(item => item.CredentialKey, StringComparer.Ordinal))
        {
            var failures = new JsonArray();
            foreach (ConnectAttemptFailure failure in result.Failures)
            {
                failures.Add(new JsonObject
                {
                    ["route"] = failure.Route,
                    ["target"] = failure.Target,
                    ["exceptionType"] = failure.ExceptionType,
                    ["message"] = failure.Message
                });
            }

            accounts.Add(new JsonObject
            {
                ["credentialKey"] = result.CredentialKey,
                ["server"] = result.Server,
                ["maskedLogin"] = result.MaskedLogin,
                ["endpointResolution"] = result.Resolution,
                ["diagnosis"] = result.Diagnosis,
                ["host"] = result.Host.Length > 0 ? result.Host : null,
                ["port"] = result.Port > 0 ? result.Port : null,
                ["connected"] = result.Connected,
                ["accountGroup"] = result.Connected ? result.AccountGroup : null,
                ["accountGroupIndicatesDemo"] = result.AccountGroupIndicatesDemo,
                ["safeForDemoExecution"] = result.SafeForDemoExecution,
                ["accountCompanyName"] = result.Connected ? result.AccountCompanyName : null,
                ["currency"] = result.Connected ? result.Currency : null,
                ["balance"] = result.Connected ? result.Balance : null,
                ["equity"] = result.Connected ? result.Equity : null,
                ["openOrderCount"] = result.Connected ? result.OpenOrderCount : null,
                ["failedAttempts"] = failures
            });
        }

        var document = new JsonObject
        {
            ["schemaVersion"] = "yo4x.mt5.demo-account-connectivity.v1",
            ["evidenceAuthority"] = "unsigned-local-observation",
            ["observedAtUtc"] = observedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            ["vendorArtifactSha256"] = vendorArtifactSha256.ToLowerInvariant(),
            ["endpointDiscoverySource"] = "https://search.mtapi.io/Search?company=<server>&mt5=true",
            ["accountsInspected"] = results.Count,
            ["accountsConnected"] = results.Count(item => item.Connected),
            ["accountsSafeForDemoExecution"] = results.Count(item => item.SafeForDemoExecution),
            ["ordersSent"] = 0,
            ["accounts"] = accounts
        };

        string json = document.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });
        return json.ReplaceLineEndings("\n") + "\n";
    }

    /// <summary>Renders the sweep as a table for a human reader.</summary>
    /// <param name="results">The sweep results.</param>
    internal static string RenderTable(IReadOnlyList<AccountConnectivityResult> results)
    {
        ArgumentNullException.ThrowIfNull(results);

        var builder = new StringBuilder();
        builder.AppendLine(
            "key        server                     login     conn group  company                   ccy    balance     equity ord  endpoint");
        builder.AppendLine(new string('-', 178));
        foreach (AccountConnectivityResult result in results.OrderBy(item => item.Server, StringComparer.Ordinal)
            .ThenBy(item => item.CredentialKey, StringComparer.Ordinal))
        {
            builder.Append(CultureInfo.InvariantCulture, $"{result.CredentialKey[..10]} ");
            builder.Append(CultureInfo.InvariantCulture, $"{Fit(result.Server, 26)} ");
            builder.Append(CultureInfo.InvariantCulture, $"{Fit(result.MaskedLogin, 9)} ");
            builder.Append(CultureInfo.InvariantCulture, $"{(result.Connected ? "YES" : "no ")} ");
            builder.Append(CultureInfo.InvariantCulture, $"{Fit(result.Connected ? result.AccountGroup : "-", 6)} ");
            builder.Append(CultureInfo.InvariantCulture, $"{Fit(result.Connected ? result.AccountCompanyName : "-", 25)} ");
            builder.Append(CultureInfo.InvariantCulture, $"{Fit(result.Connected ? result.Currency : "-", 4)} ");
            builder.Append(CultureInfo.InvariantCulture, $"{(result.Connected ? result.Balance.ToString("N2", CultureInfo.InvariantCulture) : "-"),10} ");
            builder.Append(CultureInfo.InvariantCulture, $"{(result.Connected ? result.Equity.ToString("N2", CultureInfo.InvariantCulture) : "-"),10} ");
            builder.Append(CultureInfo.InvariantCulture, $"{(result.Connected ? result.OpenOrderCount.ToString(CultureInfo.InvariantCulture) : "-"),3}  ");
            builder.AppendLine(CultureInfo.InvariantCulture,
                $"{(result.Connected ? result.RenderTarget() : result.Diagnosis)}");
        }

        return builder.ToString();
    }

    private static string Fit(string value, int width) =>
        (value.Length > width ? value[..width] : value).PadRight(width);
}
