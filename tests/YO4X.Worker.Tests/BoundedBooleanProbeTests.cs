using YO4X.BuildingBlocks;

namespace YO4X.Worker.Tests;

public sealed class BoundedBooleanProbeTests
{
    [Fact]
    public async Task ConcurrentCallersShareOneProbe()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int invocations = 0;
        var snapshot = new BoundedBooleanProbe(
            async cancellationToken =>
            {
                Interlocked.Increment(ref invocations);
                entered.TrySetResult();
                await release.Task.WaitAsync(cancellationToken);
                return true;
            },
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(5));

        Task<bool>[] callers = Enumerable.Range(0, 32)
            .Select(_ => snapshot.GetAsync(TestContext.Current.CancellationToken).AsTask())
            .ToArray();
        await entered.Task.WaitAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, Volatile.Read(ref invocations));
        release.TrySetResult();
        Assert.All(await Task.WhenAll(callers), static result => Assert.True(result));
        Assert.Equal(1, Volatile.Read(ref invocations));
    }

    [Fact]
    public async Task CancelingOneCallerDoesNotCancelTheSharedProbe()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int invocations = 0;
        var snapshot = new BoundedBooleanProbe(
            async cancellationToken =>
            {
                Interlocked.Increment(ref invocations);
                entered.TrySetResult();
                await release.Task.WaitAsync(cancellationToken);
                return true;
            },
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(5));
        using var callerCancellation = new CancellationTokenSource();

        Task<bool> canceledCaller = snapshot.GetAsync(callerCancellation.Token).AsTask();
        await entered.Task.WaitAsync(TestContext.Current.CancellationToken);
        callerCancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => canceledCaller);

        Task<bool> remainingCaller = snapshot.GetAsync(
            TestContext.Current.CancellationToken).AsTask();
        release.TrySetResult();

        Assert.True(await remainingCaller);
        Assert.Equal(1, Volatile.Read(ref invocations));
    }

    [Fact]
    public async Task AlreadyCanceledCallerCannotUseCachedTrueResult()
    {
        var snapshot = new BoundedBooleanProbe(
            _ => ValueTask.FromResult(true),
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(5));
        Assert.True(await snapshot.GetAsync(TestContext.Current.CancellationToken));
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => snapshot.GetAsync(canceled.Token).AsTask());
    }

    [Fact]
    public async Task SynchronousAndAsynchronousProbeExceptionsAreRedactedAsFalse()
    {
        var synchronous = new BoundedBooleanProbe(
            _ => throw new InvalidOperationException("sensitive synchronous failure"),
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(5));
        var asynchronous = new BoundedBooleanProbe(
            _ => ValueTask.FromException<bool>(
                new InvalidOperationException("sensitive asynchronous failure")),
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(5));

        Assert.False(await synchronous.GetAsync(TestContext.Current.CancellationToken));
        Assert.False(await asynchronous.GetAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SynchronouslyBlockingDependencyIsStillBoundedAndSingleFlight()
    {
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int invocations = 0;
        var snapshot = new BoundedBooleanProbe(
            _ =>
            {
                Interlocked.Increment(ref invocations);
                entered.TrySetResult();
                release.Wait(CancellationToken.None);
                return ValueTask.FromResult(true);
            },
            TimeSpan.FromMilliseconds(10),
            TimeSpan.FromMilliseconds(50));

        Task<bool> timedOut = snapshot.GetAsync(TestContext.Current.CancellationToken).AsTask();
        await entered.Task.WaitAsync(TestContext.Current.CancellationToken);
        Assert.False(await timedOut.WaitAsync(TestContext.Current.CancellationToken));
        await Task.Delay(TimeSpan.FromMilliseconds(25), TestContext.Current.CancellationToken);

        Assert.False(await snapshot.GetAsync(TestContext.Current.CancellationToken));
        Assert.Equal(1, Volatile.Read(ref invocations));

        release.Set();
    }

    [Fact]
    public async Task TimedOutDependencyThatIgnoresCancellationRemainsTheSingleFlight()
    {
        var releaseLateProbe = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        int invocations = 0;
        int active = 0;
        int maximumActive = 0;
        var snapshot = new BoundedBooleanProbe(
            async _ =>
            {
                int invocation = Interlocked.Increment(ref invocations);
                int concurrent = Interlocked.Increment(ref active);
                UpdateMaximum(ref maximumActive, concurrent);
                try
                {
                    return invocation == 1
                        ? await releaseLateProbe.Task
                        : true;
                }
                finally
                {
                    Interlocked.Decrement(ref active);
                }
            },
            TimeSpan.FromMilliseconds(10),
            TimeSpan.FromMilliseconds(50));

        Assert.False(await snapshot.GetAsync(TestContext.Current.CancellationToken));
        await Task.Delay(TimeSpan.FromMilliseconds(25), TestContext.Current.CancellationToken);

        Assert.False(await snapshot.GetAsync(TestContext.Current.CancellationToken));
        Assert.Equal(1, Volatile.Read(ref invocations));
        Assert.Equal(1, Volatile.Read(ref maximumActive));

        releaseLateProbe.TrySetResult(true);
        bool recovered = false;
        for (int attempt = 0; attempt < 20 && !recovered; attempt++)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(10), TestContext.Current.CancellationToken);
            recovered = await snapshot.GetAsync(TestContext.Current.CancellationToken);
        }

        Assert.True(recovered);
        Assert.Equal(2, Volatile.Read(ref invocations));
        Assert.Equal(1, Volatile.Read(ref maximumActive));
    }

    private static void UpdateMaximum(ref int maximum, int candidate)
    {
        int observed = Volatile.Read(ref maximum);
        while (candidate > observed)
        {
            int original = Interlocked.CompareExchange(ref maximum, candidate, observed);
            if (original == observed)
            {
                return;
            }

            observed = original;
        }
    }
}
