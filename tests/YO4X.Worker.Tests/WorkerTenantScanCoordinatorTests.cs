using YO4X.ControlPlane.Workers.Operations;

namespace YO4X.Worker.Tests;

public sealed class WorkerTenantScanCoordinatorTests
{
    [Fact]
    public async Task DurableProgressSurvivesCoordinatorReplacementAndWraps()
    {
        Guid[] tenants = TenantIds(5);
        var durableStore = new FakeDurableCursorStore(tenants);

        Assert.Equal(
            tenants[..2],
            await ReadCycleAsync(
                new WorkerTenantScanCoordinator(2),
                WorkerTenantScanConsumer.UserOperations,
                durableStore.AdvanceAsync));
        Assert.Equal(
            tenants[2..4],
            await ReadCycleAsync(
                new WorkerTenantScanCoordinator(2),
                WorkerTenantScanConsumer.UserOperations,
                durableStore.AdvanceAsync));
        Assert.Equal(
            new[] { tenants[4], tenants[0] },
            await ReadCycleAsync(
                new WorkerTenantScanCoordinator(2),
                WorkerTenantScanConsumer.UserOperations,
                durableStore.AdvanceAsync));
    }

    [Fact]
    public async Task EarlyOutboxSaturationCannotResetProgressOnRestart()
    {
        Guid[] tenants = TenantIds(3);
        var durableStore = new FakeDurableCursorStore(tenants);
        var attempted = new List<Guid>();

        for (int restart = 0; restart < tenants.Length; restart++)
        {
            var coordinator = new WorkerTenantScanCoordinator(tenants.Length);
            await using WorkerTenantScanLease scan = await coordinator.AcquireAsync(
                WorkerTenantScanConsumer.Outbox,
                durableStore.AdvanceAsync,
                TestContext.Current.CancellationToken);
            WorkerTenantScanStep? step = await scan.TryBeginNextAsync(
                TestContext.Current.CancellationToken);
            attempted.Add(Assert.IsType<WorkerTenantScanStep>(step).TenantId);
            // Simulates one busy tenant filling MaximumMessages before restart.
        }

        Assert.Equal(tenants, attempted);
    }

    [Fact]
    public async Task RepeatedTenantFailureStillAdvancesDurably()
    {
        Guid[] tenants = TenantIds(3);
        var durableStore = new FakeDurableCursorStore(tenants);
        var attempted = new List<Guid>();

        for (int restart = 0; restart < tenants.Length; restart++)
        {
            var coordinator = new WorkerTenantScanCoordinator(1);
            await using WorkerTenantScanLease scan = await coordinator.AcquireAsync(
                WorkerTenantScanConsumer.CredentialGrantExpiry,
                durableStore.AdvanceAsync,
                TestContext.Current.CancellationToken);
            WorkerTenantScanStep? step = await scan.TryBeginNextAsync(
                TestContext.Current.CancellationToken);
            attempted.Add(Assert.IsType<WorkerTenantScanStep>(step).TenantId);
            // The cursor is committed before the synthetic work failure.
        }

        Assert.Equal(tenants, attempted);
    }

    [Fact]
    public async Task AShortCatalogIsVisitedOnlyOncePerCycle()
    {
        Guid[] tenants = TenantIds(2);
        var durableStore = new FakeDurableCursorStore(tenants);
        var coordinator = new WorkerTenantScanCoordinator(100);

        Guid[] visited = await ReadCycleAsync(
            coordinator,
            WorkerTenantScanConsumer.DeploymentProjection,
            durableStore.AdvanceAsync);

        Assert.Equal(tenants, visited);
        Assert.Equal(1, durableStore.ReadRotationCount(
            WorkerTenantScanConsumer.DeploymentProjection));
    }

