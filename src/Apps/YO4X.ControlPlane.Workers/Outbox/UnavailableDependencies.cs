namespace YO4X.ControlPlane.Workers.Outbox;

public sealed class UnavailablePostgresOutboxStore : IPostgresOutboxStore
{
    public ValueTask<bool> IsAvailableAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult(false);

    public ValueTask<bool> IsScanProgressHealthyAsync(
        TimeSpan maximumRotationAge,
        CancellationToken cancellationToken) =>
        ValueTask.FromResult(false);

    public ValueTask<IReadOnlyList<ClaimedOutboxItem>> ClaimAsync(
        OutboxClaimRequest request,
        CancellationToken cancellationToken) =>
        ValueTask.FromException<IReadOnlyList<ClaimedOutboxItem>>(
            new InvalidOperationException("A PostgreSQL outbox adapter has not been configured."));

    public ValueTask<bool> SettleAsync(
        OutboxSettlement settlement,
        CancellationToken cancellationToken) =>
        ValueTask.FromException<bool>(
            new InvalidOperationException("A PostgreSQL outbox adapter has not been configured."));
}

public sealed class UnavailableOutboxDestination : IOutboxDestination
{
    public ValueTask<bool> IsAvailableAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult(false);

    public ValueTask<OutboxDeliveryResult> DeliverAsync(
        OutboxDeliveryEnvelope message,
        CancellationToken cancellationToken) =>
        ValueTask.FromException<OutboxDeliveryResult>(
            new InvalidOperationException("An outbox destination adapter has not been configured."));
}
