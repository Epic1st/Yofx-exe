using System.Collections.Concurrent;

namespace YO4X.RuntimeControl.Postgres;

/// <summary>
/// Coalesces only overlapping, byte-equivalent uses of one stable protocol
/// identity. The shared transition is deliberately independent of any one
/// waiter's cancellation so a canceled transport cannot trigger a second
/// irreversible database transition while the first is still running.
/// </summary>
internal sealed class UserOperationProtocolSingleFlight<TResult>
{
    private readonly ConcurrentDictionary<Guid, Flight> flights = new();

    public async Task<TResult> RunAsync(
        Guid protocolIdentity,
        string exactRequestFingerprint,
        Func<CancellationToken, Task<TResult>> transition,
        CancellationToken cancellationToken)
    {
        if (protocolIdentity == Guid.Empty)
        {
            throw new ArgumentException(
                "A stable protocol identity is required.",
                nameof(protocolIdentity));
        }

        if (!UserOperationProtocolPostgresCommand.IsSha256(exactRequestFingerprint))
        {
            throw new ArgumentException(
                "An exact request fingerprint is required.",
                nameof(exactRequestFingerprint));
        }

        ArgumentNullException.ThrowIfNull(transition);
        cancellationToken.ThrowIfCancellationRequested();

        var candidate = new Flight(
            this,
            protocolIdentity,
            exactRequestFingerprint,
            transition);
        Flight selected = flights.GetOrAdd(protocolIdentity, candidate);
        if (!string.Equals(
                selected.ExactRequestFingerprint,
                exactRequestFingerprint,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "A concurrent protocol transition conflicts with the stable identity.");
        }

        return await selected.SharedTask.Value
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<TResult> ExecuteAndRemoveAsync(
        Guid protocolIdentity,
        Flight flight,
        Func<CancellationToken, Task<TResult>> transition)
    {
        try
        {
            return await transition(CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            if (flights.TryGetValue(protocolIdentity, out Flight? current)
                && ReferenceEquals(current, flight))
            {
                flights.TryRemove(protocolIdentity, out _);
            }
        }
    }

    private sealed class Flight
    {
        public Flight(
            UserOperationProtocolSingleFlight<TResult> owner,
            Guid protocolIdentity,
            string exactRequestFingerprint,
            Func<CancellationToken, Task<TResult>> transition)
        {
            ExactRequestFingerprint = exactRequestFingerprint;
            SharedTask = new Lazy<Task<TResult>>(
                () => ObserveFault(
                    owner.ExecuteAndRemoveAsync(
                        protocolIdentity,
                        this,
                        transition)),
                LazyThreadSafetyMode.ExecutionAndPublication);
        }

        public string ExactRequestFingerprint { get; }

        public Lazy<Task<TResult>> SharedTask { get; }

        private static Task<TResult> ObserveFault(Task<TResult> task)
        {
            _ = task.ContinueWith(
                static completed => _ = completed.Exception,
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously
                    | TaskContinuationOptions.OnlyOnFaulted,
                TaskScheduler.Default);
            return task;
        }
    }
}