    [Fact]
    public async Task ConcurrentProcessCoordinatorsReceiveDistinctAtomicSteps()
    {
        Guid[] tenants = TenantIds(4);
        var durableStore = new FakeDurableCursorStore(tenants);
        var firstCoordinator = new WorkerTenantScanCoordinator(1);
        var secondCoordinator = new WorkerTenantScanCoordinator(1);
        await using WorkerTenantScanLease first = await firstCoordinator.AcquireAsync(
            WorkerTenantScanConsumer.UserOperations,
            durableStore.AdvanceAsync,
            TestContext.Current.CancellationToken);
        await using WorkerTenantScanLease second = await secondCoordinator.AcquireAsync(
            WorkerTenantScanConsumer.UserOperations,
            durableStore.AdvanceAsync,
            TestContext.Current.CancellationToken);

        WorkerTenantScanStep?[] steps = await Task.WhenAll(
            first.TryBeginNextAsync(TestContext.Current.CancellationToken).AsTask(),
            second.TryBeginNextAsync(TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(
            tenants[..2],
            steps.Select(step => Assert.IsType<WorkerTenantScanStep>(step).TenantId)
                .Order()
                .ToArray());
    }

    [Fact]
    public async Task ConsumersAdvanceIndependentlyAndCanceledWaitersDoNotScan()
    {
        Guid[] tenants = TenantIds(4);
        var durableStore = new FakeDurableCursorStore(tenants);
        var coordinator = new WorkerTenantScanCoordinator(2);
        await using WorkerTenantScanLease heldOutbox = await coordinator.AcquireAsync(
            WorkerTenantScanConsumer.Outbox,
            durableStore.AdvanceAsync,
            TestContext.Current.CancellationToken);
        Assert.Equal(
            tenants[0],
            (await heldOutbox.TryBeginNextAsync(TestContext.Current.CancellationToken))!.Value.TenantId);

        await using WorkerTenantScanLease independent = await coordinator.AcquireAsync(
            WorkerTenantScanConsumer.DeploymentProjection,
            durableStore.AdvanceAsync,
            TestContext.Current.CancellationToken);
        Assert.Equal(
            tenants[0],
            (await independent.TryBeginNextAsync(TestContext.Current.CancellationToken))!.Value.TenantId);

        using var canceled = new CancellationTokenSource();
        ValueTask<WorkerTenantScanLease> waiting = coordinator.AcquireAsync(
            WorkerTenantScanConsumer.Outbox,
            durableStore.AdvanceAsync,
            canceled.Token);
        canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting.AsTask());

        await heldOutbox.DisposeAsync();
        await using WorkerTenantScanLease nextOutbox = await coordinator.AcquireAsync(
            WorkerTenantScanConsumer.Outbox,
            durableStore.AdvanceAsync,
            TestContext.Current.CancellationToken);
        Assert.Equal(
            tenants[1],
            (await nextOutbox.TryBeginNextAsync(TestContext.Current.CancellationToken))!.Value.TenantId);
    }

    [Fact]
    public async Task InvalidDurableMetadataFailsClosed()
    {
        var coordinator = new WorkerTenantScanCoordinator(1);
        await using WorkerTenantScanLease scan = await coordinator.AcquireAsync(
            WorkerTenantScanConsumer.Outbox,
            (_, _, _) => ValueTask.FromResult<WorkerTenantScanStep?>(
                new WorkerTenantScanStep(Guid.Empty, false, 0)),
            TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => scan.TryBeginNextAsync(TestContext.Current.CancellationToken).AsTask());
    }

    [Fact]
    public void FourPostgresWorkflowsUseDurableScansWithoutProcessLocalCursors()
    {
        var contracts = new Dictionary<string, WorkerTenantScanConsumer>
        {
            ["PostgresWorkerInfrastructure.cs"] = WorkerTenantScanConsumer.Outbox,
            ["PostgresCredentialGrantExpiryStore.cs"] =
                WorkerTenantScanConsumer.CredentialGrantExpiry,
            ["PostgresDeploymentProjectionStore.cs"] =
                WorkerTenantScanConsumer.DeploymentProjection,
            ["PostgresUserOperationWorkStore.cs"] = WorkerTenantScanConsumer.UserOperations
        };

        foreach ((string file, WorkerTenantScanConsumer consumer) in contracts)
        {
            string source = ReadRepositoryFile(
                "src",
                "Apps",
                "YO4X.ControlPlane.Workers",
                "Operations",
                file);
            Assert.Contains(
                $"WorkerTenantScanConsumer.{consumer}",
                source,
                StringComparison.Ordinal);
            Assert.Contains("TryBeginNextAsync", source, StringComparison.Ordinal);
            Assert.DoesNotContain("AcknowledgeVisited", source, StringComparison.Ordinal);
            Assert.DoesNotContain("ConcurrentDictionary", source, StringComparison.Ordinal);
        }

        string infrastructure = ReadRepositoryFile(
            "src",
            "Apps",
            "YO4X.ControlPlane.Workers",
            "Operations",
            "PostgresWorkerInfrastructure.cs");
        string deployments = ReadRepositoryFile(
            "src",
            "Apps",
            "YO4X.ControlPlane.Workers",
            "Operations",
            "PostgresDeploymentProjectionStore.cs");
        string userOperations = ReadRepositoryFile(
            "src",
            "Apps",
            "YO4X.ControlPlane.Workers",
            "Operations",
            "PostgresUserOperationWorkStore.cs");
        Assert.Contains("control.worker_tenant_scan_cursors", infrastructure, StringComparison.Ordinal);
        Assert.Contains("array_agg(consumer order by consumer)", infrastructure, StringComparison.Ordinal);
        Assert.Contains(
            "control.user_operation_backlog_observations",
            infrastructure,
            StringComparison.Ordinal);
        Assert.Contains("for update", infrastructure, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "set last_tenant_id = coalesce(eligible.id, progress.last_tenant_id)",
            infrastructure,
            StringComparison.Ordinal);
        Assert.DoesNotContain("last_advanced_at =", infrastructure, StringComparison.Ordinal);
        Assert.DoesNotContain("last_rotation_completed_at =", infrastructure, StringComparison.Ordinal);
        Assert.DoesNotContain("rotation_count =", infrastructure, StringComparison.Ordinal);
        Assert.Contains("control.deployment_scan_cursors", deployments, StringComparison.Ordinal);
        Assert.Contains("for update", deployments, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "set last_deployment_id = coalesce(",
            deployments,
            StringComparison.Ordinal);
        Assert.DoesNotContain("last_advanced_at =", deployments, StringComparison.Ordinal);
        Assert.DoesNotContain("last_rotation_completed_at =", deployments, StringComparison.Ordinal);
        Assert.DoesNotContain("rotation_count =", deployments, StringComparison.Ordinal);
        Assert.Contains("and desired_state <> 'draft'", deployments, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "and row_version = @expected_version\n            for update",
            deployments.Replace("\r\n", "\n", StringComparison.Ordinal),
            StringComparison.Ordinal);
        string normalizedUserOperations = userOperations.Replace("\r\n", "\n", StringComparison.Ordinal);
        Assert.Contains(
            "'broker_account.delete',\n                        'broker_account.disable',\n                        'deployment.stop_after_flat',\n                        'deployment.close_only'",
            normalizedUserOperations,
            StringComparison.Ordinal);
        Assert.Contains(
            "then 0\n                    else 1\n                end,\n                coalesce(operation.next_processing_at, operation.created_at),\n                operation.created_at,\n                operation.id",
            normalizedUserOperations,
            StringComparison.Ordinal);
        Assert.Contains(
            "operation.next_processing_at is null\n                  or operation.next_processing_at <= work_clock.checked_at",
            normalizedUserOperations,
            StringComparison.Ordinal);
        Assert.Contains(
            "control.defer_user_operation(@operation_id, @claim_token, @expected_version, @state, @processing_error_code)",
            normalizedUserOperations,
            StringComparison.Ordinal);
        Assert.Contains(
            "operation.state in ('accepted', 'dispatching') as for_dispatch",
            normalizedUserOperations,
            StringComparison.Ordinal);
        Assert.DoesNotContain("dispatchCandidates", normalizedUserOperations, StringComparison.Ordinal);
        Assert.DoesNotContain("reconciliationCandidates", normalizedUserOperations, StringComparison.Ordinal);
        Assert.Contains(
            "control.refresh_user_operation_backlog_observation()",
            normalizedUserOperations,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "insert into control.user_operation_backlog_observations",
            normalizedUserOperations,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "update control.user_operation_backlog_observations",
            normalizedUserOperations,
            StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<Guid[]> ReadCycleAsync(
        WorkerTenantScanCoordinator coordinator,
        WorkerTenantScanConsumer consumer,
        Func<WorkerTenantScanConsumer, long?, CancellationToken,
            ValueTask<WorkerTenantScanStep?>> advance)
    {
        await using WorkerTenantScanLease scan = await coordinator.AcquireAsync(
            consumer,
            advance,
            TestContext.Current.CancellationToken);
        var visited = new List<Guid>();
        while (await scan.TryBeginNextAsync(TestContext.Current.CancellationToken)
               is { } step)
        {
            visited.Add(step.TenantId);
        }

        return visited.ToArray();
    }

    private static Guid[] TenantIds(int count) => Enumerable.Range(1, count)
        .Select(value => Guid.Parse($"00000000-0000-0000-0000-{value:D12}"))
        .ToArray();

    private static string ReadRepositoryFile(params string[] segments)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null
            && !File.Exists(Path.Combine(directory.FullName, "YO4X.sln")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        string path = Path.Combine([directory.FullName, .. segments]);
        Assert.True(File.Exists(path), $"The repository contract file {path} was not found.");
        return File.ReadAllText(path);
    }

    private sealed class FakeDurableCursorStore(Guid[] orderedTenants)
    {
        private readonly object sync = new();
        private readonly Dictionary<WorkerTenantScanConsumer, CursorState> cursors = [];

        public ValueTask<WorkerTenantScanStep?> AdvanceAsync(
            WorkerTenantScanConsumer consumer,
            long? rotationCeiling,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (sync)
            {
                if (orderedTenants.Length == 0)
                {
                    return ValueTask.FromResult<WorkerTenantScanStep?>(null);
                }

                CursorState current = cursors.GetValueOrDefault(consumer, new(-1, 0));
                int nextIndex = (current.Index + 1) % orderedTenants.Length;
                bool completesRotation = current.Index >= 0 && nextIndex == 0;
                long nextRotationCount = checked(
                    current.RotationCount + (completesRotation ? 1 : 0));
                if (rotationCeiling is long ceiling && nextRotationCount > ceiling)
                {
                    return ValueTask.FromResult<WorkerTenantScanStep?>(null);
                }

                cursors[consumer] = new CursorState(nextIndex, nextRotationCount);
                return ValueTask.FromResult<WorkerTenantScanStep?>(new WorkerTenantScanStep(
                    orderedTenants[nextIndex],
                    completesRotation,
                    nextRotationCount));
            }
        }

        public long ReadRotationCount(WorkerTenantScanConsumer consumer)
        {
            lock (sync)
            {
                return cursors.GetValueOrDefault(consumer, new(-1, 0)).RotationCount;
            }
        }

        private readonly record struct CursorState(int Index, long RotationCount);
    }
}
