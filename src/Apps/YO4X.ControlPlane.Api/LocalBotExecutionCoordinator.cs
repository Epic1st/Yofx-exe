using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Npgsql;
using NpgsqlTypes;
using YO4X.BuildingBlocks;
using YO4X.ControlPlane.Application;
using YO4X.LocalSecrets.Windows;
using YO4X.Mql5.Engine.Feed;
using YO4X.Mql5.Live;
using YO4X.Mt5.ConnectionProbe.Windows;
using YO4X.Persistence.Postgres;
using YO4X.Runtime.Contracts;
using YO4X.StrategyGovernance.Licensing;
using YO4X.StrategyGovernance.Packaging;
using YO4X.Tenancy;

namespace YO4X.ControlPlane.Api;

internal sealed record LocalBotExecutionOptions(
    string ArtifactRoot,
    string PackageKeyDocument,
    string Mt5Artifact,
    string VaultRoot,
    string EnableRoot,
    string MarketDataRoot,
    string? FallbackServer,
    string? FallbackHost,
    int FallbackPort)
{
    internal static LocalBotExecutionOptions? Load(IConfiguration configuration, IHostEnvironment environment)
    {
        MarketplacePublicationOptions? marketplace = MarketplacePublicationOptions.Load(configuration);
        IConfigurationSection mt5 = configuration.GetSection("DevelopmentMt5ConnectionProbe");
        string? artifact = mt5["ArtifactPath"]?.Trim();
        string? vault = mt5["VaultRoot"]?.Trim();
        if (!environment.IsDevelopment()
            || marketplace is null
            || string.IsNullOrWhiteSpace(artifact)
            || string.IsNullOrWhiteSpace(vault))
            return null;

        string local = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "YO4X");
        return new LocalBotExecutionOptions(
            marketplace.ArtifactRoot,
            marketplace.PackageKeyDocumentFile,
            Path.GetFullPath(artifact),
            Path.GetFullPath(vault),
            Path.Combine(local, "bot-enable"),
            Path.Combine(local, "marketdata"),
            mt5["ServerName"]?.Trim(),
            mt5["Host"]?.Trim(),
            int.TryParse(mt5["Port"], NumberStyles.None, CultureInfo.InvariantCulture, out int port)
                ? port
                : 0);
    }
}

internal static class LocalBotExecutionRegistration
{
    internal static IServiceCollection TryAddLocalBotExecution(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        LocalBotExecutionOptions? options = LocalBotExecutionOptions.Load(configuration, environment);
        if (options is null)
            return services;
        // Package keys remain available so a same-machine diagnostic can unpack
        // an artifact. The in-process runner is not registered: a hosted control
        // plane authorizes and records, and YO4X.Desktop executes.
        services.AddSingleton(options);
        services.AddSingleton(new LocalMarketplacePackageKeyProvider(options.PackageKeyDocument));
        return services;
    }

    internal static void MapLocalBotExecutionReadiness(this WebApplication app)
    {
        LocalBotExecutionOptions? options = app.Services.GetService<LocalBotExecutionOptions>();
        LocalMarketplacePackageKeyProvider? keys =
            app.Services.GetService<LocalMarketplacePackageKeyProvider>();
        if (options is null || keys is null)
            return;
        app.MapGet("/internal/v1/local-bot-execution/readiness", (HttpContext context) =>
        {
            if (!IPAddress.IsLoopback(context.Connection.RemoteIpAddress ?? IPAddress.None))
                return Results.NotFound();
            try
            {
                using LocalMarketplacePackageKeys opened = keys.Open();
                bool ready = File.Exists(options.Mt5Artifact)
                    && Directory.Exists(options.ArtifactRoot)
                    && opened.AesKey.Length == 32
                    && opened.HmacKey.Length == 32;
                return ready
                    ? Results.Ok(new { ready = true, leaseLifetimeSeconds = 600 })
                    : Results.Problem(statusCode: 503, title: "Local bot execution is not ready.");
            }
            catch
            {
                return Results.Problem(statusCode: 503, title: "Local bot execution is not ready.");
            }
        }).AllowAnonymous();
    }
}

internal readonly record struct LocalMt5Endpoint(string Host, int Port)
{
    internal string Redacted => $"approved:{Port.ToString(CultureInfo.InvariantCulture)}";
}

internal static class LocalMt5EndpointSelector
{
    private const int MaximumAttempts = 8;

    internal static IReadOnlyList<LocalMt5Endpoint> Build(
        IEnumerable<string> configured,
        string? explicitFallbackHost,
        int explicitFallbackPort)
    {
        ArgumentNullException.ThrowIfNull(configured);
        var result = new List<LocalMt5Endpoint>();
        var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string value in configured)
        {
            if (result.Count == MaximumAttempts) break;
            if (!TryParse(value, out LocalMt5Endpoint endpoint) || !IsPublic(endpoint.Host))
                continue;
            Add(endpoint);
        }

