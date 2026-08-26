using System.Diagnostics;
using System.Security.Cryptography;
using YO4X.Trading.Abstractions;
using YO4X.Trading.ProcessIsolation;

namespace YO4X.GatewayHost.Tests;

public sealed class IsolatedBrokerProcessClientTests
{
    private static readonly Lazy<TestWorkerDeployment> TestWorker = new(
        CreateTestWorkerDeployment,
        LazyThreadSafetyMode.ExecutionAndPublication);

    [Fact]
    public async Task DeadlineTreeKillFailsClosedWithoutClaimingDescendantConfirmation()
    {
        var observer = new RecordingProcessObserver();
        var client = new BrokerProcessClient(
            WorkerOptions(TimeSpan.FromMilliseconds(500)),
            TimeProvider.System,
            observer);

        BrokerProcessBoundaryException exception = await Assert.ThrowsAsync<
            BrokerProcessBoundaryException>(() => client.ExecuteAsync(
                Request("__YO4X_TEST_HANG__"),
                CancellationToken.None));

        Assert.Equal("mt5_process_termination_unconfirmed", exception.Code);
        Assert.True(exception.ProcessStarted);
        Assert.True(observer.ProcessId > 0);
        Assert.True(observer.ProcessTreeKillRequested);
        await AssertProcessExitedAsync(observer.ProcessId);
    }

    [Fact]
    public async Task SpawnedDescendantMakesTreeKillOutcomeExplicitlyUnconfirmed()
    {
        string markerPath = Path.Combine(
            Path.GetTempPath(),
            $"yo4x-broker-descendant-{Guid.CreateVersion7():N}.pid");
        int descendantProcessId = 0;
        try
        {
            var observer = new RecordingProcessObserver();
            var client = new BrokerProcessClient(
                WorkerOptions(TimeSpan.FromSeconds(5)),
                TimeProvider.System,
                observer);

            BrokerProcessBoundaryException exception = await Assert.ThrowsAsync<
                BrokerProcessBoundaryException>(() => client.ExecuteAsync(
                    Request("__YO4X_TEST_DESCENDANT_HANG__", markerPath),
                    CancellationToken.None));

            Assert.Equal("mt5_process_termination_unconfirmed", exception.Code);
            Assert.True(exception.ProcessStarted);
            Assert.True(observer.ProcessTreeKillRequested);
            descendantProcessId = await ReadDescendantProcessIdAsync(markerPath);
            Assert.True(descendantProcessId > 0);
        }
        finally
        {
            TryTerminateOwnedTestProcess(descendantProcessId);
            if (File.Exists(markerPath))
            {
                File.Delete(markerPath);
            }
        }
    }

    [Fact]
    public async Task SlowLaunchVerificationCannotRefreshExpiredBoundaryDeadline()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var timeProvider = new AdvancingTimeProvider(now);
        var observer = new RecordingProcessObserver();
        var checkpoint = new AdvancingLaunchCheckpoint(
            () => timeProvider.Advance(TimeSpan.FromSeconds(1)));
        var client = new BrokerProcessClient(
            WorkerOptions(TimeSpan.FromMilliseconds(100)),
            timeProvider,
            observer,
            checkpoint);

        BrokerProcessBoundaryException exception = await Assert.ThrowsAsync<
            BrokerProcessBoundaryException>(() => client.ExecuteAsync(
                Request("EURUSD"),
                CancellationToken.None));

