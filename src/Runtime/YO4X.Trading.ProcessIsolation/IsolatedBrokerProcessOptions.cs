namespace YO4X.Trading.ProcessIsolation;

public sealed class IsolatedBrokerProcessOptions
{
    private IsolatedBrokerProcessOptions()
    {
        Enabled = false;
        WorkerExecutablePath = string.Empty;
        WorkerExecutableSha256 = string.Empty;
        WorkerLaunchManifestPath = string.Empty;
        WorkerLaunchManifestSha256 = string.Empty;
        OperationTimeout = TimeSpan.FromSeconds(5);
        ShutdownTimeout = TimeSpan.FromSeconds(2);
        MaximumRequestBytes = BrokerProcessProtocol.DefaultMaximumRequestBytes;
        MaximumResponseBytes = BrokerProcessProtocol.DefaultMaximumResponseBytes;
    }

    public IsolatedBrokerProcessOptions(
        string workerExecutablePath,
        string workerExecutableSha256,
        string workerLaunchManifestPath,
        string workerLaunchManifestSha256,
        TimeSpan operationTimeout,
        TimeSpan? shutdownTimeout = null,
        int maximumRequestBytes = BrokerProcessProtocol.DefaultMaximumRequestBytes,
        int maximumResponseBytes = BrokerProcessProtocol.DefaultMaximumResponseBytes)
        : this(
            BrokerWorkerDeploymentPathPolicy.System,
            workerExecutablePath,
            workerExecutableSha256,
            workerLaunchManifestPath,
            workerLaunchManifestSha256,
            operationTimeout,
            shutdownTimeout,
            maximumRequestBytes,
            maximumResponseBytes)
    {
    }

    internal IsolatedBrokerProcessOptions(
        BrokerWorkerDeploymentPathPolicy deploymentPathPolicy,
        string workerExecutablePath,
        string workerExecutableSha256,
        string workerLaunchManifestPath,
        string workerLaunchManifestSha256,
        TimeSpan operationTimeout,
        TimeSpan? shutdownTimeout = null,
        int maximumRequestBytes = BrokerProcessProtocol.DefaultMaximumRequestBytes,
        int maximumResponseBytes = BrokerProcessProtocol.DefaultMaximumResponseBytes)
    {
        ArgumentNullException.ThrowIfNull(deploymentPathPolicy);
        ArgumentException.ThrowIfNullOrWhiteSpace(workerExecutablePath);
        if (!Path.IsPathFullyQualified(workerExecutablePath)
            || workerExecutablePath.IndexOfAny(['\r', '\n', '\0']) >= 0)
        {
            throw new ArgumentException(
                "The broker worker executable path must be absolute.",
                nameof(workerExecutablePath));
        }

        string fullPath = Path.GetFullPath(workerExecutablePath);
        if (!IsSha256(workerExecutableSha256))
        {
            throw new ArgumentException(
                "A SHA-256 worker executable digest is required.",
                nameof(workerExecutableSha256));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(workerLaunchManifestPath);
        if (!Path.IsPathFullyQualified(workerLaunchManifestPath)
            || workerLaunchManifestPath.IndexOfAny(['\r', '\n', '\0']) >= 0)
        {
            throw new ArgumentException(
                "The broker worker launch manifest path must be absolute.",
                nameof(workerLaunchManifestPath));
        }

        string fullManifestPath = Path.GetFullPath(workerLaunchManifestPath);
        if (!IsSha256(workerLaunchManifestSha256))
        {
            throw new ArgumentException(
                "A SHA-256 worker launch manifest digest is required.",
                nameof(workerLaunchManifestSha256));
        }

        deploymentPathPolicy.Validate(fullPath, fullManifestPath);

        TimeSpan effectiveShutdownTimeout = shutdownTimeout ?? TimeSpan.FromSeconds(2);
        if (operationTimeout < TimeSpan.FromMilliseconds(100)
            || operationTimeout > TimeSpan.FromSeconds(30)
            || effectiveShutdownTimeout < TimeSpan.FromMilliseconds(100)
            || effectiveShutdownTimeout > TimeSpan.FromSeconds(5)
            || maximumRequestBytes is < 4096 or > 512 * 1024
            || maximumResponseBytes is < 4096 or > 4 * 1024 * 1024)
        {
            throw new ArgumentOutOfRangeException(
                nameof(operationTimeout),
                "The broker worker resource limits are invalid.");
        }

        Enabled = true;
        WorkerExecutablePath = fullPath;
        WorkerExecutableSha256 = workerExecutableSha256.ToUpperInvariant();
        WorkerLaunchManifestPath = fullManifestPath;
        WorkerLaunchManifestSha256 = workerLaunchManifestSha256.ToUpperInvariant();
        OperationTimeout = operationTimeout;
        ShutdownTimeout = effectiveShutdownTimeout;
        MaximumRequestBytes = maximumRequestBytes;
        MaximumResponseBytes = maximumResponseBytes;
    }

    public static IsolatedBrokerProcessOptions Disabled { get; } = new();

    public bool Enabled { get; }

    public string WorkerExecutablePath { get; }

    public string WorkerExecutableSha256 { get; }

    public string WorkerLaunchManifestPath { get; }

    public string WorkerLaunchManifestSha256 { get; }

    public TimeSpan OperationTimeout { get; }

    public TimeSpan ShutdownTimeout { get; }

    public int MaximumRequestBytes { get; }

    public int MaximumResponseBytes { get; }

    private static bool IsSha256(string? value)
    {
        if (value is null || value.Length != 64)
        {
            return false;
        }

        foreach (char character in value)
        {
            if (!Uri.IsHexDigit(character))
            {
                return false;
            }
        }

        return true;
    }
}
