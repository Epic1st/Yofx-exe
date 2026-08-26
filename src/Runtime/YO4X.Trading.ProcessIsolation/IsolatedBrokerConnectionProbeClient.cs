using YO4X.Trading.Abstractions;

namespace YO4X.Trading.ProcessIsolation;

/// <summary>
/// Production caller for the dedicated connection-probe worker. The public surface
/// can create only connect_probe requests and exposes no trading operation.
/// </summary>
public sealed class IsolatedBrokerConnectionProbeClient
{
    private const string WorkerEnvironmentPrefix = "YO4X_MT5_PROBE_";
    private static readonly HashSet<string> AllowedWorkerEnvironment = new(StringComparer.Ordinal)
    {
        WorkerEnvironmentPrefix + "ARTIFACT_ID",
        WorkerEnvironmentPrefix + "ARTIFACT_SHA256",
        WorkerEnvironmentPrefix + "ARTIFACT_PATH",
        WorkerEnvironmentPrefix + "VAULT_ROOT",
        WorkerEnvironmentPrefix + "BROKER_COMPANY",
        WorkerEnvironmentPrefix + "SERVER_NAME",
        WorkerEnvironmentPrefix + "HOST",
        WorkerEnvironmentPrefix + "PORT",
        WorkerEnvironmentPrefix + "PFX_PATH",
        WorkerEnvironmentPrefix + "PFX_PASSWORD"
    };

    private readonly IsolatedBrokerProcessOptions options;
    private readonly TimeProvider timeProvider;
    private readonly BrokerProcessClient client;

    public IsolatedBrokerConnectionProbeClient(
        IsolatedBrokerProcessOptions options,
        IReadOnlyDictionary<string, string> workerEnvironment,
        TimeProvider? timeProvider = null)
        : this(options, workerEnvironment, timeProvider, observer: null)
    {
    }

    internal IsolatedBrokerConnectionProbeClient(
        IsolatedBrokerProcessOptions options,
        IReadOnlyDictionary<string, string> workerEnvironment,
        TimeProvider? timeProvider,
        IBrokerProcessObserver? observer)
    {
        this.options = options ?? throw new ArgumentNullException(nameof(options));
        this.timeProvider = timeProvider ?? TimeProvider.System;
        Dictionary<string, string> environment = ValidateEnvironment(workerEnvironment);
        client = new BrokerProcessClient(
            options,
            this.timeProvider,
            observer,
            launchCheckpoint: null,
            environment);
    }

    public async Task<GatewayOperationResult<BrokerConnectionProbeObservation>> ProbeAsync(
        BrokerWorkerConnectProbeRequest probe,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(probe);
        cancellationToken.ThrowIfCancellationRequested();
        if (!options.Enabled)
        {
            return Failure(BrokerWorkerProtocolContract.ConnectProbeUnavailableCode);
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        var normalizedProbe = probe with { ProbeNotBeforeUtc = now };
        var request = new BrokerWorkerRequest(
            BrokerWorkerProtocolContract.Version,
            Guid.CreateVersion7(),
            BrokerWorkerProtocolContract.ConnectProbeOperation,
            now.Add(options.OperationTimeout),
            null,
            null,
            normalizedProbe);
        try
        {
            BrokerWorkerResponse response = await client
                .ExecuteAsync(request, cancellationToken)
                .ConfigureAwait(false);
            return response.IsSuccess && response.ConnectProbeObservation is not null
                ? new GatewayOperationResult<BrokerConnectionProbeObservation>(
                    true,
                    response.Code,
                    response.ConnectProbeObservation)
                : Failure(response.Code);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (BrokerProcessBoundaryException)
        {
            return Failure(BrokerWorkerProtocolContract.ConnectProbeFailedCode);
        }
    }

    private static Dictionary<string, string> ValidateEnvironment(
        IReadOnlyDictionary<string, string> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var snapshot = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach ((string key, string value) in values)
        {
            if (!AllowedWorkerEnvironment.Contains(key)
                || string.IsNullOrWhiteSpace(value)
                || value.IndexOfAny(['\0', '\r', '\n']) >= 0)
            {
                throw new ArgumentException(
                    "The connection-probe worker environment is invalid.",
                    nameof(values));
            }

            snapshot.Add(key, value);
        }

        return snapshot;
    }

    private static GatewayOperationResult<BrokerConnectionProbeObservation> Failure(string code) =>
        new(false, code, null);
}
