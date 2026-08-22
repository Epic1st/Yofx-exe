namespace YO4X.ControlPlane.Workers.Operations;

public sealed record ControlWorkCycleResult(
    int TenantsVisited,
    int ItemsExamined,
    int ItemsChanged,
    int ItemsFailed);

public interface IUserOperationWorkStore
{
    ValueTask<bool> IsAvailableAsync(CancellationToken cancellationToken);

    Task<ControlWorkCycleResult> RunCycleAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken);
}

public interface ICredentialGrantExpiryStore
{
    ValueTask<bool> IsAvailableAsync(CancellationToken cancellationToken);

    Task<ControlWorkCycleResult> RunCycleAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken);
}

public interface IDeploymentProjectionStore
{
    ValueTask<bool> IsAvailableAsync(CancellationToken cancellationToken);

    Task<ControlWorkCycleResult> RunCycleAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken);
}

public sealed class UnavailableUserOperationWorkStore : IUserOperationWorkStore
{
    public ValueTask<bool> IsAvailableAsync(CancellationToken cancellationToken) => ValueTask.FromResult(false);

    public Task<ControlWorkCycleResult> RunCycleAsync(DateTimeOffset now, CancellationToken cancellationToken) =>
        Task.FromResult(new ControlWorkCycleResult(0, 0, 0, 0));
}

public sealed class UnavailableCredentialGrantExpiryStore : ICredentialGrantExpiryStore
{
    public ValueTask<bool> IsAvailableAsync(CancellationToken cancellationToken) => ValueTask.FromResult(false);

    public Task<ControlWorkCycleResult> RunCycleAsync(DateTimeOffset now, CancellationToken cancellationToken) =>
        Task.FromResult(new ControlWorkCycleResult(0, 0, 0, 0));
}

public sealed class UnavailableDeploymentProjectionStore : IDeploymentProjectionStore
{
    public ValueTask<bool> IsAvailableAsync(CancellationToken cancellationToken) => ValueTask.FromResult(false);

    public Task<ControlWorkCycleResult> RunCycleAsync(DateTimeOffset now, CancellationToken cancellationToken) =>
        Task.FromResult(new ControlWorkCycleResult(0, 0, 0, 0));
}
