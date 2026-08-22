namespace YO4X.RuntimeControl.Postgres;

public sealed class RuntimeControlPostgresOptions
{
    public string? ApprovedRuntimeImageDigest { get; init; }

    public TimeSpan AssignmentLifetime { get; init; } = TimeSpan.FromMinutes(10);

    public TimeSpan MaximumEvidenceAge { get; init; } = TimeSpan.FromMinutes(5);

    public TimeSpan MaximumFutureClockSkew { get; init; } = TimeSpan.FromMinutes(1);

    public int MaximumEventPayloadBytes { get; init; } = 64 * 1024;

    public TimeSpan MaximumLeaseLifetime { get; init; } = TimeSpan.FromMinutes(10);

    public TimeSpan MaximumLeaseGracePeriod { get; init; } = TimeSpan.FromMinutes(15);

    public void Validate()
    {
        if (!IsRuntimeImageDigest(ApprovedRuntimeImageDigest))
        {
            throw new InvalidOperationException(
                "RuntimePostgres:ApprovedRuntimeImageDigest must be one exact lowercase sha256 digest.");
        }

        RequireRange(AssignmentLifetime, TimeSpan.FromMinutes(1), TimeSpan.FromHours(1), nameof(AssignmentLifetime));
        RequireRange(MaximumEvidenceAge, TimeSpan.FromSeconds(30), TimeSpan.FromHours(1), nameof(MaximumEvidenceAge));
        RequireRange(MaximumFutureClockSkew, TimeSpan.Zero, TimeSpan.FromMinutes(5), nameof(MaximumFutureClockSkew));
        RequireRange(MaximumLeaseLifetime, TimeSpan.FromMinutes(1), TimeSpan.FromHours(1), nameof(MaximumLeaseLifetime));
        RequireRange(MaximumLeaseGracePeriod, TimeSpan.Zero, TimeSpan.FromHours(1), nameof(MaximumLeaseGracePeriod));
        if (MaximumEventPayloadBytes is < 1024 or > 1024 * 1024)
        {
            throw new InvalidOperationException("MaximumEventPayloadBytes must be between 1 KiB and 1 MiB.");
        }
    }

    private static bool IsRuntimeImageDigest(string? value) => value is { Length: 71 }
        && value.StartsWith("sha256:", StringComparison.Ordinal)
        && value[7..].All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static void RequireRange(TimeSpan value, TimeSpan minimum, TimeSpan maximum, string name)
    {
        if (value < minimum || value > maximum)
        {
            throw new InvalidOperationException($"{name} is outside the supported range.");
        }
    }
}