        if (result.Count < MaximumAttempts
            && !string.IsNullOrWhiteSpace(explicitFallbackHost)
            && explicitFallbackPort is > 0 and <= ushort.MaxValue)
        {
            string host = explicitFallbackHost.Trim().Trim('[', ']');
            if (IsValidHost(host)) Add(new LocalMt5Endpoint(host, explicitFallbackPort));
        }
        return result;

        void Add(LocalMt5Endpoint endpoint)
        {
            string key = endpoint.Host + ":" + endpoint.Port.ToString(CultureInfo.InvariantCulture);
            if (unique.Add(key)) result.Add(endpoint);
        }
    }

    private static bool TryParse(string value, out LocalMt5Endpoint endpoint)
    {
        endpoint = default;
        if (string.IsNullOrWhiteSpace(value)) return false;
        int separator = value.LastIndexOf(':');
        if (separator <= 0 || separator == value.Length - 1
            || !int.TryParse(value.AsSpan(separator + 1), NumberStyles.None, CultureInfo.InvariantCulture, out int port)
            || port is <= 0 or > ushort.MaxValue)
            return false;
        string host = value[..separator].Trim().Trim('[', ']');
        if (!IsValidHost(host)) return false;
        endpoint = new LocalMt5Endpoint(host, port);
        return true;
    }

    private static bool IsValidHost(string host) =>
        host.Length is > 0 and <= 253 && !host.Any(char.IsWhiteSpace);

    private static bool IsPublic(string host)
    {
        if (!IPAddress.TryParse(host, out IPAddress? address)) return true;
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any)
            || address.Equals(IPAddress.IPv6Any) || address.Equals(IPAddress.None))
            return false;
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
        return !(address.IsIPv6LinkLocal || address.IsIPv6SiteLocal
            || (bytes[0] & 0xFE) == 0xFC);
    }
}

internal sealed class LocalMt5EndpointsUnavailableException(Exception innerException)
    : Exception("Every approved MT5 endpoint failed.", innerException);

internal static class LocalMt5EndpointFailover
{
    internal static TClient Connect<TClient>(
        IReadOnlyList<LocalMt5Endpoint> endpoints,
        Func<LocalMt5Endpoint, TClient> create,
        Action<TClient> connectAndVerify,
        Func<Exception, bool> isRetryable,
        Action<LocalMt5Endpoint, Exception>? onRetry = null)
        where TClient : class, IDisposable
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(create);
        ArgumentNullException.ThrowIfNull(connectAndVerify);
        ArgumentNullException.ThrowIfNull(isRetryable);
        if (endpoints.Count == 0)
            throw new ArgumentException("At least one endpoint is required.", nameof(endpoints));

        Exception? last = null;
        foreach (LocalMt5Endpoint endpoint in endpoints)
        {
            TClient? client = null;
            try
            {
                client = create(endpoint);
                connectAndVerify(client);
                return client;
            }
            catch (Exception exception)
            {
                client?.Dispose();
                if (!isRetryable(exception)) throw;
                last = exception;
                onRetry?.Invoke(endpoint, exception);
            }
        }
        throw new LocalMt5EndpointsUnavailableException(last!);
    }
}

internal sealed class LocalBotExecutionCoordinator(
    LocalBotExecutionManager manager,
    IFrontendProjectionApplication projections) : IBotExecutionCoordinator
{
    public async Task<BotView?> ChangeStatusAsync(
        UserActor actor,
        Guid botId,
        BotStatusChange request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        BotSettingsView? settings = await projections.GetBotSettingsAsync(actor, botId, cancellationToken)
            .ConfigureAwait(false);
        if (settings is null)
            return null;

        if (request.Status is BotStatus.Starting or BotStatus.Running)
            await manager.StartAsync(actor, settings, cancellationToken).ConfigureAwait(false);
        else if (request.Status is BotStatus.Stopped or BotStatus.Paused)
            await manager.StopAsync(actor, botId, request.Status, cancellationToken).ConfigureAwait(false);
        else
            await projections.SetBotStatusAsync(actor, botId, request, cancellationToken).ConfigureAwait(false);

        return await projections.GetBotAsync(actor, botId, cancellationToken).ConfigureAwait(false);
    }
}

