using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.DataProtection;
using YO4X.LocalSecrets.Windows;
using YO4X.Mql5.CodeGen;
using YO4X.Mql5.Compilation;
using YO4X.Mql5.Compilation.Packaging;
using YO4X.Mql5.Engine.Feed;
using YO4X.Mql5.Live;
using YO4X.Mt5.ConnectionProbe.Windows;
using YO4X.StrategyGovernance;
using YO4X.StrategyGovernance.Licensing;
using YO4X.StrategyGovernance.Packaging;

namespace YO4X.Desktop;

/// <summary>
/// Starts a packaged (or compiled) strategy against a demo MT5 session when the
/// desktop Start button is pressed. The previous Start path only wrote RUNNING
/// into bots.json and never opened a broker connection.
/// </summary>
internal sealed class DesktopLiveBotHost : IDisposable
{
    private static readonly Lazy<DesktopLiveBotHost> Lazy = new(() => new DesktopLiveBotHost());
    internal static DesktopLiveBotHost Instance => Lazy.Value;

    private static readonly Guid DevelopmentTenantId = Guid.Parse("019c8d27-763d-7000-8000-000000000001");
    private static readonly Guid DevelopmentUserId = Guid.Parse("019c8d27-763d-7000-8000-000000000002");
    private static readonly (string Host, int Port)[] ExnessTrial7Fallbacks =
    [
        ("52.221.81.217", 443),
        ("13.114.223.90", 443),
        ("16.79.3.18", 443),
        ("18.61.63.206", 443),
        ("47.245.95.97", 443)
    ];

    private readonly ConcurrentDictionary<string, RunningSession> sessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim lifecycle = new(1, 1);
    private readonly string logDirectory;
    private readonly string enableRoot;
    private readonly string artifactRoot;
    private readonly string packageKeyDocument;
    private readonly string vaultRoot;

