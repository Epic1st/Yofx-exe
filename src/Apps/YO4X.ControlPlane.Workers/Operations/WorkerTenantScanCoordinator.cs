namespace YO4X.ControlPlane.Workers.Operations;

internal enum WorkerTenantScanConsumer
{
    Outbox,
    CredentialGrantExpiry,
    DeploymentProjection,
    UserOperations
}

internal readonly record struct WorkerTenantScanStep(
    Guid TenantId,
    bool RotationCompleted,
    long RotationCount)
{
    public void Validate()
    {
        if (TenantId == Guid.Empty
            || RotationCount < 0
            || (RotationCompleted && RotationCount == 0))
        {
            throw new InvalidOperationException(
                "The durable tenant scan returned invalid progress metadata.");
        }
    }
}

/// <summary>
/// Serializes each fixed worker workstream inside one process and bounds the
/// number of tenants it can acquire per cycle. Cursor ownership remains in
/// PostgreSQL so a restart or another worker instance cannot reset progress.
/// </summary>
internal sealed class WorkerTenantScanCoordinator
{
    private const int MaximumSupportedTenantCount = 1_000;

    private readonly SemaphoreSlim[] consumerGates =
    [
        new(1, 1),
        new(1, 1),
        new(1, 1),
        new(1, 1)
    ];

    private readonly int maximumTenantsPerCycle;

    public WorkerTenantScanCoordinator(int maximumTenantsPerCycle)
    {
        if (maximumTenantsPerCycle is < 1 or > MaximumSupportedTenantCount)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumTenantsPerCycle));
        }

        this.maximumTenantsPerCycle = maximumTenantsPerCycle;
    }

    public async ValueTask<WorkerTenantScanLease> AcquireAsync(
        WorkerTenantScanConsumer consumer,
        Func<WorkerTenantScanConsumer, long?, CancellationToken,
            ValueTask<WorkerTenantScanStep?>> advanceDurableCursor,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(advanceDurableCursor);
        int consumerIndex = GetConsumerIndex(consumer);
        SemaphoreSlim consumerGate = consumerGates[consumerIndex];
        await consumerGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        return new WorkerTenantScanLease(
            consumer,
            consumerGate,
            advanceDurableCursor,
            maximumTenantsPerCycle);
    }

    private static int GetConsumerIndex(WorkerTenantScanConsumer consumer) =>
        consumer switch
        {
            WorkerTenantScanConsumer.Outbox => 0,
            WorkerTenantScanConsumer.CredentialGrantExpiry => 1,
            WorkerTenantScanConsumer.DeploymentProjection => 2,
            WorkerTenantScanConsumer.UserOperations => 3,
            _ => throw new ArgumentOutOfRangeException(nameof(consumer))
        };
}

/// <summary>
/// Advances the durable cursor immediately before handing a tenant to its
/// caller. A crash can defer that tenant until the next rotation, but cannot
/// reset the cursor and indefinitely starve later tenants.
/// </summary>
internal sealed class WorkerTenantScanLease : IAsyncDisposable
{
    private readonly WorkerTenantScanConsumer consumer;
    private readonly SemaphoreSlim consumerGate;
    private readonly Func<WorkerTenantScanConsumer, long?, CancellationToken,
        ValueTask<WorkerTenantScanStep?>> advanceDurableCursor;
    private readonly SemaphoreSlim lifecycleGate = new(1, 1);
    private readonly HashSet<Guid> acquiredTenantIds = [];
    private readonly int maximumTenants;
    private int acquiredTenants;
    private long? rotationCeiling;
    private bool exhausted;
    private bool disposed;

    internal WorkerTenantScanLease(
        WorkerTenantScanConsumer consumer,
        SemaphoreSlim consumerGate,
        Func<WorkerTenantScanConsumer, long?, CancellationToken,
            ValueTask<WorkerTenantScanStep?>> advanceDurableCursor,
        int maximumTenants)
    {
        this.consumer = consumer;
        this.consumerGate = consumerGate;
        this.advanceDurableCursor = advanceDurableCursor;
        this.maximumTenants = maximumTenants;
    }

    public int AcquiredTenantCount => Volatile.Read(ref acquiredTenants);

    public async ValueTask<WorkerTenantScanStep?> TryBeginNextAsync(
        CancellationToken cancellationToken)
    {
        await lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (exhausted || acquiredTenants >= maximumTenants)
            {
                return null;
            }

            WorkerTenantScanStep? step = await advanceDurableCursor(
                    consumer,
                    rotationCeiling,
                    cancellationToken)
                .ConfigureAwait(false);
            if (step is not { } durableStep)
            {
                exhausted = true;
                return null;
            }

            durableStep.Validate();
            if (!acquiredTenantIds.Add(durableStep.TenantId))
            {
                // PostgreSQL has completed a full rotation. The duplicate is
                // deliberately not handed to the workstream in this cycle.
                exhausted = true;
                return null;
            }

            if (rotationCeiling is null)
            {
                rotationCeiling = durableStep.RotationCompleted
                    ? durableStep.RotationCount
                    : checked(durableStep.RotationCount + 1);
            }
            else if (durableStep.RotationCount > rotationCeiling.Value)
            {
                throw new InvalidOperationException(
                    "The durable tenant scan exceeded its rotation boundary.");
            }

            acquiredTenants++;
            return durableStep;
        }
        finally
        {
            lifecycleGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await lifecycleGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            consumerGate.Release();
        }
        finally
        {
            lifecycleGate.Release();
        }
    }
}