internal sealed class LocalBotExecutionManager : IAsyncDisposable
{
    private static readonly Action<ILogger, Guid, string, Exception?> LogBotLifecycle =
        LoggerMessage.Define<Guid, string>(
            LogLevel.Information,
            new EventId(2101, "LocalBotLifecycle"),
            "Local bot {BotId}: {LifecycleMessage}");
    private static readonly Action<ILogger, Guid, LiveStopReason, string, Exception?> LogBotOutcome =
        LoggerMessage.Define<Guid, LiveStopReason, string>(
            LogLevel.Error,
            new EventId(2102, "LocalBotOutcome"),
            "Local bot {BotId} stopped during execution startup/runtime: {Reason}. {Detail}");
    private static readonly Action<ILogger, Guid, Exception?> LogBotFault =
        LoggerMessage.Define<Guid>(
            LogLevel.Error,
            new EventId(2103, "LocalBotFault"),
            "Local bot {BotId} faulted.");
    private static readonly Action<ILogger, string, string, Exception?> LogMt5Session =
        LoggerMessage.Define<string, string>(
            LogLevel.Information,
            new EventId(2104, "Mt5Session"),
            "MT5 session for {Server}: {SessionMessage}");
    private static readonly Action<ILogger, string, string, Exception?> LogEndpointRetry =
        LoggerMessage.Define<string, string>(
            LogLevel.Warning,
            new EventId(2105, "Mt5EndpointRetry"),
            "MT5 endpoint attempt failed for {Server}; trying the next approved endpoint. Endpoint={Endpoint}");
    private static readonly Action<ILogger, string, Exception?> LogEndpointsUnavailable =
        LoggerMessage.Define<string>(
            LogLevel.Error,
            new EventId(2106, "Mt5EndpointsUnavailable"),
            "Every approved MT5 endpoint failed for {Server}.");
    private static readonly LeaseActionClass BotActions =
        LeaseActionClass.Increase | LeaseActionClass.Reduce | LeaseActionClass.Protect
        | LeaseActionClass.Cancel | LeaseActionClass.EmergencyClose;
    private readonly ConcurrentDictionary<Guid, RunningBot> running = [];
    private readonly SemaphoreSlim lifecycle = new(1, 1);
    private readonly PostgresDatabase database;
    private readonly LocalBotExecutionOptions options;
    private readonly LocalMarketplacePackageKeyProvider packageKeys;
    private readonly LocalExecutionLeaseAuthority leaseAuthority;
    private readonly DpapiLocalMt5CredentialVault vault;
    private readonly ILogger<LocalBotExecutionManager> logger;

    public LocalBotExecutionManager(
        PostgresDatabase database,
        LocalBotExecutionOptions options,
        LocalMarketplacePackageKeyProvider packageKeys,
        LocalExecutionLeaseAuthority leaseAuthority,
        ILogger<LocalBotExecutionManager> logger)
    {
        this.database = database;
        this.options = options;
        this.packageKeys = packageKeys;
        this.leaseAuthority = leaseAuthority;
        this.logger = logger;
        vault = new DpapiLocalMt5CredentialVault(options.VaultRoot);
    }