        Assert.Equal("mt5_process_deadline_expired", exception.Code);
        Assert.False(exception.ProcessStarted);
        Assert.True(checkpoint.Invoked);
        Assert.Equal(0, observer.ProcessId);
    }

    [Theory]
    [InlineData("__YO4X_TEST_MALFORMED__")]
    [InlineData("__YO4X_TEST_BAD_AUTH__")]
    public async Task MalformedOrUnauthenticatedResponseFailsClosed(string symbol)
    {
        var observer = new RecordingProcessObserver();
        var client = new BrokerProcessClient(
            WorkerOptions(TimeSpan.FromSeconds(5)),
            TimeProvider.System,
            observer);

        BrokerProcessBoundaryException exception = await Assert.ThrowsAsync<
            BrokerProcessBoundaryException>(() => client.ExecuteAsync(
                Request(symbol),
                CancellationToken.None));

        Assert.Equal("mt5_process_termination_unconfirmed", exception.Code);
        Assert.True(exception.ProcessStarted);
        Assert.True(observer.ProcessId > 0);
        Assert.True(observer.ProcessTreeKillRequested);
        await AssertProcessExitedAsync(observer.ProcessId);
    }

    [Fact]
    public async Task AuthenticatedStrictResponseIsAcceptedWithoutVendorExecution()
    {
        var client = new BrokerProcessClient(
            WorkerOptions(TimeSpan.FromSeconds(5)),
            TimeProvider.System);
        BrokerWorkerRequest request = Request("EURUSD");

        BrokerWorkerResponse response = await client.ExecuteAsync(
            request,
            CancellationToken.None);

        Assert.Equal(request.RequestId, response.RequestId);
        Assert.False(response.IsSuccess);
        Assert.Equal(
            GatewayCommandDisposition.SubmissionDisabled,
            Assert.IsType<GatewaySendResult>(response.SendResult).Disposition);
        Assert.True(response.SendResult.PreInvocationNotSentProven);
    }

    [Fact]
    public async Task ExecutableDigestMismatchPreventsProcessStart()
    {
        var observer = new RecordingProcessObserver();
        string workerPath = TestWorkerPath();
        TestWorkerDeployment deployment = TestWorker.Value;
        var options = new IsolatedBrokerProcessOptions(
            workerPath,
            new string('0', 64),
            deployment.ManifestPath,
            deployment.ManifestSha256,
            TimeSpan.FromSeconds(5));
        var client = new BrokerProcessClient(options, TimeProvider.System, observer);

        BrokerProcessBoundaryException exception = await Assert.ThrowsAsync<
            BrokerProcessBoundaryException>(() => client.ExecuteAsync(
                Request("EURUSD"),
                CancellationToken.None));

        Assert.Equal("mt5_process_launch_closure_invalid", exception.Code);
        Assert.False(exception.ProcessStarted);
        Assert.Equal(0, observer.ProcessId);
    }

    [Fact]
    public async Task ManifestDigestMismatchPreventsProcessStart()
    {
        var observer = new RecordingProcessObserver();
        TestWorkerDeployment deployment = TestWorker.Value;
        var options = new IsolatedBrokerProcessOptions(
            deployment.ExecutablePath,
            deployment.ExecutableSha256,
            deployment.ManifestPath,
            new string('0', 64),
            TimeSpan.FromSeconds(5));
        var client = new BrokerProcessClient(options, TimeProvider.System, observer);

        BrokerProcessBoundaryException exception = await Assert.ThrowsAsync<
            BrokerProcessBoundaryException>(() => client.ExecuteAsync(
                Request("EURUSD"),
                CancellationToken.None));

        Assert.Equal("mt5_process_launch_closure_invalid", exception.Code);
        Assert.False(exception.ProcessStarted);
        Assert.Equal(0, observer.ProcessId);
    }

    [Theory]
    [InlineData(DriveType.Network)]
    [InlineData(DriveType.Removable)]
    public void NonFixedDeploymentVolumeIsRejectedByOptions(DriveType driveType)
    {
        TestWorkerDeployment deployment = TestWorker.Value;
        var pathPolicy = new BrokerWorkerDeploymentPathPolicy(
            _ => new BrokerWorkerVolumeState(driveType, IsReady: true));

        Assert.Throws<ArgumentException>(() => new IsolatedBrokerProcessOptions(
            pathPolicy,
            deployment.ExecutablePath,
            deployment.ExecutableSha256,
            deployment.ManifestPath,
            deployment.ManifestSha256,
            TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void WindowsNetworkAndDevicePathsAreRejectedBeforeFilesystemAccess()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        string digest = new('a', 64);
        Assert.Throws<ArgumentException>(() => new IsolatedBrokerProcessOptions(
            @"\\yo4x-invalid-host\worker\broker-worker.exe",
            digest,
            @"\\yo4x-invalid-host\worker\broker-worker.launch.v1.json",
            digest,
            TimeSpan.FromSeconds(5)));
        Assert.Throws<ArgumentException>(() => new IsolatedBrokerProcessOptions(
            @"\\?\C:\yo4x\worker\broker-worker.exe",
            digest,
            @"\\?\C:\yo4x\worker\broker-worker.launch.v1.json",
            digest,
            TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task UnlistedAdjacentDeploymentFilePreventsProcessStart()
    {
        string temporaryRoot = Path.Combine(
            Path.GetTempPath(),
            "yo4x-broker-boundary-tests",
            Guid.CreateVersion7().ToString("N"));
        try
        {
            TestWorkerDeployment deployment = CopyTestWorkerDeployment(temporaryRoot);
            await File.WriteAllTextAsync(
                Path.Combine(temporaryRoot, "unlisted-adjacent.bin"),
                "test-only-unlisted-file",
                TestContext.Current.CancellationToken);
            var observer = new RecordingProcessObserver();
            var options = new IsolatedBrokerProcessOptions(
                deployment.ExecutablePath,
                deployment.ExecutableSha256,
                deployment.ManifestPath,
                deployment.ManifestSha256,
                TimeSpan.FromSeconds(5));
            var client = new BrokerProcessClient(options, TimeProvider.System, observer);

            BrokerProcessBoundaryException exception = await Assert.ThrowsAsync<
                BrokerProcessBoundaryException>(() => client.ExecuteAsync(
                    Request("EURUSD"),
                    CancellationToken.None));

            Assert.Equal("mt5_process_launch_closure_invalid", exception.Code);
            Assert.False(exception.ProcessStarted);
            Assert.Equal(0, observer.ProcessId);
        }
        finally
        {
            if (Directory.Exists(temporaryRoot))
            {
                Directory.Delete(temporaryRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task ConnectionOnlyClientAcceptsAuthenticatedProbeResponse()
    {
        var client = new IsolatedBrokerConnectionProbeClient(
            WorkerOptions(TimeSpan.FromSeconds(5)),
            TestProbeEnvironment());

        GatewayOperationResult<BrokerConnectionProbeObservation> result =
            await client.ProbeAsync(
                ConnectProbe("Synthetic-Demo"),
                TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(BrokerWorkerProtocolContract.ConnectProbeUnavailableCode, result.Code);
        Assert.Null(result.Value);
    }

    [Fact]
    public async Task ConnectionOnlyClientHardDeadlineTerminatesHangingWorker()
    {
        var observer = new RecordingProcessObserver();
        var client = new IsolatedBrokerConnectionProbeClient(
            WorkerOptions(TimeSpan.FromMilliseconds(500)),
            TestProbeEnvironment(),
            TimeProvider.System,
            observer);

        GatewayOperationResult<BrokerConnectionProbeObservation> result =
            await client.ProbeAsync(
                ConnectProbe("__YO4X_TEST_HANG__"),
                CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(BrokerWorkerProtocolContract.ConnectProbeFailedCode, result.Code);
        Assert.True(observer.ProcessId > 0);
        Assert.True(observer.ProcessTreeKillRequested);
        await AssertProcessExitedAsync(observer.ProcessId);
    }

    [Fact]
    public void ConnectionOnlyClientRejectsUnscopedWorkerEnvironment()
    {
        Assert.Throws<ArgumentException>(() =>
            new IsolatedBrokerConnectionProbeClient(
                WorkerOptions(TimeSpan.FromSeconds(5)),
                new Dictionary<string, string>
                {
                    ["BROKER_PASSWORD"] = "must-not-cross-boundary"
                }));
    }

    private static BrokerWorkerConnectProbeRequest ConnectProbe(string serverName) => new(
        Guid.CreateVersion7(),
        Guid.CreateVersion7(),
        new string('a', 64),
        new string('b', 64),
        new string('c', 64),
        new BrokerServerIdentity("Synthetic Broker", serverName),
        BrokerEnvironment.Demo,
        DateTimeOffset.UtcNow);

    private static Dictionary<string, string> TestProbeEnvironment() =>
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["YO4X_MT5_PROBE_ARTIFACT_ID"] = Guid.CreateVersion7().ToString("D")
        };

    private static IsolatedBrokerProcessOptions WorkerOptions(TimeSpan timeout)
    {
        TestWorkerDeployment deployment = TestWorker.Value;
        return new IsolatedBrokerProcessOptions(
            deployment.ExecutablePath,
            deployment.ExecutableSha256,
            deployment.ManifestPath,
            deployment.ManifestSha256,
            timeout,
            TimeSpan.FromSeconds(2));
    }

    private static TestWorkerDeployment CreateTestWorkerDeployment()
    {
        return CreateDeploymentManifest(TestWorkerPath());
    }

    private static TestWorkerDeployment CopyTestWorkerDeployment(string targetRoot)
    {
        string sourceExecutable = TestWorkerPath();
        string sourceRoot = Path.GetDirectoryName(sourceExecutable)
            ?? throw new InvalidOperationException("The test worker root is unavailable.");
        Directory.CreateDirectory(targetRoot);
        foreach (string sourcePath in Directory.EnumerateFiles(
            sourceRoot,
            "*",
            SearchOption.AllDirectories))
        {
            if (string.Equals(
                Path.GetFileName(sourcePath),
                BrokerWorkerLaunchManifestVerifier.DefaultFileName,
                StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string relative = Path.GetRelativePath(sourceRoot, sourcePath);
            string targetPath = Path.Combine(targetRoot, relative);
            Directory.CreateDirectory(
                Path.GetDirectoryName(targetPath)
                    ?? throw new InvalidOperationException(
                        "The copied test worker path is invalid."));
            File.Copy(sourcePath, targetPath, overwrite: false);
        }

        string targetExecutable = Path.Combine(
            targetRoot,
            Path.GetRelativePath(sourceRoot, sourceExecutable));
        return CreateDeploymentManifest(targetExecutable);
    }

    private static TestWorkerDeployment CreateDeploymentManifest(string executablePath)
    {
        string root = Path.GetDirectoryName(executablePath)
            ?? throw new InvalidOperationException("The test worker root is unavailable.");
        string manifestPath = Path.Combine(
            root,
            BrokerWorkerLaunchManifestVerifier.DefaultFileName);
        BrokerWorkerLaunchFile[] files = Directory
            .EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(path => !string.Equals(
                Path.GetFullPath(path),
                Path.GetFullPath(manifestPath),
                StringComparison.OrdinalIgnoreCase))
            .Select(path => new BrokerWorkerLaunchFile(
                Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/'),
                FileSha256(path)))
            .OrderBy(file => file.Path, StringComparer.Ordinal)
            .ToArray();
        string entrypoint = Path.GetRelativePath(root, executablePath)
            .Replace(Path.DirectorySeparatorChar, '/');
        var manifest = new BrokerWorkerLaunchManifest(
            BrokerWorkerLaunchManifestVerifier.ContractVersion,
            entrypoint,
            files);
        byte[] manifestBytes = BrokerWorkerLaunchManifestVerifier.SerializeForTests(manifest);
        try
        {
            File.WriteAllBytes(manifestPath, manifestBytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(manifestBytes);
        }

        return new TestWorkerDeployment(
            executablePath,
            FileSha256(executablePath),
            manifestPath,
            FileSha256(manifestPath));
    }

    private static string FileSha256(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static BrokerWorkerRequest Request(
        string symbol,
        string? targetBrokerId = null)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var command = new NormalizedBrokerCommand(
            1,
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            1,
            $"test-{Guid.CreateVersion7():N}",
            BrokerCommandAction.Place,
            symbol,
            BrokerOrderSide.Buy,
            BrokerOrderType.Market,
            0.01m,
            null,
            null,
            null,
            10,
            "yo4x-test-owner",
            null,
            targetBrokerId,
            null,
            null,
            null,
            null,
            now);
        return new BrokerWorkerRequest(
            BrokerWorkerProtocolContract.Version,
            Guid.CreateVersion7(),
            BrokerWorkerProtocolContract.SendOperation,
            now.AddSeconds(30),
            new BrokerWorkerSendRequest(
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                new string('a', 64),
                new string('b', 64),
                command),
            null);
    }

    private static string TestWorkerPath()
    {
        string repositoryRoot = FindRepositoryRoot();
        string configuration = new DirectoryInfo(AppContext.BaseDirectory)
            .Parent?.Name
            ?? throw new InvalidOperationException("The test configuration is unavailable.");
        string executable = OperatingSystem.IsWindows()
            ? "YO4X.BrokerProcess.TestWorker.exe"
            : "YO4X.BrokerProcess.TestWorker";
        string path = Path.Combine(
            repositoryRoot,
            "tests",
            "YO4X.BrokerProcess.TestWorker",
            "bin",
            configuration,
            "net10.0",
            executable);
        Assert.True(File.Exists(path), $"The isolated test worker is missing: {path}");
        return Path.GetFullPath(path);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null
            && !File.Exists(Path.Combine(directory.FullName, "YO4X.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("The repository root is unavailable.");
    }

    private static async Task AssertProcessExitedAsync(int processId)
    {
        for (int attempt = 0; attempt < 50; attempt++)
        {
            try
            {
                using Process process = Process.GetProcessById(processId);
                if (process.HasExited)
                {
                    return;
                }
            }
            catch (ArgumentException)
            {
                return;
            }

            await Task.Delay(20);
        }

        Assert.Fail("The isolated broker worker remained alive after termination.");
    }

    private static async Task<int> ReadDescendantProcessIdAsync(string markerPath)
    {
        for (int attempt = 0; attempt < 100; attempt++)
        {
            if (File.Exists(markerPath))
            {
                string text = await File.ReadAllTextAsync(
                    markerPath,
                    TestContext.Current.CancellationToken);
                if (int.TryParse(text, out int processId))
                {
                    return processId;
                }
            }

            await Task.Delay(20, TestContext.Current.CancellationToken);
        }

        return 0;
    }

    private static void TryTerminateOwnedTestProcess(int processId)
    {
        if (processId <= 0)
        {
            return;
        }

        try
        {
            using Process process = Process.GetProcessById(processId);
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(2_000);
            }
        }
        catch (ArgumentException)
        {
            // The exact test-owned descendant has already exited.
        }
        catch (InvalidOperationException)
        {
            // The exact test-owned descendant has already exited.
        }
    }

    private sealed class RecordingProcessObserver : IBrokerProcessObserver
    {
        public int ProcessId { get; private set; }

        public bool ProcessTreeKillRequested { get; private set; }

        public void OnStarted(int processId) => ProcessId = processId;

        public void OnTerminated(int processId, bool processTreeKillRequested)
        {
            Assert.Equal(ProcessId, processId);
            ProcessTreeKillRequested = processTreeKillRequested;
        }
    }

    private sealed class AdvancingLaunchCheckpoint(Action advance) :
        IBrokerProcessLaunchCheckpoint
    {
        public bool Invoked { get; private set; }

        public ValueTask DuringLaunchClosureVerificationAsync(
            CancellationToken cancellationToken)
        {
            Invoked = true;
            advance();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class AdvancingTimeProvider(DateTimeOffset initialUtc) : TimeProvider
    {
        private readonly object sync = new();
        private DateTimeOffset utcNow = initialUtc;

        public override DateTimeOffset GetUtcNow()
        {
            lock (sync)
            {
                return utcNow;
            }
        }

        internal void Advance(TimeSpan duration)
        {
            lock (sync)
            {
                utcNow = utcNow.Add(duration);
            }
        }
    }

    private sealed record TestWorkerDeployment(
        string ExecutablePath,
        string ExecutableSha256,
        string ManifestPath,
        string ManifestSha256);
}
