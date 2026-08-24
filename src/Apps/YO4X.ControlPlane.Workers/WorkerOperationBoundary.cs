namespace YO4X.ControlPlane.Workers;

internal sealed class WorkerOperationTimedOutException : Exception
{
    internal WorkerOperationTimedOutException()
        : base("The bounded worker operation timed out after termination was observed.")
    {
    }
}

internal sealed class WorkerOperationTerminationUnconfirmedException : Exception
{
    internal WorkerOperationTerminationUnconfirmedException()
        : base("The bounded worker operation failed to confirm termination.")
    {
    }
}

internal sealed class WorkerWorkstreamStoppedException : Exception
{
    internal WorkerWorkstreamStoppedException()
        : base("The worker workstream is terminally stopped.")
    {
    }
}

internal static class WorkerOperationBoundary
{
    internal static Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        TimeSpan operationTimeout,
        TimeSpan cancellationConfirmationTimeout,
        TimeProvider timeProvider,
        CancellationToken cancellationToken) =>
        ExecuteCoreAsync(
            operation,
            operationTimeout,
            cancellationConfirmationTimeout,
            timeProvider,
            cancellationToken);

    internal static Task<T> ExecuteAsync<T>(
        Func<CancellationToken, ValueTask<T>> operation,
        TimeSpan operationTimeout,
        TimeSpan cancellationConfirmationTimeout,
        TimeProvider timeProvider,
        CancellationToken cancellationToken) =>
        ExecuteCoreAsync(
            token => operation(token).AsTask(),
            operationTimeout,
            cancellationConfirmationTimeout,
            timeProvider,
            cancellationToken);

    private static async Task<T> ExecuteCoreAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        TimeSpan operationTimeout,
        TimeSpan cancellationConfirmationTimeout,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(timeProvider);
        cancellationToken.ThrowIfCancellationRequested();

        using var operationCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        Task<T> operationTask = operation(operationCancellation.Token)
            ?? throw new InvalidOperationException("The worker dependency returned no operation task.");
        try
        {
            return await operationTask.WaitAsync(
                    operationTimeout,
                    timeProvider,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            TryCancel(operationCancellation);
            bool terminationObserved = await ObserveTerminationAsync(
                    operationTask,
                    cancellationConfirmationTimeout,
                    timeProvider)
                .ConfigureAwait(false);
            if (!terminationObserved)
            {
                _ = ObserveLateCompletionAsync(operationTask);
                throw new WorkerOperationTerminationUnconfirmedException();
            }

            throw new WorkerOperationTimedOutException();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            TryCancel(operationCancellation);
            bool terminationObserved = await ObserveTerminationAsync(
                    operationTask,
                    cancellationConfirmationTimeout,
                    timeProvider)
                .ConfigureAwait(false);
            if (!terminationObserved)
            {
                _ = ObserveLateCompletionAsync(operationTask);
                throw new WorkerOperationTerminationUnconfirmedException();
            }

            throw;
        }
    }

    private static async Task<bool> ObserveTerminationAsync(
        Task operationTask,
        TimeSpan cancellationConfirmationTimeout,
        TimeProvider timeProvider)
    {
        try
        {
            await operationTask.WaitAsync(
                    cancellationConfirmationTimeout,
                    timeProvider,
                    CancellationToken.None)
                .ConfigureAwait(false);
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
        catch (WorkerOperationTerminationUnconfirmedException)
        {
            // A nested boundary has already established that its underlying
            // work may still be running. Propagate that fail-stop signal.
            throw;
        }
        catch
        {
            // Cancellation or failure is terminal and therefore confirms that
            // a later cycle cannot overlap this operation.
            return true;
        }
    }

    private static async Task ObserveLateCompletionAsync(Task operationTask)
    {
        try
        {
            await operationTask.ConfigureAwait(false);
        }
        catch
        {
            // Observation only; dependency details are never logged.
        }
    }

    private static void TryCancel(CancellationTokenSource cancellation)
    {
        try
        {
            cancellation.Cancel();
        }
        catch
        {
            // Failure to signal cancellation is reflected by the subsequent
            // termination-confirmation result.
        }
    }
}
