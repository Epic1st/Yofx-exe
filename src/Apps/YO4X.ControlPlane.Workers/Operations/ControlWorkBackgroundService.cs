namespace YO4X.ControlPlane.Workers.Operations;

internal enum ControlWorkCycleOutcome
{
    Completed,
    RequiredDependencyUnavailable,
    PartialCycleFailure,
    ScanProgressLagging,
    StoreOperationFailed,
    OperationBacklogLagging
}

public sealed partial class ControlWorkBackgroundService : BackgroundService
{
    private readonly IUserOperationWorkStore _operations;
    private readonly ICredentialGrantExpiryStore _credentialGrants;
    private readonly IDeploymentProjectionStore _deployments;
    private readonly ControlWorkOptions _options;
    private readonly ControlWorkReadiness _readiness;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ControlWorkBackgroundService> _logger;

    public ControlWorkBackgroundService(
        IUserOperationWorkStore operations,
        ICredentialGrantExpiryStore credentialGrants,
        IDeploymentProjectionStore deployments,
        ControlWorkOptions options,
        ControlWorkReadiness readiness,
        TimeProvider timeProvider,
        ILogger<ControlWorkBackgroundService> logger)
    {
        ArgumentNullException.ThrowIfNull(operations);
        ArgumentNullException.ThrowIfNull(credentialGrants);
        ArgumentNullException.ThrowIfNull(deployments);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(readiness);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);
        options.Validate();

