namespace YO4X.Trading.Application;

public sealed record BrokerCommandCoordinatorOptions
{
    public TimeSpan GatewaySendTimeout { get; init; } = TimeSpan.FromSeconds(5);

    public TimeSpan GatewayReconciliationTimeout { get; init; } = TimeSpan.FromSeconds(10);

    public TimeSpan DurableWriteTimeout { get; init; } = TimeSpan.FromSeconds(10);

    public TimeSpan MinimumAuthorityWindow { get; init; } = TimeSpan.FromMilliseconds(10);

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
            MinimumAuthorityWindow,
            nameof(MinimumAuthorityWindow),
            TimeSpan.Zero,
            TimeSpan.FromSeconds(5));
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