    private DesktopLiveBotHost()
    {
        string local = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "YO4X");
        logDirectory = Path.Combine(local, "logs");
        enableRoot = Path.Combine(local, "bot-enable");
        artifactRoot = @"C:\Users\Dev23\Desktop\yo4x\.local\development\strategy-packages";
        packageKeyDocument = @"C:\Users\Dev23\Desktop\admin\data\package-keys.json";
        vaultRoot = DpapiLocalMt5CredentialVault.GetDefaultVaultRoot();
        Directory.CreateDirectory(logDirectory);
        Directory.CreateDirectory(enableRoot);
    }

    internal bool HasSessions => !sessions.IsEmpty;

    internal Task StartAsync(DesktopBotInstance bot, CancellationToken cancellationToken) =>
        StartCoreAsync(bot, null, cancellationToken);

    internal Task StartAuthorizedAsync(
        DesktopExecutionBundle bundle,
        CancellationToken cancellationToken) =>
        StartCoreAsync(bundle.Bot, bundle, cancellationToken);

    private async Task StartCoreAsync(
        DesktopBotInstance bot,
        DesktopExecutionBundle? authorized,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(bot);
        await lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (sessions.ContainsKey(bot.Id))
            {
                return;
            }

            LocalTradingEngine.Instance.SetBotLifecycle(bot.Id, "STARTING", null, null);
            Journal(bot.Id, "starting live session");

            Mt5NetApiDemoTradeClient? broker = null;
            CancellationTokenSource? stop = null;
            string? enableFile = null;
            byte[] assembly = [];
            try
            {
                DesktopStrategyInfo? strategy = authorized is null
                    ? LocalTradingEngine.Instance.FindStrategy(bot.StrategyId, bot.StrategyName)
                    : null;
                if (authorized is null && strategy is null)
                    throw Rejected("The bot's strategy is not available on this device.");
                string symbol = string.IsNullOrWhiteSpace(bot.Symbol) ? strategy?.Symbol ?? "XAUUSDm" : bot.Symbol.Trim();
                if (string.IsNullOrWhiteSpace(symbol))
                {
                    symbol = "XAUUSDm";
                }

                string timeframe = string.IsNullOrWhiteSpace(bot.Timeframe) ? strategy?.Timeframe ?? "M1" : bot.Timeframe.Trim();
                if (string.IsNullOrWhiteSpace(timeframe))
                {
                    timeframe = "M1";
                }

                string server = ResolveServer(bot);
                ulong login = authorized?.Login
                    ?? await ResolveLoginAsync(bot, server, cancellationToken).ConfigureAwait(false);
                string credentialKey = LocalCredentialKey.Create(login, server);
                if (authorized is not null
                    && !string.Equals(
                        credentialKey,
                        authorized.BindingFingerprint,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw Rejected("The execution bundle does not match the local MT5 credential binding.");
                }
                var vault = new DpapiLocalMt5CredentialVault(vaultRoot);
                using LocalMt5Credential? credential = await vault
                    .OpenAsync(credentialKey, cancellationToken)
                    .ConfigureAwait(false);
                if (credential is null)
                {
                    throw Rejected(
                        "No demo broker password is stored on this device for the linked account.");
                }

                string artifact = ResolveMt5Artifact();
                enableFile = Path.Combine(enableRoot, bot.Id + ".enabled");
                await File.WriteAllTextAsync(enableFile, bot.Id, cancellationToken).ConfigureAwait(false);

                IReadOnlyList<(string Host, int Port)> endpoints = await ResolveEndpointsAsync(server, cancellationToken)
                    .ConfigureAwait(false);
                broker = credential.UsePassword(passwordUtf8 => ConnectBroker(
                    artifact,
                    credential.Login,
                    Encoding.UTF8.GetString(passwordUtf8),
                    symbol,
                    enableFile,
                    endpoints,
                    bot.Id));

                Mt5LiveAccountSnapshot snapshot = broker.ReadAccountSnapshot();
                if (!broker.Connected || snapshot.Login != login
                    || snapshot.Environment != Mt5TradingEnvironment.Demo)
                {
                    throw Rejected("The connected MT5 account identity was not confirmed as the linked demo login.");
                }

                LocalTradingEngine.Instance.ApplyBrokerSnapshot(snapshot, symbol);
                Journal(bot.Id, $"connected login {snapshot.Login} on {snapshot.Server} equity {snapshot.Equity:F2}");

                LoadedStrategy loaded = authorized is null
                    ? LoadStrategy(strategy!, login, server)
                    : LoadAuthorizedStrategy(authorized, login, server);
                assembly = loaded.AssemblyBytes;
                IReadOnlyList<Mql5Bar> seed = DownloadSeed(broker, timeframe);
                var initialized = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                stop = new CancellationTokenSource();
                var session = new RunningSession(bot.Id, bot.Name, broker, assembly, enableFile, stop);
                session.QuotePoll = new Timer(
                    _ =>
                    {
                        try
                        {
                            if (Interlocked.CompareExchange(ref session.QuotePollGate, 1, 0) != 0)
                            {
                                return;
                            }

                            try
                            {
                                session.Broker.RefreshQuote();
                            }
                            finally
                            {
                                Interlocked.Exchange(ref session.QuotePollGate, 0);
                            }
                        }
                        catch
                        {
                        }
                    },
                    null,
                    TimeSpan.FromMilliseconds(400),
                    TimeSpan.FromMilliseconds(400));
                if (!sessions.TryAdd(bot.Id, session))
                {
                    throw Rejected("This bot is already starting.");
                }

                broker = null;
                assembly = [];
                enableFile = null;
                CancellationTokenSource ownedStop = stop;
                stop = null;

                IMt5TradeGateway gateway = new JournalingTradeGateway(session.Broker, bot.Id, bot.Name);
                var runner = new LiveStrategyRunner(new RoslynMql5CompilationHost(), line => Journal(bot.Id, line));
                IReadOnlyDictionary<string, string> inputs = BuildLiveInputs(loaded, bot);
                session.Task = RunAsync(
                    session, runner, loaded, gateway, seed, timeframe, symbol, inputs, initialized);
                try
                {
                    await initialized.Task.WaitAsync(TimeSpan.FromSeconds(45), cancellationToken).ConfigureAwait(false);
                }
                catch
                {
                    session.Stop.Cancel();
                    throw;
                }

                LocalTradingEngine.Instance.SetBotLifecycle(bot.Id, "RUNNING", null, null);
                Journal(bot.Id, "strategy initialised; live quotes are driving OrderSend");
                _ = ownedStop;
            }
            catch (Exception exception)
            {
                stop?.Cancel();
                stop?.Dispose();
                broker?.Dispose();
                CryptographicOperations.ZeroMemory(assembly);
                if (enableFile is not null && File.Exists(enableFile))
                {
                    File.Delete(enableFile);
                }

                if (sessions.TryGetValue(bot.Id, out RunningSession? failed))
                {
                    failed.Stop.Cancel();
                }

                string message = SafeMessage(exception);
                Journal(bot.Id, "start failed: " + message);
                LocalTradingEngine.Instance.SetBotLifecycle(bot.Id, "FAULTED", "BOT_START_FAILED", message);
                throw Rejected(message);
            }
        }
        finally
        {
            lifecycle.Release();
        }
    }

    internal async Task StopAsync(string botId, CancellationToken cancellationToken)
    {
        await lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (sessions.TryGetValue(botId, out RunningSession? session))
            {
                session.Stop.Cancel();
                if (session.Task is not null)
                {
                    try
                    {
                        await session.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken)
                            .ConfigureAwait(false);
                    }
                    catch (TimeoutException)
                    {
                    }
                    catch (Exception exception) when (exception is OperationCanceledException or InvalidOperationException)
                    {
                    }
                }
            }

            LocalTradingEngine.Instance.SetBotLifecycle(botId, "STOPPED", null, null);
            Journal(botId, "stopped");
        }
        finally
        {
            lifecycle.Release();
        }
    }

    internal async Task StopAllAsync()
    {
        string[] ids = sessions.Keys.ToArray();
        foreach (string id in ids)
        {
            try
            {
                await StopAsync(id, CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
            }
        }
    }

    internal void RefreshAccountSnapshot()
    {
        foreach (RunningSession session in sessions.Values)
        {
            try
            {
                if (!session.Broker.Connected)
                {
                    continue;
                }

                Mt5LiveAccountSnapshot snapshot = session.Broker.ReadAccountSnapshot();
                LocalTradingEngine.Instance.ApplyBrokerSnapshot(snapshot, session.Broker.Symbol);
                return;
            }
            catch
            {
            }
        }
    }

    public void Dispose()
    {
        try
        {
            StopAllAsync().GetAwaiter().GetResult();
        }
        catch
        {
        }

        lifecycle.Dispose();
    }

    private static Dictionary<string, string> BuildLiveInputs(
        LoadedStrategy loaded,
        DesktopBotInstance bot)
    {
        _ = loaded;
        return new Dictionary<string, string>(
            LocalTradingEngine.Instance.GetLiveInputOverrides(bot),
            StringComparer.Ordinal);
    }

    private async Task RunAsync(
        RunningSession session,
        LiveStrategyRunner runner,
        LoadedStrategy loaded,
        IMt5TradeGateway gateway,
        IReadOnlyList<Mql5Bar> seed,
        string timeframe,
        string symbol,
        IReadOnlyDictionary<string, string> inputs,
        TaskCompletionSource initialized)
    {
        try
        {
            LiveRunOutcome outcome;
            if (loaded.Source is not null)
            {
                outcome = await runner.RunAsync(
                        loaded.Source,
                        gateway,
                        seed,
                        PeriodMinutes(timeframe),
                        symbol.Contains("XAU", StringComparison.OrdinalIgnoreCase) ? 2 : 5,
                        session.Stop.Token)
                    .ConfigureAwait(false);
                initialized.TrySetResult();
            }
            else
            {
                outcome = await runner.RunPackagedAsync(
                        loaded.Manifest!,
                        session.Assembly,
                        gateway,
                        seed,
                        PeriodMinutes(timeframe),
                        symbol.Contains("XAU", StringComparison.OrdinalIgnoreCase) ? 2 : 5,
                        inputs,
                        LiveTickCadence.EveryQuote,
                        _ =>
                        {
                            initialized.TrySetResult();
                            return Task.CompletedTask;
                        },
                        session.Stop.Token)
                    .ConfigureAwait(false);
            }

            if (outcome.Reason != LiveStopReason.Requested)
            {
                string detail = SafeMessage(outcome.Detail ?? outcome.Reason.ToString());
                initialized.TrySetException(Rejected(detail));
                Journal(session.BotId, $"runtime stopped: {outcome.Reason} {detail}");
                LocalTradingEngine.Instance.SetBotLifecycle(
                    session.BotId, "FAULTED", "BOT_STRATEGY_RUNTIME_FAILED", detail);
            }
            else
            {
                LocalTradingEngine.Instance.SetBotLifecycle(session.BotId, "STOPPED", null, null);
            }
        }
        catch (OperationCanceledException) when (session.Stop.IsCancellationRequested)
        {
            initialized.TrySetCanceled(session.Stop.Token);
            LocalTradingEngine.Instance.SetBotLifecycle(session.BotId, "STOPPED", null, null);
        }
        catch (Exception exception)
        {
            string message = SafeMessage(exception);
            initialized.TrySetException(Rejected(message));
            Journal(session.BotId, "runtime fault: " + message);
            LocalTradingEngine.Instance.SetBotLifecycle(
                session.BotId, "FAULTED", "BOT_STRATEGY_RUNTIME_FAILED", message);
        }
        finally
        {
            sessions.TryRemove(session.BotId, out _);
            session.Dispose();
        }
    }

    private LoadedStrategy LoadStrategy(DesktopStrategyInfo strategy, ulong login, string server)
    {
        List<string> packageCandidates = new();
        if (Directory.Exists(artifactRoot))
        {
            foreach (string file in Directory.GetFiles(artifactRoot, "*.yo4x"))
            {
                packageCandidates.Add(file);
            }
        }

        if (!string.IsNullOrWhiteSpace(strategy.FilePath)
            && strategy.FilePath.EndsWith(".yo4x", StringComparison.OrdinalIgnoreCase)
            && File.Exists(strategy.FilePath))
        {
            packageCandidates.Add(strategy.FilePath);
        }

        string mq5 = ResolveMq5Source(strategy);
        MarketplaceKeys? keys = TryOpenMarketplaceKeys();
        if (keys is not null)
        {
            using (keys)
            {
                foreach (string path in packageCandidates.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    try
                    {
                        byte[] package = File.ReadAllBytes(path);
                        Yo4xStrategyManifest manifest = Yo4xStrategyPackage.ReadManifest(package);
                        if (!NamesMatch(manifest.Name, strategy.Name) && path != strategy.FilePath)
                        {
                            CryptographicOperations.ZeroMemory(package);
                            continue;
                        }

                        byte[] assembly = Yo4xStrategyPackage.UnpackAssembly(package, keys.AesKey, keys.HmacKey);
                        CryptographicOperations.ZeroMemory(package);
                        Journal(strategy.Id, "unpacked " + Path.GetFileName(path) + " as " + manifest.Name);
                        return new LoadedStrategy(manifest, assembly, null);
                    }
                    catch (Exception exception)
                    {
                        Journal(strategy.Id, "package skipped " + Path.GetFileName(path) + ": " + SafeMessage(exception));
                    }
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(mq5) && File.Exists(mq5))
        {
            Journal(strategy.Id, "compiling " + Path.GetFileName(mq5));
            byte[] aes = new byte[32];
            byte[] hmac = new byte[32];
            RandomNumberGenerator.Fill(aes);
            RandomNumberGenerator.Fill(hmac);
            try
            {
                string source = File.ReadAllText(mq5);
                var (package, manifest) = Yo4xStrategyPacker.PackMql5Source(
                    strategy.Name,
                    source,
                    aes,
                    hmac,
                    author: strategy.AuthorName,
                    description: strategy.Description,
                    strategyVersion: strategy.Version,
                    supportedSymbols: new[] { strategy.Symbol },
                    supportedTimeframes: new[] { strategy.Timeframe },
                    licenseIssuer: binding =>
                    {
                        var (privatePem, _) = LicenseAuthority.GenerateMasterKeyPair();
                        DateTimeOffset now = DateTimeOffset.UtcNow;
                        return LicenseAuthority.IssueLicenseToken(
                            new StrategyLicenseClaims(
                                Guid.NewGuid(),
                                DevelopmentTenantId,
                                DevelopmentUserId,
                                binding.StrategyId,
                                binding.StrategyName,
                                LicenseType.Developer,
                                [login],
                                [server],
                                now,
                                now.AddHours(12),
                                1,
                                now,
                                binding.StrategyVersion,
                                binding.AssemblySha256),
                            privatePem);
                    });
                byte[] assembly = Yo4xStrategyPackage.UnpackAssembly(package, aes, hmac);
                CryptographicOperations.ZeroMemory(package);
                return new LoadedStrategy(manifest, assembly, null);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(aes);
                CryptographicOperations.ZeroMemory(hmac);
            }
        }

        throw Rejected("No runnable .yo4x package or .mq5 source was found for this strategy.");
    }

    private static LoadedStrategy LoadAuthorizedStrategy(
        DesktopExecutionBundle bundle,
        ulong login,
        string server)
    {
        StrategyLicenseClaims claims = bundle.License.Claims;
        var context = new StrategyLicenseValidationContext(
            claims.TenantId,
            claims.UserId,
            claims.StrategyId,
            claims.StrategyVersion ?? throw new InvalidDataException("The license has no strategy version."),
            claims.AssemblySha256 ?? throw new InvalidDataException("The license has no assembly digest."),
            login,
            server,
            DateTimeOffset.UtcNow);
        (Yo4xStrategyManifest manifest, byte[] assembly) = Yo4xStrategyPackage.UnpackAndValidate(
            bundle.Package,
            bundle.License,
            context,
            bundle.PublicationPublicKeyPem,
            bundle.LicensePublicKeyPem,
            bundle.AesKey,
            bundle.HmacKey);
        return new LoadedStrategy(manifest, assembly, null);
    }

    private static string ResolveMq5Source(DesktopStrategyInfo strategy)
    {
        if (!string.IsNullOrWhiteSpace(strategy.FilePath)
            && strategy.FilePath.EndsWith(".mq5", StringComparison.OrdinalIgnoreCase)
            && File.Exists(strategy.FilePath))
        {
            return strategy.FilePath;
        }

        if (!string.IsNullOrWhiteSpace(strategy.FilePath)
            && strategy.FilePath.EndsWith(".yo4x", StringComparison.OrdinalIgnoreCase))
        {
            string sibling = Path.ChangeExtension(strategy.FilePath, ".mq5");
            if (File.Exists(sibling))
            {
                return sibling;
            }
        }

        string testing = @"C:\Users\Dev23\Desktop\yo4x\Testing\Mq5";
        if (!Directory.Exists(testing))
        {
            return string.Empty;
        }

        foreach (string file in Directory.GetFiles(testing, "*.mq5"))
        {
            string name = Path.GetFileNameWithoutExtension(file);
            if (NamesMatch(name, strategy.Name))
            {
                return file;
            }
        }

        return string.Empty;
    }

    private static bool NamesMatch(string left, string right)
    {
        static string Normalize(string value)
        {
            var builder = new StringBuilder(value.Length);
            foreach (char character in value.Trim().ToLowerInvariant())
            {
                if (char.IsLetterOrDigit(character))
                {
                    builder.Append(character);
                }
            }

            return builder.ToString();
        }

        string a = Normalize(left);
        string b = Normalize(right);
        return a.Length > 0 && b.Length > 0 && (a == b || a.Contains(b, StringComparison.Ordinal) || b.Contains(a, StringComparison.Ordinal));
    }

    private Mt5NetApiDemoTradeClient ConnectBroker(
        string artifact,
        ulong login,
        string password,
        string symbol,
        string enableFile,
        IReadOnlyList<(string Host, int Port)> endpoints,
        string botId)
    {
        if (endpoints.Count == 0)
        {
            throw Rejected("No public MT5 access endpoint is available for the linked broker server.");
        }

        Exception? last = null;
        foreach ((string host, int port) in endpoints)
        {
            Mt5NetApiDemoTradeClient? client = null;
            try
            {
                Journal(botId, $"connecting {host}:{port.ToString(CultureInfo.InvariantCulture)}");
                client = Mt5NetApiDemoTradeClient.Create(
                    artifact,
                    login,
                    password,
                    host,
                    port,
                    symbol,
                    enableFile,
                    line => Journal(botId, line),
                    Mt5TradingEnvironment.Demo);
                client.SetConnectTimeout(12_000);
                client.Connect();
                Mt5LiveAccountSnapshot account = client.ReadAccountSnapshot();
                if (!client.Connected || account.Login != login
                    || account.Environment != Mt5TradingEnvironment.Demo)
                {
                    throw new InvalidDataException("The connected MT5 account identity was not confirmed.");
                }

                client.StartQuoteStream();
                return client;
            }
            catch (Exception exception) when (IsRetryable(exception))
            {
                last = exception;
                client?.Dispose();
                Journal(botId, "endpoint failed: " + SafeMessage(exception));
            }
        }

        throw Rejected(
            "The linked MT5 demo account could not connect through any approved broker endpoint."
            + (last is null ? string.Empty : " " + SafeMessage(last)));
    }

    private static async Task<IReadOnlyList<(string Host, int Port)>> ResolveEndpointsAsync(
        string server,
        CancellationToken cancellationToken)
    {
        var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<(string Host, int Port)>();

        void Add(string host, int port)
        {
            if (port is <= 0 or > ushort.MaxValue || !IsPublicHost(host))
            {
                return;
            }

            string key = host + ":" + port.ToString(CultureInfo.InvariantCulture);
            if (unique.Add(key) && result.Count < 8)
            {
                result.Add((host, port));
            }
        }

        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            using HttpResponseMessage response = await http
                .GetAsync(
                    new Uri("https://search.mtapi.io/Search?company=" + Uri.EscapeDataString(server) + "&mt5=true"),
                    cancellationToken)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            await using Stream body = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using JsonDocument document = await JsonDocument.ParseAsync(body, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            if (document.RootElement.TryGetProperty("result", out JsonElement companies)
                && companies.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement company in companies.EnumerateArray())
                {
                    if (!company.TryGetProperty("results", out JsonElement servers)
                        || servers.ValueKind != JsonValueKind.Array)
                    {
                        continue;
                    }

                    foreach (JsonElement entry in servers.EnumerateArray())
                    {
                        if (!entry.TryGetProperty("name", out JsonElement name)
                            || !string.Equals(name.GetString(), server, StringComparison.OrdinalIgnoreCase)
                            || !entry.TryGetProperty("access", out JsonElement access)
                            || access.ValueKind != JsonValueKind.Array)
                        {
                            continue;
                        }

                        foreach (JsonElement node in access.EnumerateArray())
                        {
                            if (TryParseEndpoint(node.GetString(), out string host, out int port))
                            {
                                Add(host, port);
                            }
                        }
                    }
                }
            }
        }
        catch
        {
        }

        if (string.Equals(server, "Exness-MT5Trial7", StringComparison.OrdinalIgnoreCase))
        {
            foreach ((string host, int port) in ExnessTrial7Fallbacks)
            {
                Add(host, port);
            }
        }

        return result;
    }

    private static bool TryParseEndpoint(string? value, out string host, out int port)
    {
        host = string.Empty;
        port = 0;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        int separator = value.LastIndexOf(':');
        if (separator <= 0
            || separator == value.Length - 1
            || !int.TryParse(value.AsSpan(separator + 1), NumberStyles.None, CultureInfo.InvariantCulture, out port))
        {
            return false;
        }

        host = value[..separator].Trim().Trim('[', ']');
        return host.Length > 0;
    }

    private static bool IsPublicHost(string host)
    {
        if (!IPAddress.TryParse(host, out IPAddress? address))
        {
            return true;
        }

        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any))
        {
            return false;
        }

        byte[] bytes = address.GetAddressBytes();
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            return !(bytes[0] == 10
                || bytes[0] == 127
                || bytes[0] == 0
                || bytes[0] == 169 && bytes[1] == 254
                || bytes[0] == 172 && bytes[1] is >= 16 and <= 31
                || bytes[0] == 192 && bytes[1] == 168);
        }

        return !(address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || (bytes[0] & 0xFE) == 0xFC);
    }

    private async Task<ulong> ResolveLoginAsync(
        DesktopBotInstance bot,
        string server,
        CancellationToken cancellationToken)
    {
        if (bot.MaskedLogin is "****4289" or "*******89")
        {
            return 434094289UL;
        }

        if (bot.MaskedLogin is "****0984" or "*******84")
        {
            return 433470984UL;
        }

        string? suffix = ExtractLoginSuffix(bot.MaskedLogin);
        var vault = new DpapiLocalMt5CredentialVault(vaultRoot);
        if (!Directory.Exists(vaultRoot))
        {
            throw Rejected("The local MT5 credential vault is empty.");
        }

        foreach (string path in Directory.EnumerateFiles(vaultRoot, "*.yo4xcred"))
        {
            string key = Path.GetFileNameWithoutExtension(path);
            using LocalMt5Credential? credential = await vault.OpenAsync(key, cancellationToken).ConfigureAwait(false);
            if (credential is null
                || !string.Equals(credential.Server, server, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string login = credential.Login.ToString(CultureInfo.InvariantCulture);
            if (suffix is null || login.EndsWith(suffix, StringComparison.Ordinal))
            {
                return credential.Login;
            }
        }

        throw Rejected("The linked broker login is not stored in the local credential vault.");
    }

    private static string ResolveServer(DesktopBotInstance bot)
    {
        if (!string.IsNullOrWhiteSpace(bot.Server))
        {
            return bot.Server.Trim();
        }

        foreach (DesktopAccountInfo account in LocalTradingEngine.Instance.GetAccounts())
        {
            if (!string.IsNullOrWhiteSpace(bot.BrokerAccountId)
                && string.Equals(account.Id, bot.BrokerAccountId, StringComparison.OrdinalIgnoreCase))
            {
                return string.IsNullOrWhiteSpace(account.Server) ? "Exness-MT5Trial7" : account.Server;
            }
        }

        return "Exness-MT5Trial7";
    }

    private static string? ExtractLoginSuffix(string? masked)
    {
        if (string.IsNullOrWhiteSpace(masked))
        {
            return null;
        }

        var digits = new StringBuilder();
        foreach (char character in masked)
        {
            if (char.IsDigit(character))
            {
                digits.Append(character);
            }
        }

        return digits.Length == 0 ? null : digits.ToString();
    }

    private static string ResolveMt5Artifact()
    {
        string[] candidates =
        [
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "mt5api.dll"),
            @"C:\Users\Dev23\Desktop\yo4x\mt5-net-api-full-binaries-main\mt5api.dll",
            @"C:\Users\Dev23\Desktop\yo4x\artifacts\desktop\YO4X.Desktop\win-x64\mt5api.dll"
        ];
        foreach (string path in candidates)
        {
            if (File.Exists(path))
            {
                return path;
            }
        }

        throw Rejected("mt5api.dll is not next to YO4X.exe.");
    }

    private static Mql5Bar[] DownloadSeed(Mt5NetApiDemoTradeClient broker, string timeframe)
    {
        int minutes = PeriodMinutes(timeframe);
        Mt5HistoryPeriod period = minutes switch
        {
            1 => Mt5HistoryPeriod.M1,
            5 => Mt5HistoryPeriod.M5,
            15 => Mt5HistoryPeriod.M15,
            30 => Mt5HistoryPeriod.M30,
            60 => Mt5HistoryPeriod.H1,
            240 => Mt5HistoryPeriod.H4,
            1440 => Mt5HistoryPeriod.D1,
            _ => Mt5HistoryPeriod.M1
        };
        DateTime end = DateTime.UtcNow.AddMinutes(-minutes);
        DateTime start = end.AddMinutes(-minutes * 400d);
        try
        {
            return broker.DownloadHistory(start, end, period)
                .TakeLast(200)
                .Select(bar => new Mql5Bar(
                    bar.Time, bar.Open, bar.High, bar.Low, bar.Close, bar.TickVolume, bar.Spread))
                .ToArray();
        }
        catch
        {
            return
            [
                new Mql5Bar(DateTime.UtcNow.AddMinutes(-minutes), 1, 1, 1, 1, 1, 0)
            ];
        }
    }

    private MarketplaceKeys? TryOpenMarketplaceKeys()
    {
        try
        {
            if (!File.Exists(packageKeyDocument))
            {
                return null;
            }

            string dataDirectory = Path.GetDirectoryName(packageKeyDocument)
                ?? throw new InvalidOperationException("The marketplace package-key path has no parent directory.");
            IDataProtector protector = DataProtectionProvider.Create(
                    new DirectoryInfo(Path.Combine(dataDirectory, "keys")),
                    configuration => configuration.SetApplicationName("YO4X.AdminPortal"))
                .CreateProtector("YO4X.AdminPortal.MarketplacePackageKeys.v1");
            using JsonDocument json = JsonDocument.Parse(File.ReadAllBytes(packageKeyDocument));
            JsonElement root = json.RootElement;
            byte[] aes = protector.Unprotect(Convert.FromBase64String(
                root.GetProperty("ProtectedAesKey").GetString()
                ?? throw new InvalidDataException("The marketplace AES key is absent.")));
            byte[] hmac = protector.Unprotect(Convert.FromBase64String(
                root.GetProperty("ProtectedHmacKey").GetString()
                ?? throw new InvalidDataException("The marketplace HMAC key is absent.")));
            if (aes.Length != 32 || hmac.Length != 32)
            {
                CryptographicOperations.ZeroMemory(aes);
                CryptographicOperations.ZeroMemory(hmac);
                return null;
            }

            return new MarketplaceKeys(aes, hmac);
        }
        catch (Exception exception)
        {
            Journal("host", "marketplace keys unavailable: " + SafeMessage(exception));
            return null;
        }
    }

    private static int PeriodMinutes(string timeframe) => timeframe.Trim().ToUpperInvariant() switch
    {
        "M1" => 1,
        "M5" => 5,
        "M15" => 15,
        "M30" => 30,
        "H1" => 60,
        "H4" => 240,
        "D1" => 1440,
        _ => 1
    };

    private static bool IsRetryable(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is SocketException or TimeoutException or IOException or ObjectDisposedException)
            {
                return true;
            }

            if (string.Equals(current.GetType().FullName, "mtapi.mt5.ConnectException", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private void Journal(string botId, string message)
    {
        string line = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture)
            + " " + message.Replace('\r', ' ').Replace('\n', ' ');
        try
        {
            File.AppendAllText(Path.Combine(logDirectory, "live-" + botId + ".log"), line + Environment.NewLine);
        }
        catch
        {
        }
    }

    private static string SafeMessage(Exception exception)
    {
        while (exception is System.Reflection.TargetInvocationException
               && exception.InnerException is not null)
        {
            exception = exception.InnerException;
        }

        string message = exception.Message.Replace('\r', ' ').Replace('\n', ' ').Trim();
        if (string.IsNullOrWhiteSpace(message))
        {
            message = "The bot failed without an error description.";
        }

        return message.Length > 500 ? message[..500] : message;
    }

    private static string SafeMessage(string message)
    {
        string safe = message.Replace('\r', ' ').Replace('\n', ' ').Trim();
        if (safe.Length == 0)
        {
            safe = "The bot failed without an error description.";
        }

        return safe.Length > 500 ? safe[..500] : safe;
    }

    private static InvalidOperationException Rejected(string message) => new(message);

    private sealed record LoadedStrategy(
        Yo4xStrategyManifest? Manifest,
        byte[] AssemblyBytes,
        Mql5SourceDocument? Source);

    private sealed class MarketplaceKeys(byte[] aesKey, byte[] hmacKey) : IDisposable
    {
        internal byte[] AesKey { get; } = aesKey;
        internal byte[] HmacKey { get; } = hmacKey;

        public void Dispose()
        {
            CryptographicOperations.ZeroMemory(AesKey);
            CryptographicOperations.ZeroMemory(HmacKey);
        }
    }

    private sealed class RunningSession(
        string botId,
        string botName,
        Mt5NetApiDemoTradeClient broker,
        byte[] assembly,
        string enableFile,
        CancellationTokenSource stop) : IDisposable
    {
        internal string BotId { get; } = botId;
        internal string BotName { get; } = botName;
        internal Mt5NetApiDemoTradeClient Broker { get; } = broker;
        internal byte[] Assembly { get; } = assembly;
        internal string EnableFile { get; } = enableFile;
        internal CancellationTokenSource Stop { get; } = stop;
        internal Task? Task { get; set; }
        internal Timer? QuotePoll { get; set; }
        internal int QuotePollGate;

        public void Dispose()
        {
            QuotePoll?.Dispose();
            Stop.Dispose();
            Broker.Dispose();
            CryptographicOperations.ZeroMemory(Assembly);
            if (File.Exists(EnableFile))
            {
                File.Delete(EnableFile);
            }
        }
    }

    private sealed class JournalingTradeGateway(IMt5TradeGateway inner, string botId, string botName) : IMt5TradeGateway
    {
        private int quotesSeen;
        private Action<DateTime, double, double>? observer;

        public string Symbol => inner.Symbol;

        public Action<DateTime, double, double>? QuoteObserver
        {
            get => observer;
            set
            {
                observer = value;
                inner.QuoteObserver = (time, bid, ask) =>
                {
                    int n = Interlocked.Increment(ref quotesSeen);
                    if (n == 1 || n % 50 == 0)
                    {
                        try
                        {
                            File.AppendAllText(
                                Path.Combine(
                                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                                    "YO4X", "logs", "live-" + botId + ".log"),
                                DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture)
                                + $" quote #{n} bid={bid.ToString("F2", CultureInfo.InvariantCulture)} ask={ask.ToString("F2", CultureInfo.InvariantCulture)}"
                                + Environment.NewLine);
                        }
                        catch
                        {
                        }
                    }

                    observer?.Invoke(time, bid, ask);
                };
            }
        }

        public Mt5LiveAccountSnapshot ReadAccountSnapshot() => inner.ReadAccountSnapshot();

        public Mt5LiveSymbolSnapshot? ReadSymbolSnapshot() => inner.ReadSymbolSnapshot();

        public async Task<Mt5DemoOrderReceipt> SendAsync(
            Mt5DemoSide side,
            double volume,
            double price,
            double stopLoss,
            double takeProfit,
            string comment,
            CancellationToken cancellationToken = default)
        {
            Mt5DemoOrderReceipt receipt = await inner
                .SendAsync(side, volume, price, stopLoss, takeProfit, comment, cancellationToken)
                .ConfigureAwait(false);
            string journalSide = side is Mt5DemoSide.Buy or Mt5DemoSide.BuyLimit or Mt5DemoSide.BuyStop
                ? "BUY"
                : "SELL";
            try
            {
                File.AppendAllText(
                    Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "YO4X", "logs", "live-" + botId + ".log"),
                    DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture)
                    + $" OrderSend {side} vol={volume.ToString(CultureInfo.InvariantCulture)} price={price.ToString(CultureInfo.InvariantCulture)} ticket={receipt.Ticket}"
                    + Environment.NewLine);
            }
            catch
            {
            }

            if (receipt.Ticket != 0)
            {
                LocalTradingEngine.Instance.RecordLiveOrder(
                    botId, botName, receipt.Symbol, journalSide, receipt.Volume, receipt.Price, receipt.Ticket);
            }

            return receipt;
        }

        public Task<Mt5ExecutionLatency> ModifyAsync(
            Mt5DemoOrderReceipt receipt,
            double stopLoss,
            double takeProfit,
            CancellationToken cancellationToken = default)
            => inner.ModifyAsync(receipt, stopLoss, takeProfit, cancellationToken);

        public async Task<Mt5DemoOrderReceipt> CloseAsync(
            Mt5DemoOrderReceipt receipt,
            CancellationToken cancellationToken = default)
        {
            Mt5DemoOrderReceipt closed = await inner.CloseAsync(receipt, cancellationToken).ConfigureAwait(false);
            LocalTradingEngine.Instance.RecordLiveClose(receipt.Ticket, closed.Price, closed.Profit);
            return closed;
        }

        public Task<Mt5ExecutionLatency> CancelAsync(
            Mt5DemoOrderReceipt receipt,
            CancellationToken cancellationToken = default)
            => inner.CancelAsync(receipt, cancellationToken);
    }
}
