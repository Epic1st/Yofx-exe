namespace YO4X.ControlPlane.Workers.Operations;

public sealed class ControlWorkOptions
{
    public const string SectionName = "ControlWork";

    public int TenantBatchSize { get; init; } = 100;

    public int OperationBatchSizePerTenant { get; init; } = 32;

    public int CleanupBatchSizePerTenant { get; init; } = 32;

    public int DeploymentBatchSizePerTenant { get; init; } = 32;

    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(1);

    public TimeSpan ClaimLease { get; init; } = TimeSpan.FromSeconds(30);

    public TimeSpan ProofUnknownAfter { get; init; } = TimeSpan.FromMinutes(2);

    public TimeSpan OperationExpiresAfter { get; init; } = TimeSpan.FromMinutes(15);

    public TimeSpan ComponentHeartbeatMaximumAge { get; init; } = TimeSpan.FromSeconds(15);

    public TimeSpan EvidenceFutureClockSkew { get; init; } = TimeSpan.FromSeconds(30);

    public void Validate()
    {
        RequireBatch(TenantBatchSize, nameof(TenantBatchSize));
        RequireBatch(OperationBatchSizePerTenant, nameof(OperationBatchSizePerTenant));
        RequireBatch(CleanupBatchSizePerTenant, nameof(CleanupBatchSizePerTenant));
        RequireBatch(DeploymentBatchSizePerTenant, nameof(DeploymentBatchSizePerTenant));
        RequireRange(PollInterval, TimeSpan.FromMilliseconds(100), TimeSpan.FromMinutes(1), nameof(PollInterval));
        RequireRange(ClaimLease, TimeSpan.FromSeconds(1), TimeSpan.FromMinutes(5), nameof(ClaimLease));
        RequireRange(ProofUnknownAfter, TimeSpan.FromSeconds(5), TimeSpan.FromHours(1), nameof(ProofUnknownAfter));
        RequireRange(OperationExpiresAfter, ProofUnknownAfter, TimeSpan.FromDays(1), nameof(OperationExpiresAfter));
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
    }

    private static void RequireBatch(int value, string name)
    {
        if (value is < 1 or > 1_000)
        {
            throw new InvalidOperationException($"{name} must be between 1 and 1000.");
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
