namespace YO4X.ControlPlane.Workers.Outbox;

public sealed partial class OutboxDispatcherBackgroundService : BackgroundService
{
    private readonly OutboxDispatchCoordinator _coordinator;
    private readonly OutboxDispatchOptions _options;
    private readonly OutboxWorkerReadiness _readiness;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<OutboxDispatcherBackgroundService> _logger;

    public OutboxDispatcherBackgroundService(
        OutboxDispatchCoordinator coordinator,
        OutboxDispatchOptions options,
        OutboxWorkerReadiness readiness,
        TimeProvider timeProvider,
        ILogger<OutboxDispatcherBackgroundService> logger)
    {
        ArgumentNullException.ThrowIfNull(coordinator);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(readiness);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);
        options.Validate();

        _coordinator = coordinator;
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
                try
                {
                    _ = await _coordinator.RunCycleAsync(stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exception)
                {
                    _readiness.MarkNotReady(OutboxReadinessCondition.StoreOperationFailed);
                    LogCycleFailure(_logger, exception.GetType().Name);
                }

                await Task.Delay(_options.PollInterval, _timeProvider, stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal host shutdown.
        }
        finally
        {
            _readiness.MarkStopped();
        }
    }

    [LoggerMessage(
        EventId = 1001,
        Level = LogLevel.Error,
        Message = "Outbox dispatch cycle failed with exception type {ExceptionType}.")]
    private static partial void LogCycleFailure(ILogger logger, string exceptionType);
}
