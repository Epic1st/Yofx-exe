using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace YO4X.Trading.ProcessIsolation;

internal sealed record BrokerWorkerLaunchManifest(
    int ContractVersion,
    string Entrypoint,
    IReadOnlyList<BrokerWorkerLaunchFile> Files);

internal sealed record BrokerWorkerLaunchFile(string Path, string Sha256);

internal sealed class BrokerWorkerLaunchClosure : IDisposable
{
    private readonly IReadOnlyList<FileStream> pins;

    internal BrokerWorkerLaunchClosure(IReadOnlyList<FileStream> pins) =>
        this.pins = pins;

    public void Dispose()
    {
        foreach (FileStream pin in pins)
        {
            try
            {
                pin.Dispose();
            }
            catch
            {
                // Cleanup never exposes a deployment path or masks the fixed
                // process-boundary outcome.
            }
        }
    }
}

internal static class BrokerWorkerLaunchManifestVerifier
{
    internal const int ContractVersion = 1;
    internal const string DefaultFileName = "broker-worker.launch.v1.json";

    private const int MaximumManifestBytes = 256 * 1024;
    private const int MaximumFiles = 512;
    private const long MaximumFileBytes = 512L * 1024L * 1024L;
    private const long MaximumClosureBytes = 1024L * 1024L * 1024L;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        AllowTrailingCommas = false,
        MaxDepth = 8,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        NumberHandling = JsonNumberHandling.Strict,
        RespectRequiredConstructorParameters = true
    };

    internal static async Task<BrokerWorkerLaunchClosure> OpenAndVerifyAsync(
        IsolatedBrokerProcessOptions options,
        CancellationToken cancellationToken,
        IBrokerProcessLaunchCheckpoint? launchCheckpoint = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        var pins = new List<FileStream>();
        try
        {
            if (launchCheckpoint is not null)
            {
                await launchCheckpoint.DuringLaunchClosureVerificationAsync(cancellationToken)
                    .ConfigureAwait(false);
            }

            cancellationToken.ThrowIfCancellationRequested();
            FileStream manifestPin = OpenPinnedFile(
                options.WorkerLaunchManifestPath,
                MaximumManifestBytes);
            pins.Add(manifestPin);
            await VerifyDigestAsync(
                    manifestPin,
                    options.WorkerLaunchManifestSha256,
                    cancellationToken)
                .ConfigureAwait(false);
            manifestPin.Position = 0;
            BrokerWorkerLaunchManifest manifest = await JsonSerializer.DeserializeAsync<
                    BrokerWorkerLaunchManifest>(
                    manifestPin,
                    JsonOptions,
                    cancellationToken)
                .ConfigureAwait(false)
                ?? throw InvalidManifest();
            string root = Path.GetDirectoryName(options.WorkerLaunchManifestPath)
                ?? throw InvalidManifest();
            string manifestFullPath = Path.GetFullPath(options.WorkerLaunchManifestPath);
            ValidateRoot(root);

            if (manifest.ContractVersion != ContractVersion
                || manifest.Files is null
                || manifest.Files.Count is < 1 or > MaximumFiles)
            {
                throw InvalidManifest();
            }

            var declaredFiles = new Dictionary<string, BrokerWorkerLaunchFile>(
                PathComparer());
            long totalBytes = 0;
            foreach (BrokerWorkerLaunchFile file in manifest.Files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (file is null
                    || !TryResolveRelativePath(root, file.Path, out string fullPath)
                    || !IsSha256(file.Sha256)
                    || !declaredFiles.TryAdd(file.Path, file))
                {
                    throw InvalidManifest();
                }

                FileStream pin = OpenPinnedFile(fullPath, MaximumFileBytes);
                pins.Add(pin);
                totalBytes = checked(totalBytes + pin.Length);
                if (totalBytes > MaximumClosureBytes)
                {
                    throw InvalidManifest();
                }

                await VerifyDigestAsync(pin, file.Sha256, cancellationToken)
                    .ConfigureAwait(false);
            }

            if (!declaredFiles.TryGetValue(
                    manifest.Entrypoint,
                    out BrokerWorkerLaunchFile? entrypoint)
                || !TryResolveRelativePath(root, manifest.Entrypoint, out string entrypointPath)
                || !PathEquals(entrypointPath, options.WorkerExecutablePath)
                || !DigestEquals(entrypoint.Sha256, options.WorkerExecutableSha256))
            {
                throw InvalidManifest();
            }

            cancellationToken.ThrowIfCancellationRequested();
            HashSet<string> actualFiles = EnumerateClosureFiles(
                root,
                manifestFullPath,
                cancellationToken);
            if (!actualFiles.SetEquals(declaredFiles.Keys))
            {
                throw InvalidManifest();
            }

            return new BrokerWorkerLaunchClosure(pins);
        }
        catch
        {
            foreach (FileStream pin in pins)
            {
                try
                {
                    await pin.DisposeAsync().ConfigureAwait(false);
                }
                catch
                {
                    // Preserve the fixed manifest validation failure.
                }
            }

            throw;
        }
    }

    internal static byte[] SerializeForTests(BrokerWorkerLaunchManifest manifest) =>
        JsonSerializer.SerializeToUtf8Bytes(manifest, JsonOptions);

    private static HashSet<string> EnumerateClosureFiles(
        string root,
        string manifestFullPath,
        CancellationToken cancellationToken)
    {
        var files = new HashSet<string>(PathComparer());
        var pending = new Stack<DirectoryInfo>();
        pending.Push(new DirectoryInfo(root));
        while (pending.TryPop(out DirectoryInfo? directory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (FileSystemInfo entry in directory.EnumerateFileSystemInfos())
            {
                cancellationToken.ThrowIfCancellationRequested();
                entry.Refresh();
                if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw InvalidManifest();
                }

                if (entry is DirectoryInfo childDirectory)
                {
                    pending.Push(childDirectory);
                    continue;
                }

                if (entry is not FileInfo file
                    || PathEquals(file.FullName, manifestFullPath))
                {
                    continue;
                }

                string relative = Path.GetRelativePath(root, file.FullName)
                    .Replace(Path.DirectorySeparatorChar, '/');
                if (!files.Add(relative))
                {
                    throw InvalidManifest();
                }
            }
        }

        return files;
    }

    private static FileStream OpenPinnedFile(string path, long maximumBytes)
    {
        var info = new FileInfo(path);
        info.Refresh();
        if (!info.Exists
            || info.Length is <= 0
            || info.Length > maximumBytes
            || (info.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw InvalidManifest();
        }

        return new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
    }

    private static async Task VerifyDigestAsync(
        FileStream stream,
        string expectedSha256,
        CancellationToken cancellationToken)
    {
        stream.Position = 0;
        byte[] digest = await SHA256.HashDataAsync(stream, cancellationToken)
            .ConfigureAwait(false);
        byte[] expected = Encoding.ASCII.GetBytes(expectedSha256.ToUpperInvariant());
        byte[] actual = Encoding.ASCII.GetBytes(Convert.ToHexString(digest));
        try
        {
            if (!CryptographicOperations.FixedTimeEquals(expected, actual))
            {
                throw InvalidManifest();
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(digest);
            CryptographicOperations.ZeroMemory(expected);
            CryptographicOperations.ZeroMemory(actual);
        }

        stream.Position = 0;
    }

    private static void ValidateRoot(string root)
    {
        var info = new DirectoryInfo(root);
        info.Refresh();
        if (!info.Exists || (info.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw InvalidManifest();
        }
    }

    private static bool TryResolveRelativePath(
        string root,
        string? relative,
        out string fullPath)
    {
        fullPath = string.Empty;
        if (string.IsNullOrWhiteSpace(relative)
            || relative.Length > 512
            || relative != relative.Trim()
            || relative.Contains('\\')
            || Path.IsPathFullyQualified(relative)
            || relative.Any(char.IsControl))
        {
            return false;
        }

        string[] segments = relative.Split('/');
        if (segments.Any(segment => segment.Length == 0 || segment is "." or ".."))
        {
            return false;
        }

        fullPath = Path.GetFullPath(
            Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        string canonical = Path.GetRelativePath(root, fullPath)
            .Replace(Path.DirectorySeparatorChar, '/');
        return string.Equals(canonical, relative, PathComparison());
    }

    private static bool IsSha256(string? value) =>
        value is { Length: 64 } && value.All(Uri.IsHexDigit);

    private static bool DigestEquals(string left, string right)
    {
        byte[] leftBytes = Encoding.ASCII.GetBytes(left.ToUpperInvariant());
        byte[] rightBytes = Encoding.ASCII.GetBytes(right.ToUpperInvariant());
        try
        {
            return CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(leftBytes);
            CryptographicOperations.ZeroMemory(rightBytes);
        }
    }

    private static bool PathEquals(string left, string right) =>
        string.Equals(
            Path.GetFullPath(left),
            Path.GetFullPath(right),
            PathComparison());

    private static StringComparer PathComparer() => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    private static StringComparison PathComparison() => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    private static InvalidDataException InvalidManifest() =>
        new("The broker worker launch manifest is invalid.");
}
