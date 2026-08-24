using YO4X.Trading.Abstractions;

namespace YO4X.Trading.ProcessIsolation;

/// <summary>
/// Long-lived gateway-host adapter. Every operation that could reach broker code is
/// delegated to a fresh, authenticated, deadline-bound child process.
/// </summary>
public sealed class IsolatedMt5ProcessGateway : IMt5Gateway
{
    public const string DisabledCode = "mt5_process_boundary_disabled";
    public const string UnsupportedCode = "mt5_process_operation_unsupported";
    public const string UnknownCode = "mt5_process_outcome_unknown";
    public const string ReconciliationUnavailableCode =
        "mt5_process_reconciliation_unavailable";

    private readonly IsolatedBrokerProcessOptions options;
    private readonly TimeProvider timeProvider;
    private readonly BrokerProcessClient client;

    public IsolatedMt5ProcessGateway(
        IsolatedBrokerProcessOptions options,
        TimeProvider? timeProvider = null)
        : this(options, timeProvider, observer: null)
    {
    }

    internal IsolatedMt5ProcessGateway(
        IsolatedBrokerProcessOptions options,
        TimeProvider? timeProvider,
        IBrokerProcessObserver? observer)
    {
        this.options = options ?? throw new ArgumentNullException(nameof(options));
        this.timeProvider = timeProvider ?? TimeProvider.System;
        client = new BrokerProcessClient(options, this.timeProvider, observer);
    }

    public GatewayConnectionState ConnectionState => GatewayConnectionState.Suspended;

    public Task<GatewayOperationResult<GatewayCapabilities>> ConnectAsync(
        GatewayConnectionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(
            new GatewayOperationResult<GatewayCapabilities>(false, UnsupportedCode, null));
    }

    public Task<GatewayOperationResult> DisconnectAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(GatewayOperationResult.Success("mt5_process_already_disconnected"));
    }

    public Task<GatewayOperationResult<BrokerAccountSnapshot>> GetAccountAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(
            new GatewayOperationResult<BrokerAccountSnapshot>(false, UnsupportedCode, null));
    }

    public Task<GatewayOperationResult<IReadOnlyList<BrokerQuoteSnapshot>>> GetQuotesAsync(
        IReadOnlyCollection<string> symbols,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(symbols);
        cancellationToken.ThrowIfCancellationRequested();
        return EmptyFailure<BrokerQuoteSnapshot>();
    }

    public Task<GatewayOperationResult<IReadOnlyList<BrokerPositionSnapshot>>> GetPositionsAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return EmptyFailure<BrokerPositionSnapshot>();
    }

    public Task<GatewayOperationResult<IReadOnlyList<BrokerOrderSnapshot>>> GetOrdersAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return EmptyFailure<BrokerOrderSnapshot>();
    }

    public Task<GatewayOperationResult<IReadOnlyList<BrokerDealSnapshot>>> GetDealsAsync(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentOutOfRangeException.ThrowIfGreaterThan(fromUtc, toUtc);
        return EmptyFailure<BrokerDealSnapshot>();
    }

    public async Task<GatewaySendResult> SendAsync(
        AuthorizedBrokerCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();
        if (!options.Enabled)
        {
            return Disabled(command.Command.CreatedAtUtc);
        }

        var request = new BrokerWorkerRequest(
            BrokerWorkerProtocolContract.Version,
            Guid.CreateVersion7(),
            BrokerWorkerProtocolContract.SendOperation,
            DeadlineUtc(),
            new BrokerWorkerSendRequest(
                command.Provenance.BrokerAccountId,
                command.Provenance.GatewayArtifactId,
                command.Provenance.GatewayArtifactSha256,
                command.AuthorizationSha256,
                command.Command),
            null);
        try
        {
            BrokerWorkerResponse response = await client.ExecuteAsync(request, cancellationToken)
                .ConfigureAwait(false);
            return response.SendResult!;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (BrokerProcessBoundaryException exception)
        {
            return exception.ProcessStarted
                ? Unknown()
                : Disabled(timeProvider.GetUtcNow());
        }
    }

    public async Task<GatewayOperationResult<BrokerReconciliationSnapshot>> ReconcileAsync(
        IReadOnlyCollection<Guid> commandIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(commandIds);
        cancellationToken.ThrowIfCancellationRequested();
        if (!options.Enabled)
        {
            return new GatewayOperationResult<BrokerReconciliationSnapshot>(
                false,
                DisabledCode,
                null);
        }

        Guid[] ids = commandIds.ToArray();
        if (ids.Length is < 1 or > BrokerWorkerContractValidator.MaximumCommandIds
            || ids.Any(id => id == Guid.Empty)
            || ids.Distinct().Count() != ids.Length)
        {
            return new GatewayOperationResult<BrokerReconciliationSnapshot>(
                false,
                ReconciliationUnavailableCode,
                null);
        }

        var request = new BrokerWorkerRequest(
            BrokerWorkerProtocolContract.Version,
            Guid.CreateVersion7(),
            BrokerWorkerProtocolContract.ReconcileOperation,
            DeadlineUtc(),
            null,
            new BrokerWorkerReconcileRequest(ids));
        try
        {
            BrokerWorkerResponse response = await client.ExecuteAsync(request, cancellationToken)
                .ConfigureAwait(false);
            return response.IsSuccess && response.ReconciliationSnapshot is not null
                ? new GatewayOperationResult<BrokerReconciliationSnapshot>(
                    true,
                    response.Code,
                    response.ReconciliationSnapshot)
                : new GatewayOperationResult<BrokerReconciliationSnapshot>(
                    false,
                    response.Code,
                    null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (BrokerProcessBoundaryException)
        {
            return new GatewayOperationResult<BrokerReconciliationSnapshot>(
                false,
                ReconciliationUnavailableCode,
                null);
        }
    }

    private DateTimeOffset DeadlineUtc() =>
        timeProvider.GetUtcNow().Add(options.OperationTimeout);

    private static GatewaySendResult Disabled(DateTimeOffset observedAtUtc) =>
        new(
            GatewayCommandDisposition.SubmissionDisabled,
            DisabledCode,
            null,
            null,
            null,
            observedAtUtc.ToUniversalTime(),
            true);

    private GatewaySendResult Unknown() =>
        new(
            GatewayCommandDisposition.Unknown,
            UnknownCode,
            null,
            null,
            null,
            timeProvider.GetUtcNow(),
            false);

    private static Task<GatewayOperationResult<IReadOnlyList<T>>> EmptyFailure<T>() =>
        Task.FromResult(
            new GatewayOperationResult<IReadOnlyList<T>>(
                false,
                UnsupportedCode,
                Array.Empty<T>()));
}