        _operations = operations;
        _credentialGrants = credentialGrants;
        _deployments = deployments;
        _options = options;
        _readiness = readiness;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _readiness.MarkStarted();
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                DateTimeOffset now = _timeProvider.GetUtcNow();
                _ = await RunCycleOnceAsync(now, stoppingToken).ConfigureAwait(false);
                await Task.Delay(_options.PollInterval, _timeProvider, stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal host shutdown.
        }
        catch (Exception exception)
        {
            _readiness.MarkNotReady(ControlWorkReadinessCondition.StoreOperationFailed);
            if (_logger.IsEnabled(LogLevel.Critical))
            {
                string exceptionType = exception.GetType().Name;
                LogBackgroundServiceFailure(_logger, exceptionType);
            }

            throw;
        }
        finally
        {
            _readiness.MarkStopped();
        }
    }

    internal async Task<ControlWorkCycleOutcome> RunCycleOnceAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (_readiness.Condition == ControlWorkReadinessCondition.Stopped)
        {
            throw new WorkerWorkstreamStoppedException();
        }

        bool operationsAvailable = await ProbeAsync(
            _operations.IsAvailableAsync,
            cancellationToken).ConfigureAwait(false);
        bool credentialGrantsAvailable = await ProbeAsync(
            _credentialGrants.IsAvailableAsync,
            cancellationToken).ConfigureAwait(false);
        bool deploymentsAvailable = await ProbeAsync(
            _deployments.IsAvailableAsync,
            cancellationToken).ConfigureAwait(false);
        if (!operationsAvailable || !credentialGrantsAvailable || !deploymentsAvailable)
        {
            _readiness.MarkNotReady(ControlWorkReadinessCondition.RequiredDependencyUnavailable);
            return ControlWorkCycleOutcome.RequiredDependencyUnavailable;
        }

        StoreCycleOutcome operationsOutcome = await RunStoreAsync(
            "user_operations",
            token => _operations.RunCycleAsync(now, token),
            cancellationToken).ConfigureAwait(false);
        StoreCycleOutcome credentialGrantsOutcome = await RunStoreAsync(
            "credential_grants",
            token => _credentialGrants.RunCycleAsync(now, token),
            cancellationToken).ConfigureAwait(false);
        StoreCycleOutcome deploymentsOutcome = await RunStoreAsync(
            "deployment_projection",
            token => _deployments.RunCycleAsync(now, token),
            cancellationToken).ConfigureAwait(false);

        if (operationsOutcome == StoreCycleOutcome.OperationFailed ||
            credentialGrantsOutcome == StoreCycleOutcome.OperationFailed ||
            deploymentsOutcome == StoreCycleOutcome.OperationFailed)
        {
            _readiness.MarkNotReady(ControlWorkReadinessCondition.StoreOperationFailed);
            return ControlWorkCycleOutcome.StoreOperationFailed;
        }

        if (operationsOutcome == StoreCycleOutcome.PartialFailure ||
            credentialGrantsOutcome == StoreCycleOutcome.PartialFailure ||
            deploymentsOutcome == StoreCycleOutcome.PartialFailure)
        {
            _readiness.MarkNotReady(ControlWorkReadinessCondition.PartialCycleFailure);
            return ControlWorkCycleOutcome.PartialCycleFailure;
        }

        if (operationsOutcome == StoreCycleOutcome.ScanProgressLagging ||
            credentialGrantsOutcome == StoreCycleOutcome.ScanProgressLagging ||
            deploymentsOutcome == StoreCycleOutcome.ScanProgressLagging)
        {
            _readiness.MarkNotReady(ControlWorkReadinessCondition.ScanProgressLagging);
            return ControlWorkCycleOutcome.ScanProgressLagging;
        }

        if (operationsOutcome == StoreCycleOutcome.OperationBacklogLagging ||
            credentialGrantsOutcome == StoreCycleOutcome.OperationBacklogLagging ||
            deploymentsOutcome == StoreCycleOutcome.OperationBacklogLagging)
        {
            _readiness.MarkNotReady(ControlWorkReadinessCondition.OperationBacklogLagging);
            return ControlWorkCycleOutcome.OperationBacklogLagging;
        }

        _readiness.MarkReady();
        return ControlWorkCycleOutcome.Completed;
    }

    private async Task<bool> ProbeAsync(
        Func<CancellationToken, ValueTask<bool>> probe,
        CancellationToken cancellationToken)
    {
        try
        {
            return await WorkerOperationBoundary.ExecuteAsync(
                    probe,
                    _options.DependencyTimeout,
                    _options.CancellationConfirmationTimeout,
                    _timeProvider,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (WorkerOperationTerminationUnconfirmedException)
        {
            _readiness.MarkStopped();
            throw;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private async Task<StoreCycleOutcome> RunStoreAsync(
        string storeName,
        Func<CancellationToken, Task<ControlWorkCycleResult>> run,
        CancellationToken cancellationToken)
    {
        try
        {
            ControlWorkCycleResult result = await WorkerOperationBoundary.ExecuteAsync(
                    run,
                    _options.OperationTimeout,
                    _options.CancellationConfirmationTimeout,
                    _timeProvider,
                    cancellationToken)
                .ConfigureAwait(false);
            if (result is null)
            {
                LogCycleFailure(_logger, storeName, "InvalidCycleResult");
                return StoreCycleOutcome.OperationFailed;
            }

            if (result.ItemsFailed != 0)
            {
                LogPartialCycle(_logger, storeName, result.ItemsFailed);
                return StoreCycleOutcome.PartialFailure;
            }

            if (!result.ScanRotationHealthy)
            {
                return StoreCycleOutcome.ScanProgressLagging;
            }

            if (!result.OperationBacklogHealthy)
            {
                return StoreCycleOutcome.OperationBacklogLagging;
            }

            return StoreCycleOutcome.Completed;
        }
        catch (WorkerOperationTerminationUnconfirmedException)
        {
            _readiness.MarkStopped();
            throw;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            string exceptionType = exception.GetType().Name;
            LogCycleFailure(_logger, storeName, exceptionType);
            return StoreCycleOutcome.OperationFailed;
        }
    }

    private enum StoreCycleOutcome
    {
        Completed,
        PartialFailure,
        ScanProgressLagging,
        OperationFailed,
        OperationBacklogLagging
    }

    [LoggerMessage(
        EventId = 1101,
        Level = LogLevel.Warning,
        Message = "Control work cycle {StoreName} reported {FailedCount} failed items.")]
    private static partial void LogPartialCycle(ILogger logger, string storeName, int failedCount);

    [LoggerMessage(
        EventId = 1102,
        Level = LogLevel.Error,
        Message = "Control work cycle {StoreName} failed with exception type {ExceptionType}.")]
    private static partial void LogCycleFailure(ILogger logger, string storeName, string exceptionType);

    [LoggerMessage(
        EventId = 1103,
        Level = LogLevel.Critical,
        Message = "Control work background service stopped after exception type {ExceptionType}.")]
    private static partial void LogBackgroundServiceFailure(ILogger logger, string exceptionType);
}
