namespace YO4X.Admin.Postgres;

public sealed class AdminPostgresOptions
{
    public TimeSpan ReadAuthenticationMaximumAge { get; init; } = TimeSpan.FromMinutes(30);

    public TimeSpan SensitiveReadAuthenticationMaximumAge { get; init; } = TimeSpan.FromMinutes(10);

    public TimeSpan MutationAuthenticationMaximumAge { get; init; } = TimeSpan.FromMinutes(5);

    public TimeSpan ApprovalAuthenticationMaximumAge { get; init; } = TimeSpan.FromMinutes(5);

    public TimeSpan IdempotencyLifetime { get; init; } = TimeSpan.FromHours(24);

    public TimeSpan ImpactPreviewLifetime { get; init; } = TimeSpan.FromMinutes(10);

    public TimeSpan ApprovalLifetime { get; init; } = TimeSpan.FromMinutes(10);

    public TimeSpan MaximumClockSkew { get; init; } = TimeSpan.FromSeconds(30);

    public int MaximumPageSize { get; init; } = 100;

    public void Validate()
    {
        ValidatePositive(ReadAuthenticationMaximumAge, nameof(ReadAuthenticationMaximumAge));
        ValidatePositive(SensitiveReadAuthenticationMaximumAge, nameof(SensitiveReadAuthenticationMaximumAge));
        ValidatePositive(MutationAuthenticationMaximumAge, nameof(MutationAuthenticationMaximumAge));
        ValidatePositive(ApprovalAuthenticationMaximumAge, nameof(ApprovalAuthenticationMaximumAge));
        ValidatePositive(IdempotencyLifetime, nameof(IdempotencyLifetime));
        ValidatePositive(ImpactPreviewLifetime, nameof(ImpactPreviewLifetime));
        ValidatePositive(ApprovalLifetime, nameof(ApprovalLifetime));

        if (MaximumClockSkew < TimeSpan.Zero || MaximumClockSkew > TimeSpan.FromMinutes(2))
        {
            throw new InvalidOperationException(
                $"{nameof(MaximumClockSkew)} must be between zero and two minutes.");
        }

        if (MaximumPageSize is < 1 or > 500)
        {
            throw new InvalidOperationException(
                $"{nameof(MaximumPageSize)} must be between 1 and 500.");
        }

        if (ApprovalAuthenticationMaximumAge > ReadAuthenticationMaximumAge
            || MutationAuthenticationMaximumAge > ReadAuthenticationMaximumAge
            || SensitiveReadAuthenticationMaximumAge > ReadAuthenticationMaximumAge)
        {
            throw new InvalidOperationException(
                "Sensitive authentication windows cannot exceed the ordinary admin read window.");
        }

        if (ApprovalLifetime > ImpactPreviewLifetime)
        {
            throw new InvalidOperationException(
                "An approval cannot outlive the exact impact preview to which it is bound.");
        }
    }

    private static void ValidatePositive(TimeSpan value, string name)
    {
        if (value <= TimeSpan.Zero)
        {
            throw new InvalidOperationException($"{name} must be positive.");
        }
    }
}
