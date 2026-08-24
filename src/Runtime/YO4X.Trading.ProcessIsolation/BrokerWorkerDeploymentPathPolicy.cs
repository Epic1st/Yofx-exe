namespace YO4X.Trading.ProcessIsolation;

internal readonly record struct BrokerWorkerVolumeState(
    DriveType DriveType,
    bool IsReady);

internal sealed class BrokerWorkerDeploymentPathPolicy
{
    private readonly Func<string, BrokerWorkerVolumeState> inspectVolume;

    internal BrokerWorkerDeploymentPathPolicy(
        Func<string, BrokerWorkerVolumeState> inspectVolume)
    {
        ArgumentNullException.ThrowIfNull(inspectVolume);
        this.inspectVolume = inspectVolume;
    }

    internal static BrokerWorkerDeploymentPathPolicy System { get; } = new(
        static root =>
        {
            var drive = new DriveInfo(root);
            return new BrokerWorkerVolumeState(drive.DriveType, drive.IsReady);
        });

    internal void Validate(
        string workerExecutablePath,
        string workerLaunchManifestPath)
    {
        if (HasNetworkOrDeviceSyntax(workerExecutablePath)
            || HasNetworkOrDeviceSyntax(workerLaunchManifestPath))
        {
            throw InvalidPath();
        }

        string executableDirectory = Path.GetDirectoryName(workerExecutablePath)
            ?? throw InvalidPath();
        string manifestDirectory = Path.GetDirectoryName(workerLaunchManifestPath)
            ?? throw InvalidPath();
        if (!PathEquals(executableDirectory, manifestDirectory))
        {
            throw InvalidPath();
        }

        string deploymentRoot = Path.GetFullPath(manifestDirectory);
        string? volumeRoot = Path.GetPathRoot(deploymentRoot);
        if (string.IsNullOrWhiteSpace(volumeRoot)
            || PathEquals(deploymentRoot, volumeRoot))
        {
            throw InvalidPath();
        }

        BrokerWorkerVolumeState volume;
        try
        {
            volume = inspectVolume(volumeRoot);
        }
        catch
        {
            throw InvalidPath();
        }

        if (!volume.IsReady || volume.DriveType != DriveType.Fixed)
        {
            throw InvalidPath();
        }

        RejectReparsePointAncestry(deploymentRoot, volumeRoot);
        ValidateLocalFile(workerExecutablePath);
        ValidateLocalFile(workerLaunchManifestPath);
    }

    private static void RejectReparsePointAncestry(
        string deploymentRoot,
        string volumeRoot)
    {
        DirectoryInfo? directory = new(deploymentRoot);
        while (directory is not null && !PathEquals(directory.FullName, volumeRoot))
        {
            directory.Refresh();
            if (!directory.Exists
                || (directory.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw InvalidPath();
            }

            directory = directory.Parent;
        }

        if (directory is null)
        {
            throw InvalidPath();
        }
    }

    private static void ValidateLocalFile(string path)
    {
        var file = new FileInfo(path);
        file.Refresh();
        if (!file.Exists || (file.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw InvalidPath();
        }
    }

    private static bool HasNetworkOrDeviceSyntax(string path)
    {
        string normalized = path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        if (normalized.StartsWith(
                new string(Path.DirectorySeparatorChar, 2),
                StringComparison.Ordinal))
        {
            return true;
        }

        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        string windowsPath = path.Replace('/', '\\');
        return windowsPath.StartsWith("\\??\\", StringComparison.Ordinal)
            || windowsPath.StartsWith("\\\\?\\", StringComparison.Ordinal)
            || windowsPath.StartsWith("\\\\.\\", StringComparison.Ordinal);
    }

    private static bool PathEquals(string left, string right) =>
        string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
            OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal);

    private static ArgumentException InvalidPath() =>
        new("The broker worker deployment must use one dedicated local fixed-volume directory.");
}
