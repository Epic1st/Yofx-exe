namespace YO4X.ControlPlane.Workers.Operations;

public sealed class ControlWorkOptions
{
    public const string SectionName = "ControlWork";

    public int TenantBatchSize { get; init; } = 100;

    public int OperationBatchSizePerTenant { get; init; } = 32;

    public int CleanupBatchSizePerTenant { get; init; } = 32;

    public int DeploymentBatchSizePerTenant { get; init; } = 32;

    /// <summary>
    /// Maximum number of invocation attempts whose database-owned deadlines
    /// may be advanced in one tenant cycle.
    /// </summary>
    public int InvocationTimeoutBatchSizePerTenant { get; init; } = 64;

    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(1);

    public TimeSpan DependencyTimeout { get; init; } = TimeSpan.FromSeconds(2);

    public TimeSpan OperationTimeout { get; init; } = TimeSpan.FromSeconds(30);

    public TimeSpan CancellationConfirmationTimeout { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Maximum accepted age of complete durable tenant and deployment scans.
    /// Operators must capacity-test this SLA against their tenant population.
    /// </summary>
    public TimeSpan MaximumTenantScanRotationAge { get; init; } = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Maximum database-clock age of the oldest user operation that is
    /// currently eligible for dispatch or reconciliation.
    /// </summary>
    public TimeSpan MaximumOperationBacklogAge { get; init; } = TimeSpan.FromMinutes(15);

    public TimeSpan ClaimLease { get; init; } = TimeSpan.FromSeconds(30);

    public TimeSpan ProofUnknownAfter { get; init; } = TimeSpan.FromMinutes(2);

    public TimeSpan OperationExpiresAfter { get; init; } = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Maximum interval between the database-authoritative dispatch instant
    /// and the last instant at which a runtime may begin the mutation.
    /// </summary>
    public TimeSpan DispatchExecutionWindow { get; init; } = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Safety margin subtracted from the current assignment lease when the
    /// database derives the immutable invocation deadline.
    /// </summary>
    public TimeSpan AssignmentProofMargin { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Lifetime of the one-use broker-result capability minted atomically with
    /// a durable dispatch. It permits delayed proof without trusting a
    /// caller-supplied historical assignment timestamp.
    /// </summary>
    public TimeSpan ResultCapabilityLifetime { get; init; } = TimeSpan.FromHours(24);

    public TimeSpan ComponentHeartbeatMaximumAge { get; init; } = TimeSpan.FromSeconds(15);

    public TimeSpan EvidenceFutureClockSkew { get; init; } = TimeSpan.FromSeconds(30);

    public void Validate()
    {
        RequireBatch(TenantBatchSize, nameof(TenantBatchSize));
        RequireBatch(OperationBatchSizePerTenant, nameof(OperationBatchSizePerTenant));
        RequireBatch(CleanupBatchSizePerTenant, nameof(CleanupBatchSizePerTenant));
        RequireBatch(DeploymentBatchSizePerTenant, nameof(DeploymentBatchSizePerTenant));
        RequireBatch(
            InvocationTimeoutBatchSizePerTenant,
            nameof(InvocationTimeoutBatchSizePerTenant),
            maximum: 512);
        RequireRange(PollInterval, TimeSpan.FromMilliseconds(100), TimeSpan.FromMinutes(1), nameof(PollInterval));
        RequireRange(
            DependencyTimeout,
            TimeSpan.FromMilliseconds(100),
            TimeSpan.FromSeconds(30),
            nameof(DependencyTimeout));
        RequireRange(
            OperationTimeout,
            TimeSpan.FromMilliseconds(100),
            TimeSpan.FromMinutes(5),
            nameof(OperationTimeout));
        RequireRange(
            CancellationConfirmationTimeout,
            TimeSpan.FromMilliseconds(100),
            TimeSpan.FromSeconds(10),
            nameof(CancellationConfirmationTimeout));
        RequireRange(
            MaximumTenantScanRotationAge,
            TimeSpan.FromSeconds(10),
            TimeSpan.FromHours(24),
            nameof(MaximumTenantScanRotationAge));
        RequireRange(
            MaximumOperationBacklogAge,
            TimeSpan.FromSeconds(10),
            TimeSpan.FromHours(24),
            nameof(MaximumOperationBacklogAge));
        RequireRange(ClaimLease, TimeSpan.FromSeconds(1), TimeSpan.FromMinutes(5), nameof(ClaimLease));
        RequireRange(ProofUnknownAfter, TimeSpan.FromSeconds(5), TimeSpan.FromHours(1), nameof(ProofUnknownAfter));
        RequireRange(OperationExpiresAfter, ProofUnknownAfter, TimeSpan.FromDays(1), nameof(OperationExpiresAfter));
        RequireRange(
            DispatchExecutionWindow,
            TimeSpan.FromSeconds(15),
            TimeSpan.FromMinutes(5),
            nameof(DispatchExecutionWindow));
        RequireRange(
            AssignmentProofMargin,
            TimeSpan.FromSeconds(1),
            TimeSpan.FromMinutes(1),
            nameof(AssignmentProofMargin));
        RequireRange(
            ResultCapabilityLifetime,
            TimeSpan.FromMinutes(5),
            TimeSpan.FromHours(24),
            nameof(ResultCapabilityLifetime));
        RequireRange(
            ComponentHeartbeatMaximumAge,
            TimeSpan.FromSeconds(1),
            TimeSpan.FromMinutes(5),
            nameof(ComponentHeartbeatMaximumAge));
        RequireRange(
            EvidenceFutureClockSkew,
            TimeSpan.Zero,
            TimeSpan.FromMinutes(5),
            nameof(EvidenceFutureClockSkew));
        if (MaximumOperationBacklogAge < OperationExpiresAfter)
        {
            throw new InvalidOperationException(
                "MaximumOperationBacklogAge cannot be shorter than OperationExpiresAfter.");
        }

        if (MaximumTenantScanRotationAge > MaximumOperationBacklogAge)
        {
            throw new InvalidOperationException(
                "MaximumTenantScanRotationAge cannot exceed MaximumOperationBacklogAge.");
        }
    }

    private static void RequireBatch(int value, string name, int maximum = 1_000)
    {
        if (value < 1 || value > maximum)
        {
            throw new InvalidOperationException(
                $"{name} must be between 1 and {maximum}.");
        }
    }

    private static void RequireRange(TimeSpan value, TimeSpan minimum, TimeSpan maximum, string name)
    {
        if (value < minimum || value > maximum)
        {
            throw new InvalidOperationException($"{name} is outside its supported range.");
        }
    }
}
