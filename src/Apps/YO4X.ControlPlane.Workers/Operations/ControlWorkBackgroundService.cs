namespace YO4X.ControlPlane.Workers.Operations;

public sealed partial class ControlWorkBackgroundService(
    IUserOperationWorkStore operations,
    ICredentialGrantExpiryStore credentialGrants,
    IDeploymentProjectionStore deployments,
    ControlWorkOptions options,
    TimeProvider timeProvider,
    ILogger<ControlWorkBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                DateTimeOffset now = timeProvider.GetUtcNow();
                await RunStoreAsync("user_operations", () => operations.RunCycleAsync(now, stoppingToken))
                    .ConfigureAwait(false);
                await RunStoreAsync("credential_grants", () => credentialGrants.RunCycleAsync(now, stoppingToken))
                    .ConfigureAwait(false);
                await RunStoreAsync("deployment_projection", () => deployments.RunCycleAsync(now, stoppingToken))
                    .ConfigureAwait(false);
                await Task.Delay(options.PollInterval, timeProvider, stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal host shutdown.
        }
    }

    private async Task RunStoreAsync(string storeName, Func<Task<ControlWorkCycleResult>> run)
    {
        try
        {
            ControlWorkCycleResult result = await run().ConfigureAwait(false);
            if (result.ItemsFailed != 0)
            {
                LogPartialCycle(logger, storeName, result.ItemsFailed);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            LogCycleFailure(logger, storeName, exception.GetType().Name);
        }
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
}