    internal async Task StartAsync(
        UserActor actor,
        BotSettingsView settings,
        CancellationToken cancellationToken)
    {
        await lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (running.ContainsKey(settings.BotId))
                return;

            await SetStatusAsync(actor, settings.BotId, "STARTING", cancellationToken).ConfigureAwait(false);
            BotExecutionBinding binding;
            byte[] package;
            try
            {
                binding = await LoadBindingAsync(actor, settings.BotId, cancellationToken)
                    .ConfigureAwait(false);
                (BotExecutionBinding resolvedBinding, string packagePath) = await ResolvePackageAsync(
                    actor, settings.BotId, binding, cancellationToken).ConfigureAwait(false);
                binding = resolvedBinding;
                package = await File.ReadAllBytesAsync(packagePath, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                await SetFaultAsync(actor, settings.BotId, exception, "BOT_CONFIGURATION_INVALID", CancellationToken.None)
                    .ConfigureAwait(false);
                throw;
            }
            byte[] assembly = [];
            Mt5NetApiDemoTradeClient? broker = null;
            CancellationTokenSource? stop = null;
            string? enableFile = null;
            try
            {
                Yo4xStrategyManifest manifest = Yo4xStrategyPackage.ReadManifest(package);
                using LocalMt5Credential? credential = await vault
                    .OpenAsync(binding.CredentialKey, cancellationToken).ConfigureAwait(false);
                if (credential is null
                    || !string.Equals(credential.Server, binding.Server, StringComparison.OrdinalIgnoreCase))
                    throw StartRejected("The linked broker credential is unavailable on this device.");
                if (!string.Equals(binding.Environment, "demo", StringComparison.Ordinal))
                    throw StartRejected("Local bot execution is configured for demo accounts only.");

                using LocalMarketplacePackageKeys keys = packageKeys.Open();
                DateTimeOffset now = DateTimeOffset.UtcNow;
                StrategyLicenseToken license = LicenseAuthority.IssueLicenseToken(
                    new StrategyLicenseClaims(
                        Guid.CreateVersion7(), actor.TenantId, actor.UserId,
                        manifest.StrategyId, manifest.Name, LicenseType.Developer,
                        [credential.Login], [binding.Server], now, now.AddHours(12), 1,
                        now, manifest.Version, manifest.AssemblySha256, keys.SigningKeyId),
                    keys.PrivateKeyPem);
                var validation = new StrategyLicenseValidationContext(
                    actor.TenantId, actor.UserId, manifest.StrategyId, manifest.Version,
                    manifest.AssemblySha256 ?? throw StartRejected("The package assembly digest is absent."),
                    credential.Login, binding.Server, now);
                (manifest, assembly) = Yo4xStrategyPackage.UnpackAndValidate(
                    package, license, validation, keys.PublicKeyPem, keys.PublicKeyPem,
                    keys.AesKey, keys.HmacKey);

                Directory.CreateDirectory(options.EnableRoot);
                enableFile = Path.Combine(options.EnableRoot, settings.BotId.ToString("N") + ".enabled");
                await File.WriteAllTextAsync(enableFile, settings.BotId.ToString("D"), cancellationToken)
                    .ConfigureAwait(false);
                broker = credential.UsePassword(passwordUtf8 => ConnectBroker(
                    binding,
                    credential.Login,
                    Encoding.UTF8.GetString(passwordUtf8),
                    settings.Symbol,
                    enableFile));

                ExecutionLeaseBinding leaseBinding = CreateLeaseBinding(actor, settings, binding);
                var state = new RunningBot(
                    actor,
                    settings.BotId,
                    leaseAuthority.Issue(leaseBinding, BotActions, now),
                    leaseBinding,
                    broker,
                    assembly,
                    enableFile,
                    new CancellationTokenSource());
                stop = state.Stop;
                if (!running.TryAdd(settings.BotId, state))
                    throw StartRejected("This bot is already starting.");
                broker = null;
                assembly = [];
                enableFile = null;
                stop = null;

                var gateway = new LeaseValidatedMt5TradeGateway(
                    state.Broker,
                    leaseAuthority,
                    () => state.Lease,
                    state.Binding,
                    leaseId => state.RevokedLeaseId == leaseId);
                IReadOnlyList<Mql5Bar> seed = LoadSeed(binding.Server, settings.Symbol, settings.Timeframe);
                if (seed.Count == 0)
                    seed = DownloadSeed(state.Broker, settings.Timeframe);
                var initialized = new TaskCompletionSource(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                var runner = new LiveStrategyRunner(message =>
                {
                    LogBotLifecycle(logger, settings.BotId, message, null);
                });
                IReadOnlyDictionary<string, string> inputs = settings.Overrides
                    .ToDictionary(value => value.Name, value => value.Value, StringComparer.Ordinal);
                state.Task = RunAsync(
                    state,
                    runner,
                    manifest,
                    gateway,
                    seed,
                    PeriodMinutes(settings.Timeframe),
                    settings.Symbol.Contains("XAU", StringComparison.OrdinalIgnoreCase) ? 2 : 5,
                    inputs,
                    initialized);
                await initialized.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                stop?.Cancel();
                stop?.Dispose();
                broker?.Dispose();
                CryptographicOperations.ZeroMemory(assembly);
                if (enableFile is not null && File.Exists(enableFile)) File.Delete(enableFile);
                await SetFaultAsync(actor, settings.BotId, exception, "BOT_START_FAILED", CancellationToken.None)
                    .ConfigureAwait(false);
                throw;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(package);
            }
        }
        finally
        {
            lifecycle.Release();
        }
    }

    internal async Task StopAsync(
        UserActor actor,
        Guid botId,
        BotStatus requested,
        CancellationToken cancellationToken)
    {
        await lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (running.TryGetValue(botId, out RunningBot? state))
            {
                state.RevokedLeaseId = state.Lease.Claims.LeaseId;
                state.Stop.Cancel();
                if (state.Task is not null)
                {
                    try
                    {
                        await state.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken)
                            .ConfigureAwait(false);
                    }
                    catch (TimeoutException)
                    {
                        // Authority was revoked before cancellation. A slow strategy teardown
                        // therefore cannot submit another broker mutation while it unwinds.
                    }
                }
            }
            await SetStatusAsync(
                actor, botId, requested == BotStatus.Paused ? "PAUSED" : "STOPPED", cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            lifecycle.Release();
        }
    }

