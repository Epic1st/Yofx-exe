namespace YO4X.Trading.Application;

public sealed record BrokerCommandCoordinatorOptions
{
    /// <summary>
    /// Explicit composition-root gate for entering a mutation-capable gateway.
    /// It is intentionally false by default and is not exposed by GatewayHost
    /// configuration while that host remains proof-only.
    /// </summary>
    public bool SubmissionEnabled { get; init; }

    public TimeSpan GatewaySendTimeout { get; init; } = TimeSpan.FromMilliseconds(500);

    public TimeSpan GatewayReconciliationTimeout { get; init; } = TimeSpan.FromSeconds(10);

    public TimeSpan DurableWriteTimeout { get; init; } = TimeSpan.FromSeconds(10);

    public TimeSpan AuthoritySafetyMargin { get; init; } = TimeSpan.FromMilliseconds(100);

    public TimeSpan MinimumAuthorityWindow { get; init; } = TimeSpan.FromMilliseconds(600);

    public void Validate()
    {
        RequireRange(GatewaySendTimeout, nameof(GatewaySendTimeout), TimeSpan.FromMilliseconds(10), TimeSpan.FromMinutes(1));
        RequireRange(
            GatewayReconciliationTimeout,
            nameof(GatewayReconciliationTimeout),
            TimeSpan.FromMilliseconds(10),
            TimeSpan.FromMinutes(2));
        RequireRange(DurableWriteTimeout, nameof(DurableWriteTimeout), TimeSpan.FromMilliseconds(10), TimeSpan.FromMinutes(1));
        RequireRange(
            AuthoritySafetyMargin,
            nameof(AuthoritySafetyMargin),
            TimeSpan.FromMilliseconds(10),
            TimeSpan.FromSeconds(30));
        RequireRange(
            MinimumAuthorityWindow,
            nameof(MinimumAuthorityWindow),
            TimeSpan.FromMilliseconds(20),
            TimeSpan.FromMinutes(2));
        if (MinimumAuthorityWindow < GatewaySendTimeout + AuthoritySafetyMargin)
        {
            throw new ArgumentException(
                "The minimum authority window must cover the full gateway send timeout "
                + "and the explicit authority safety margin.",
                nameof(MinimumAuthorityWindow));
        }
    }

    private static void RequireRange(
        TimeSpan value,
        string parameterName,
        TimeSpan minimum,
        TimeSpan maximum)
    {
        if (value < minimum || value > maximum)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                $"The value must be between {minimum} and {maximum}.");
        }
    }
}
