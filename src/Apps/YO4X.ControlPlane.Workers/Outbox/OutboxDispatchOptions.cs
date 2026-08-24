namespace YO4X.ControlPlane.Workers.Outbox;

public sealed class OutboxDispatchOptions
{
    public const string SectionName = "OutboxDispatch";

    public int BatchSize { get; init; } = 100;

    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(1);

    public TimeSpan ClaimLease { get; init; } = TimeSpan.FromSeconds(30);

    public TimeSpan DependencyTimeout { get; init; } = TimeSpan.FromSeconds(2);

    public TimeSpan DeliveryTimeout { get; init; } = TimeSpan.FromSeconds(10);

    public TimeSpan CancellationConfirmationTimeout { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Maximum accepted age of a complete durable tenant scan. Size this SLA
    /// for the provisioned tenant count and measured worst-case cycle time.
    /// </summary>
    public TimeSpan MaximumTenantScanRotationAge { get; init; } = TimeSpan.FromMinutes(15);

    public int MaximumAttempts { get; init; } = 8;

    public TimeSpan BaseRetryDelay { get; init; } = TimeSpan.FromSeconds(1);

    public TimeSpan MaximumRetryDelay { get; init; } = TimeSpan.FromMinutes(5);

    public TimeSpan MaximumRetryJitter { get; init; } = TimeSpan.FromMilliseconds(250);

    public int MaximumPayloadBytes { get; init; } = 1_048_576;

    public void Validate()
    {
        if (BatchSize is < 1 or > 1_000)
        {
            throw new InvalidOperationException("Outbox batch size must be between 1 and 1000.");
        }

        RequireRange(PollInterval, TimeSpan.FromMilliseconds(10), TimeSpan.FromMinutes(1), nameof(PollInterval));
        RequireRange(ClaimLease, TimeSpan.FromSeconds(1), TimeSpan.FromMinutes(10), nameof(ClaimLease));
        RequireRange(DependencyTimeout, TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(30), nameof(DependencyTimeout));
        RequireRange(DeliveryTimeout, TimeSpan.FromMilliseconds(100), TimeSpan.FromMinutes(2), nameof(DeliveryTimeout));
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

        if (MaximumAttempts is < 1 or > 100)
        {
            throw new InvalidOperationException("Maximum attempts must be between 1 and 100.");
        }

        RequireRange(BaseRetryDelay, TimeSpan.FromMilliseconds(10), TimeSpan.FromHours(24), nameof(BaseRetryDelay));
        RequireRange(MaximumRetryDelay, BaseRetryDelay, TimeSpan.FromHours(24), nameof(MaximumRetryDelay));
        RequireRange(MaximumRetryJitter, TimeSpan.Zero, MaximumRetryDelay, nameof(MaximumRetryJitter));

        if (MaximumPayloadBytes is < 1 or > 16_777_216)
        {
            throw new InvalidOperationException("Maximum payload bytes must be between 1 and 16777216.");
        }
    }

    private static void RequireRange(TimeSpan value, TimeSpan minimum, TimeSpan maximum, string optionName)
    {
        if (value < minimum || value > maximum)
        {
            throw new InvalidOperationException($"{optionName} is outside its supported range.");
        }
    }
}
