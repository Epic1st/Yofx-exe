namespace YO4X.RuntimeControl.Postgres;

public sealed class UserOperationInvocationPostgresOptions
{
    public TimeSpan DeliveryClaimLifetime { get; init; } = TimeSpan.FromSeconds(30);

    public TimeSpan GatewayReceiptLifetime { get; init; } = TimeSpan.FromMinutes(2);

    public TimeSpan AmbiguityPersistenceTimeout { get; init; } = TimeSpan.FromSeconds(10);

    public void Validate()
    {
        RequireRange(
            DeliveryClaimLifetime,
            TimeSpan.FromSeconds(1),
            TimeSpan.FromMinutes(2),
            nameof(DeliveryClaimLifetime));
        RequireRange(
            GatewayReceiptLifetime,
            TimeSpan.FromSeconds(15),
            TimeSpan.FromMinutes(5),
            nameof(GatewayReceiptLifetime));
        RequireRange(
            AmbiguityPersistenceTimeout,
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(30),
            nameof(AmbiguityPersistenceTimeout));
    }

    private static void RequireRange(
        TimeSpan value,
        TimeSpan minimum,
        TimeSpan maximum,
        string name)
    {
        if (value < minimum || value > maximum)
        {
            throw new InvalidOperationException($"{name} is outside its supported range.");
        }
    }
}