    private async Task RunAsync(
        RunningBot state,
        LiveStrategyRunner runner,
        Yo4xStrategyManifest manifest,
        IMt5TradeGateway gateway,
        IReadOnlyList<Mql5Bar> seed,
        int periodMinutes,
        int digits,
        IReadOnlyDictionary<string, string> inputs,
        TaskCompletionSource initialized)
    {
        using var renewStop = CancellationTokenSource.CreateLinkedTokenSource(state.Stop.Token);
        Task renew = RenewAsync(state, renewStop.Token);
        try
        {
            LiveRunOutcome outcome = await runner.RunPackagedAsync(
                manifest, state.Assembly, gateway, seed, periodMinutes, digits,
                inputs,
                LiveTickCadence.EveryQuote,
                async token =>
                {
                await SetStatusAsync(state.Actor, state.BotId, "RUNNING", token)
                        .ConfigureAwait(false);
                    initialized.TrySetResult();
                },
                state.Stop.Token).ConfigureAwait(false);
            if (outcome.Reason != LiveStopReason.Requested)
            {
                string detail = SafeRuntimeDetail(outcome.Detail, outcome.Reason);
                initialized.TrySetException(StartRejected(detail));
                LogBotOutcome(logger, state.BotId, outcome.Reason, detail, null);
            }
            if (outcome.Reason == LiveStopReason.Requested)
                await SetStatusAsync(state.Actor, state.BotId, "STOPPED", CancellationToken.None)
                    .ConfigureAwait(false);
            else
                await SetFaultAsync(
                    state.Actor,
                    state.BotId,
                    StartRejected(SafeRuntimeDetail(outcome.Detail, outcome.Reason)),
                    "BOT_STRATEGY_RUNTIME_FAILED",
                    CancellationToken.None).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (state.Stop.IsCancellationRequested)
        {
            initialized.TrySetCanceled(state.Stop.Token);
            await SetStatusAsync(state.Actor, state.BotId, "STOPPED", CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            initialized.TrySetException(StartRejected(SafeRuntimeDetail(exception.Message, LiveStopReason.Faulted)));
            LogBotFault(logger, state.BotId, exception);
            await SetFaultAsync(
                state.Actor,
                state.BotId,
                exception,
                "BOT_STRATEGY_RUNTIME_FAILED",
                CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            renewStop.Cancel();
            try { await renew.ConfigureAwait(false); } catch (OperationCanceledException) { }
            running.TryRemove(state.BotId, out _);
            state.RevokedLeaseId = state.Lease.Claims.LeaseId;
            state.Dispose();
        }
    }

    private async Task RenewAsync(RunningBot state, CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(5));
        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
        {
            SignedExecutionLease previous = state.Lease;
            state.Lease = leaseAuthority.Issue(state.Binding, BotActions, DateTimeOffset.UtcNow);
            state.RevokedLeaseId = previous.Claims.LeaseId;
        }
    }

    private async Task<BotExecutionBinding> LoadBindingAsync(
        UserActor actor,
        Guid botId,
        CancellationToken cancellationToken)
    {
        var context = new TenantExecutionContext(actor.TenantId, actor.UserId, Guid.CreateVersion7(), null);
        await using TenantPostgresTransaction transaction = await database
            .BeginTenantTransactionAsync(context, cancellationToken).ConfigureAwait(false);
        await using NpgsqlCommand command = transaction.CreateCommand(
            """
            select bot.strategy_id, bot.broker_account_id,
                   strategy.package_sha256, account.server, account.binding_fingerprint,
                   account.environment, coalesce(directory.access_endpoints, array[]::text[])
            from bots.bots as bot
            join catalog.strategies as strategy
              on strategy.tenant_id = bot.tenant_id and strategy.id = bot.strategy_id
            join operations.broker_accounts as account
              on account.tenant_id = bot.tenant_id and account.id = bot.broker_account_id
             and account.user_id = bot.user_id
            left join brokerdirectory.catalogue_broker_profiles as mapped
              on mapped.broker_profile_id = account.broker_profile_id
            left join brokerdirectory.servers as directory on directory.id = mapped.server_id
            where bot.tenant_id = @tenant_id and bot.user_id = @user_id and bot.id = @bot_id
              and account.state <> 'deleted'
              and strategy.is_drm_protected
              and strategy.package_format_version = 2
            """);
        command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, actor.TenantId);
        command.Parameters.AddWithValue("user_id", NpgsqlDbType.Uuid, actor.UserId);
        command.Parameters.AddWithValue("bot_id", NpgsqlDbType.Uuid, botId);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            || reader.IsDBNull(1)
            || reader.IsDBNull(2))
            throw StartRejected("The bot needs a linked demo account and a published .yo4x strategy.");
        var result = new BotExecutionBinding(
            reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), reader.GetString(3),
            reader.GetString(4), reader.GetString(5), reader.GetFieldValue<string[]>(6));
        await reader.DisposeAsync().ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return result;
    }

    private async Task<(BotExecutionBinding Binding, string PackagePath)> ResolvePackageAsync(
        UserActor actor,
        Guid botId,
        BotExecutionBinding binding,
        CancellationToken cancellationToken)
    {
        if (TryResolveArtifact(binding.PackageSha256, out string packagePath))
            return (binding, packagePath);

        var context = new TenantExecutionContext(actor.TenantId, actor.UserId, Guid.CreateVersion7(), null);
        await using TenantPostgresTransaction transaction = await database
            .BeginTenantTransactionAsync(context, cancellationToken).ConfigureAwait(false);
        var candidates = new List<(Guid StrategyId, string Name, string PackageSha256)>();
        await using (NpgsqlCommand command = transaction.CreateCommand(
            """
            select candidate.id, candidate.name, candidate.package_sha256
            from catalog.strategies as current
            join catalog.strategies as candidate
              on candidate.tenant_id = current.tenant_id
             and candidate.id <> current.id
             and candidate.is_drm_protected
             and candidate.package_format_version = 2
             and candidate.package_sha256 is not null
             and regexp_replace(lower(btrim(candidate.name)), '\.(mq5|yo4x)$', '')
                 = regexp_replace(lower(btrim(current.name)), '\.(mq5|yo4x)$', '')
            where current.tenant_id = @tenant_id
              and current.id = @strategy_id
            order by candidate.updated_at desc, candidate.id desc
            limit @limit
            """))
        {
            command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, actor.TenantId);
            command.Parameters.AddWithValue("strategy_id", NpgsqlDbType.Uuid, binding.StrategyId);
            command.Parameters.AddWithValue("limit", NpgsqlDbType.Integer, 20);
            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken)
                .ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                candidates.Add((reader.GetGuid(0), reader.GetString(1), reader.GetString(2)));
        }

        foreach ((Guid strategyId, string name, string packageSha256) in candidates)
        {
            if (!TryResolveArtifact(packageSha256, out packagePath))
                continue;

            await using NpgsqlCommand update = transaction.CreateCommand(
                """
                update bots.bots
                set strategy_id = @replacement_strategy_id,
                    name = @replacement_name,
                    updated_at = clock_timestamp()
                where tenant_id = @tenant_id
                  and user_id = @user_id
                  and id = @bot_id
                  and strategy_id = @current_strategy_id
                  and status = 'STARTING'
                """);
            update.Parameters.AddWithValue("replacement_strategy_id", NpgsqlDbType.Uuid, strategyId);
            update.Parameters.AddWithValue("replacement_name", NpgsqlDbType.Text, name);
            update.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, actor.TenantId);
            update.Parameters.AddWithValue("user_id", NpgsqlDbType.Uuid, actor.UserId);
            update.Parameters.AddWithValue("bot_id", NpgsqlDbType.Uuid, botId);
            update.Parameters.AddWithValue("current_strategy_id", NpgsqlDbType.Uuid, binding.StrategyId);
            if (await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
                throw StartRejected("The bot changed while its package was being resolved.");
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return (binding with { StrategyId = strategyId, PackageSha256 = packageSha256 }, packagePath);
        }

        throw StartRejected("The published .yo4x artifact is not available on this device.");
    }

    private bool TryResolveArtifact(string packageSha256, out string packagePath)
    {
        packagePath = string.Empty;
        if (packageSha256.Length != 64 || packageSha256.Any(character => !char.IsAsciiHexDigit(character)))
            return false;
        string normalized = packageSha256.ToLowerInvariant();
        string candidate = Path.Combine(options.ArtifactRoot, normalized + ".yo4x");
        if (!File.Exists(candidate))
            return false;
        using FileStream stream = File.OpenRead(candidate);
        string actual = Convert.ToHexStringLower(SHA256.HashData(stream));
        if (!CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(actual), Encoding.ASCII.GetBytes(normalized)))
            return false;
        packagePath = candidate;
        return true;
    }

    private async Task SetStatusAsync(
        UserActor actor,
        Guid botId,
        string status,
        CancellationToken cancellationToken)
    {
        var context = new TenantExecutionContext(actor.TenantId, actor.UserId, Guid.CreateVersion7(), null);
        await using TenantPostgresTransaction transaction = await database
            .BeginTenantTransactionAsync(context, cancellationToken).ConfigureAwait(false);
        await using NpgsqlCommand command = transaction.CreateCommand(
            """
            update bots.bots
            set status = @status,
                last_error_code = null,
                last_error_message = null,
                updated_at = clock_timestamp()
            where tenant_id = @tenant_id and user_id = @user_id and id = @bot_id
            """);
        command.Parameters.AddWithValue("status", NpgsqlDbType.Text, status);
        command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, actor.TenantId);
        command.Parameters.AddWithValue("user_id", NpgsqlDbType.Uuid, actor.UserId);
        command.Parameters.AddWithValue("bot_id", NpgsqlDbType.Uuid, botId);
        if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
            throw new ResourceNotFoundException();
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task SetFaultAsync(
        UserActor actor,
        Guid botId,
        Exception exception,
        string fallbackCode,
        CancellationToken cancellationToken)
    {
        string code = exception is DomainException domain ? domain.Code : fallbackCode;
        string message = SafeFaultMessage(exception);
        var context = new TenantExecutionContext(actor.TenantId, actor.UserId, Guid.CreateVersion7(), null);
        await using TenantPostgresTransaction transaction = await database
            .BeginTenantTransactionAsync(context, cancellationToken).ConfigureAwait(false);
        await using NpgsqlCommand command = transaction.CreateCommand(
            """
            update bots.bots
            set status = 'FAULTED',
                last_error_code = @code,
                last_error_message = @message,
                updated_at = clock_timestamp()
            where tenant_id = @tenant_id and user_id = @user_id and id = @bot_id
            """);
        command.Parameters.AddWithValue("code", NpgsqlDbType.Text, code.Length > 100 ? code[..100] : code);
        command.Parameters.AddWithValue("message", NpgsqlDbType.Text, message);
        command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, actor.TenantId);
        command.Parameters.AddWithValue("user_id", NpgsqlDbType.Uuid, actor.UserId);
        command.Parameters.AddWithValue("bot_id", NpgsqlDbType.Uuid, botId);
        if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
            throw new ResourceNotFoundException();
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static string SafeFaultMessage(Exception exception)
    {
        Exception visible = exception is DomainException
            ? exception
            : exception.InnerException is DomainException domain
                ? domain
                : exception;
        string message = visible.Message.Replace('\r', ' ').Replace('\n', ' ').Trim();
        if (string.IsNullOrWhiteSpace(message)) message = "The bot failed without an error description.";
        return message.Length > 500 ? message[..500] : message;
    }

    private Mt5NetApiDemoTradeClient ConnectBroker(
        BotExecutionBinding binding,
        ulong login,
        string password,
        string symbol,
        string enableFile)
    {
        IReadOnlyList<LocalMt5Endpoint> endpoints = LocalMt5EndpointSelector.Build(
            binding.AccessEndpoints,
            string.Equals(binding.Server, options.FallbackServer, StringComparison.OrdinalIgnoreCase)
                ? options.FallbackHost
                : null,
            options.FallbackPort);
        if (endpoints.Count == 0)
            throw StartRejected("No approved public access endpoint is available for the linked broker server.");

        try
        {
            return LocalMt5EndpointFailover.Connect(
                endpoints,
                endpoint => Mt5NetApiDemoTradeClient.Create(
                    options.Mt5Artifact,
                    login,
                    password,
                    endpoint.Host,
                    endpoint.Port,
                    symbol,
                    enableFile,
                    message => LogMt5Session(logger, binding.Server, message, null),
                    Mt5TradingEnvironment.Demo),
                client =>
                {
                    client.SetConnectTimeout(12_000);
                    client.Connect();
                    Mt5LiveAccountSnapshot account = client.ReadAccountSnapshot();
                    if (!client.Connected || account.Login != login
                        || account.Environment != Mt5TradingEnvironment.Demo)
                        throw new InvalidDataException("The connected MT5 account identity was not confirmed.");
                    client.StartQuoteStream();
                },
                IsRetryableEndpointFailure,
                (endpoint, exception) => LogEndpointRetry(
                    logger, binding.Server, endpoint.Redacted, exception));
        }
        catch (LocalMt5EndpointsUnavailableException exception)
        {
            LogEndpointsUnavailable(logger, binding.Server, exception);
            throw StartRejected(
                "The linked MT5 demo account could not connect through any approved broker endpoint.");
        }
    }

    private IReadOnlyList<Mql5Bar> LoadSeed(string server, string symbol, string timeframe)
    {
        string path = Path.Combine(options.MarketDataRoot, server, symbol, timeframe + ".csv");
        return File.Exists(path) ? [.. new Mql5CsvMarketFeed(path, symbol).ReadBars()] : [];
    }

    private static Mql5Bar[] DownloadSeed(
        Mt5NetApiDemoTradeClient broker,
        string timeframe)
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
            10080 => Mt5HistoryPeriod.W1,
            _ => throw StartRejected("Live broker history does not support the selected timeframe.")
        };
        DateTime end = DateTime.UtcNow.AddMinutes(-minutes);
        DateTime start = end.AddMinutes(-minutes * 1_500d);
        IReadOnlyList<Mt5HistoryBar> downloaded = broker.DownloadHistory(start, end, period);
        return downloaded
            .TakeLast(1_200)
            .Select(bar => new Mql5Bar(
                bar.Time,
                bar.Open,
                bar.High,
                bar.Low,
                bar.Close,
                bar.TickVolume,
                bar.Spread))
            .ToArray();
    }

    private static ExecutionLeaseBinding CreateLeaseBinding(
        UserActor actor,
        BotSettingsView settings,
        BotExecutionBinding binding) => new(
        actor.TenantId, Guid.CreateVersion7(), actor.UserId, settings.BotId,
        binding.BrokerAccountId, binding.CredentialKey, binding.StrategyId, Guid.CreateVersion7(),
        1, binding.PackageSha256, ExecutionMode.Local, Guid.CreateVersion7(), new string('0', 64),
        Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
        Guid.CreateVersion7(), 1, "local-development");

    private static bool IsRetryableEndpointFailure(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is SocketException or TimeoutException or IOException or ObjectDisposedException)
                return true;
            if (string.Equals(current.GetType().FullName, "mtapi.mt5.ConnectException", StringComparison.Ordinal))
            {
                string message = current.Message;
                return message.Contains("disconnect", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("socket", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("timeout", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("connect", StringComparison.OrdinalIgnoreCase);
            }
        }
        return false;
    }

    private static string SafeRuntimeDetail(string? detail, LiveStopReason reason)
    {
        string safe = string.IsNullOrWhiteSpace(detail)
            ? reason.ToString()
            : detail.Replace('\r', ' ').Replace('\n', ' ').Trim();
        if (safe.Length > 240) safe = safe[..240];
        return $"The strategy could not remain running: {safe}";
    }

    private static int PeriodMinutes(string timeframe) => timeframe.ToUpperInvariant() switch
    {
        "M1" => 1, "M2" => 2, "M3" => 3, "M4" => 4, "M5" => 5, "M6" => 6,
        "M10" => 10, "M12" => 12, "M15" => 15, "M20" => 20, "M30" => 30,
        "H1" => 60, "H2" => 120, "H3" => 180, "H4" => 240, "H6" => 360,
        "H8" => 480, "H12" => 720, "D1" => 1440, "W1" => 10080, "MN1" => 43200,
        _ => throw StartRejected("The bot timeframe is unsupported.")
    };

    private static DomainException StartRejected(string message) => new("BOT_START_REJECTED", message);

    public async ValueTask DisposeAsync()
    {
        foreach (RunningBot state in running.Values)
        {
            state.RevokedLeaseId = state.Lease.Claims.LeaseId;
            state.Stop.Cancel();
        }
        Task[] tasks = running.Values.Where(state => state.Task is not null).Select(state => state.Task!).ToArray();
        try { await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false); } catch { }
        lifecycle.Dispose();
    }

    private sealed record BotExecutionBinding(
        Guid StrategyId,
        Guid BrokerAccountId,
        string PackageSha256,
        string Server,
        string CredentialKey,
        string Environment,
        string[] AccessEndpoints);

    private sealed class RunningBot(
        UserActor actor,
        Guid botId,
        SignedExecutionLease lease,
        ExecutionLeaseBinding binding,
        Mt5NetApiDemoTradeClient broker,
        byte[] assembly,
        string enableFile,
        CancellationTokenSource stop) : IDisposable
    {
        internal UserActor Actor { get; } = actor;
        internal Guid BotId { get; } = botId;
        internal SignedExecutionLease Lease { get; set; } = lease;
        internal Guid? RevokedLeaseId { get; set; }
        internal ExecutionLeaseBinding Binding { get; } = binding;
        internal Mt5NetApiDemoTradeClient Broker { get; } = broker;
        internal byte[] Assembly { get; } = assembly;
        internal string EnableFile { get; } = enableFile;
        internal CancellationTokenSource Stop { get; } = stop;
        internal Task? Task { get; set; }

        public void Dispose()
        {
            Stop.Dispose();
            Broker.Dispose();
            CryptographicOperations.ZeroMemory(Assembly);
            if (File.Exists(EnableFile)) File.Delete(EnableFile);
        }
    }
}
