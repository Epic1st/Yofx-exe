namespace YO4X.BuildingBlocks;

/// <summary>
/// Coalesces one fixed-cardinality Boolean dependency probe, retains its most
/// recent redacted result for a bounded monotonic interval, and applies an
/// independent deadline that is not owned by any caller.
/// </summary>
public sealed class BoundedBooleanProbe
{
    private readonly object sync = new();
    private readonly Func<CancellationToken, ValueTask<bool>> probe;
    private readonly TimeProvider timeProvider;
    private readonly TimeSpan lifetime;
    private readonly TimeSpan probeTimeout;
    private Snapshot? lastCompleted;
    private Task<Snapshot>? inFlight;

    public BoundedBooleanProbe(
        Func<CancellationToken, ValueTask<bool>> probe,
        TimeSpan lifetime,
        TimeSpan probeTimeout,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(probe);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(lifetime, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(probeTimeout, TimeSpan.Zero);

        this.probe = probe;
        this.lifetime = lifetime;
        this.probeTimeout = probeTimeout;
        this.timeProvider = timeProvider ?? TimeProvider.System;
    }

    public ValueTask<bool> GetAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Task<Snapshot> sharedProbe;
        lock (sync)
        {
            long now = timeProvider.GetTimestamp();
            if (lastCompleted is { } completed
                && timeProvider.GetElapsedTime(completed.Timestamp, now) <= lifetime)
            {
                return ValueTask.FromResult(completed.Value);
            }

            if (inFlight is null)
            {
                var completion = new TaskCompletionSource<Snapshot>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                sharedProbe = completion.Task;
                inFlight = sharedProbe;
                _ = ExecuteProbeAsync(completion);
            }
            else
            {
                sharedProbe = inFlight;
            }
        }

        return AwaitSharedProbeAsync(sharedProbe, cancellationToken);
    }

    private async Task ExecuteProbeAsync(TaskCompletionSource<Snapshot> completion)
    {
        Task<bool>? probeTask = null;
        try
        {
            using var timeout = new CancellationTokenSource(probeTimeout, timeProvider);
            probeTask = Task.Run(
                async () => await probe(timeout.Token).ConfigureAwait(false),
                CancellationToken.None);

            bool value = false;
            try
            {
                value = await probeTask
                    .WaitAsync(probeTimeout, timeProvider, CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is OperationCanceledException
                                               or TimeoutException)
            {
                // Dependency timeouts and cooperative cancellation fail closed.
            }
            catch (Exception)
            {
                // This primitive is a redacted boundary; dependency details do not escape.
            }

            bool dependencyCompleted = probeTask.IsCompleted;
            PublishSnapshot(completion, value, dependencyCompleted);

            if (!dependencyCompleted)
            {
                await ObserveLateProbeAsync(probeTask, completion.Task).ConfigureAwait(false);
            }
        }
        catch (Exception)
        {
            PublishSnapshot(completion, value: false, releaseSingleFlight: true);
        }
    }

    private void PublishSnapshot(
        TaskCompletionSource<Snapshot> completion,
        bool value,
        bool releaseSingleFlight)
    {
        var snapshot = new Snapshot(value, timeProvider.GetTimestamp());
        lock (sync)
        {
            lastCompleted = snapshot;
            if (releaseSingleFlight && ReferenceEquals(inFlight, completion.Task))
            {
                inFlight = null;
            }
        }

        completion.TrySetResult(snapshot);
    }

    private async Task ObserveLateProbeAsync(
        Task<bool> probeTask,
        Task<Snapshot> singleFlight)
    {
        try
        {
            await probeTask.ConfigureAwait(false);
        }
        catch (Exception)
        {
            // The false snapshot is already public; observe the terminal task.
        }
        finally
        {
            lock (sync)
            {
                if (ReferenceEquals(inFlight, singleFlight))
                {
                    inFlight = null;
                }
            }
        }
    }

    private static async ValueTask<bool> AwaitSharedProbeAsync(
        Task<Snapshot> sharedProbe,
        CancellationToken cancellationToken)
    {
        Snapshot completed = await sharedProbe.WaitAsync(cancellationToken).ConfigureAwait(false);
        return completed.Value;
    }

    private sealed record Snapshot(bool Value, long Timestamp);
}
