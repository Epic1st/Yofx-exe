using System.Security.Cryptography;
using System.Text;
using YO4X.StrategyGovernance;

namespace YO4X.Conversion.Worker;

public sealed class Mql5CorpusInventoryJob
{
    public const int MaximumFileCount = 10_000;
    public const long MaximumFileBytes = 4L * 1024L * 1024L;
    public const long MaximumCorpusBytes = 256L * 1024L * 1024L;
    public const int MaximumFilesystemEntryTraversalCount = 20_000;
    public const int MaximumDirectoryTraversalCount = 2_000;

    private static readonly UTF8Encoding OutputEncoding = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    private readonly IMql5StaticInventoryAnalyzer _analyzer;

    public Mql5CorpusInventoryJob(IMql5StaticInventoryAnalyzer analyzer)
    {
        _analyzer = analyzer ?? throw new ArgumentNullException(nameof(analyzer));
    }

    public async Task<Mql5CorpusManifest> AnalyzeDirectoryAsync(
        string sourceRoot,
        CancellationToken cancellationToken = default)
    {
        using Mql5AnalyzedCorpus corpus = await AnalyzeDirectoryForPersistenceAsync(
                sourceRoot,
                cancellationToken)
            .ConfigureAwait(false);
        return corpus.Manifest;
    }

    public async Task<Mql5AnalyzedCorpus> AnalyzeDirectoryForPersistenceAsync(
        string sourceRoot,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceRoot);
        string fullRoot = Path.GetFullPath(sourceRoot);
        var root = new DirectoryInfo(fullRoot);
        if (!root.Exists)
        {
            throw new DirectoryNotFoundException("The MQL5 source root does not exist.");
        }

        EnsureDirectoryChainContainsNoReparsePoint(root);

        var boundedFiles = new List<FileInfo>();
        foreach (FileInfo file in EnumerateAllowedFiles(root, cancellationToken))
        {
            if (boundedFiles.Count == MaximumFileCount)
            {
                throw new InvalidDataException("The MQL5 corpus exceeds the file-count limit.");
            }

            boundedFiles.Add(file);
        }

        FileInfo[] files = boundedFiles
            .OrderBy(file => Path.GetRelativePath(fullRoot, file.FullName), StringComparer.OrdinalIgnoreCase)
            .ThenBy(file => Path.GetRelativePath(fullRoot, file.FullName), StringComparer.Ordinal)
            .ToArray();

        if (files.Length == 0)
        {
            throw new InvalidDataException("The source root contains no .mq5 or .mqh files.");
        }

        long declaredTotalBytes = 0;
        foreach (FileInfo file in files)
        {
            EnsureOrdinaryAllowedFile(file);
            if (file.Length > MaximumFileBytes)
            {
                throw new InvalidDataException("An MQL5 source file exceeds the per-file size limit.");
            }

            declaredTotalBytes = checked(declaredTotalBytes + file.Length);
            if (declaredTotalBytes > MaximumCorpusBytes)
            {
                throw new InvalidDataException("The MQL5 corpus exceeds the total size limit.");
            }
        }

        var documents = new List<Mql5SourceDocument>(files.Length);
        try
        {
            long actualTotalBytes = 0;
            foreach (FileInfo file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                byte[] content = await ReadBoundedFileAsync(file, cancellationToken).ConfigureAwait(false);
                try
                {
                    actualTotalBytes = checked(actualTotalBytes + content.LongLength);
                    if (actualTotalBytes > MaximumCorpusBytes)
                    {
                        throw new InvalidDataException("The MQL5 corpus exceeds the total size limit.");
                    }

                    string relativePath = Path.GetRelativePath(fullRoot, file.FullName).Replace('\\', '/');
                    var document = new Mql5SourceDocument(relativePath, content);
                    Mql5SourceSecretScanner.EnsureNoHighConfidenceSecrets(document);
                    documents.Add(document);
                }
                catch
                {
                    CryptographicOperations.ZeroMemory(content);
                    throw;
                }
            }

            return new Mql5AnalyzedCorpus(_analyzer.Analyze(documents), documents);
        }
        catch
        {
            foreach (Mql5SourceDocument document in documents)
            {
                CryptographicOperations.ZeroMemory(document.Content);
            }

            throw;
        }
    }

    internal static async Task WriteArtifactsAsync(
        Mql5CorpusManifest manifest,
        string sourceRoot,
        string manifestOutputPath,
        string reportOutputPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestOutputPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(reportOutputPath);

        string fullManifestPath = Path.GetFullPath(manifestOutputPath);
        string fullReportPath = Path.GetFullPath(reportOutputPath);
        if (fullManifestPath.Equals(fullReportPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Manifest and report outputs must use different paths.");
        }

        await WriteAtomicallyAsync(
                sourceRoot,
                fullManifestPath,
                Mql5InventoryFormatter.ToJson(manifest),
                cancellationToken)
            .ConfigureAwait(false);
        await WriteAtomicallyAsync(
                sourceRoot,
                fullReportPath,
                Mql5InventoryFormatter.ToMarkdown(manifest),
                cancellationToken)
            .ConfigureAwait(false);
    }

    internal static async Task WriteConversionEvidenceArtifactsAsync(
        Mql5ConversionCorpusEvidence evidence,
        string sourceRoot,
        string evidenceOutputPath,
        string reportOutputPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(evidenceOutputPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(reportOutputPath);

        string fullEvidencePath = Path.GetFullPath(evidenceOutputPath);
        string fullReportPath = Path.GetFullPath(reportOutputPath);
        if (fullEvidencePath.Equals(fullReportPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Evidence and report outputs must use different paths.");
        }

        await WriteAtomicallyAsync(
                sourceRoot,
                fullEvidencePath,
                Mql5ConversionEvidenceFormatter.ToJson(evidence),
                cancellationToken)
            .ConfigureAwait(false);
        await WriteAtomicallyAsync(
                sourceRoot,
                fullReportPath,
                Mql5ConversionEvidenceFormatter.ToMarkdown(evidence),
                cancellationToken)
            .ConfigureAwait(false);
    }

    internal static Task WriteCompilePackagePlanArtifactAsync(
        Mql5CompilePackagePlan plan,
        string sourceRoot,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        return WriteAtomicallyAsync(
            sourceRoot,
            Path.GetFullPath(outputPath),
            Mql5CompilePackagePlanFormatter.ToJson(plan),
            cancellationToken);
    }

    internal static Task WriteRestrictedCorpusArtifactAsync(
        Mql5RestrictedCorpusArtifact artifact,
        string sourceRoot,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        return WriteAtomicallyAsync(
            sourceRoot,
            Path.GetFullPath(outputPath),
            Mql5RestrictedCorpusArtifactFormatter.ToJson(artifact),
            cancellationToken);
    }

    private static IEnumerable<FileInfo> EnumerateAllowedFiles(
        DirectoryInfo root,
        CancellationToken cancellationToken) => EnumerateAllowedFilesBounded(
            root,
            MaximumFilesystemEntryTraversalCount,
            MaximumDirectoryTraversalCount,
            cancellationToken);

    internal static IEnumerable<FileInfo> EnumerateAllowedFilesBounded(
        DirectoryInfo root,
        int maximumFilesystemEntries,
        int maximumDirectories,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumFilesystemEntries);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumDirectories);

        var pending = new Stack<DirectoryInfo>();
        pending.Push(root);
        int traversedEntries = 0;
        int traversedDirectories = 1;
        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DirectoryInfo directory = pending.Pop();
            foreach (FileSystemInfo item in directory.EnumerateFileSystemInfos())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (traversedEntries == maximumFilesystemEntries)
                {
                    throw new InvalidDataException(
                        "The MQL5 source tree exceeds the filesystem-entry traversal limit.");
                }

                traversedEntries++;
                if ((item.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    continue;
                }

                if (item is DirectoryInfo childDirectory)
                {
                    if (traversedDirectories == maximumDirectories)
                    {
                        throw new InvalidDataException(
                            "The MQL5 source tree exceeds the directory traversal limit.");
                    }

                    traversedDirectories++;
                    pending.Push(childDirectory);
                    continue;
                }

                if (item is not FileInfo file)
                {
                    continue;
                }

                string extension = file.Extension;
                if (extension.Equals(".mq5", StringComparison.OrdinalIgnoreCase)
                    || extension.Equals(".mqh", StringComparison.OrdinalIgnoreCase))
                {
                    yield return file;
                }
            }
        }
    }

    internal static async Task<byte[]> ReadBoundedFileAsync(
        FileInfo file,
        CancellationToken cancellationToken)
    {
        EnsureOrdinaryAllowedFile(file);
        long expectedLength = file.Length;
        DateTime expectedLastWriteUtc = file.LastWriteTimeUtc;
        await using var stream = new FileStream(
            file.FullName,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        EnsureOrdinaryAllowedFile(file);
        if (stream.Length > MaximumFileBytes)
        {
            throw new InvalidDataException("An MQL5 source file exceeds the per-file size limit.");
        }

        int length = checked((int)stream.Length);
        var content = new byte[length];
        try
        {
            await stream.ReadExactlyAsync(content, cancellationToken).ConfigureAwait(false);
            EnsureOrdinaryAllowedFile(file);
            if (stream.Length != length
                || stream.Position != length
                || file.Length != expectedLength
                || file.LastWriteTimeUtc != expectedLastWriteUtc)
            {
                throw new InvalidDataException("An MQL5 source file changed while it was being read.");
            }

            return content;
        }
        catch
        {
            CryptographicOperations.ZeroMemory(content);
            throw;
        }
    }

    private static void EnsureDirectoryChainContainsNoReparsePoint(DirectoryInfo directory)
    {
        DirectoryInfo? current = directory;
        while (current is not null)
        {
            current.Refresh();
            if (!current.Exists)
            {
                throw new DirectoryNotFoundException(
                    "The MQL5 source root or one of its ancestors no longer exists.");
            }

            if ((current.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidDataException(
                    "The MQL5 source root and its ancestors must not be reparse points.");
            }

            current = current.Parent;
        }
    }

    private static void EnsureOrdinaryAllowedFile(FileInfo file)
    {
        file.Refresh();
        string extension = file.Extension;
        FileAttributes rejectedAttributes = FileAttributes.Directory
            | FileAttributes.ReparsePoint
            | FileAttributes.Device;
        if (!file.Exists
            || (file.Attributes & rejectedAttributes) != 0
            || !extension.Equals(".mq5", StringComparison.OrdinalIgnoreCase)
                && !extension.Equals(".mqh", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "Only ordinary, non-reparse .mq5 and .mqh source files are accepted.");
        }

        if (file.Directory is null)
        {
            throw new InvalidDataException("An MQL5 source file must have a parent directory.");
        }

        EnsureDirectoryChainContainsNoReparsePoint(file.Directory);
    }

    private static async Task WriteAtomicallyAsync(
        string sourceRoot,
        string outputPath,
        string content,
        CancellationToken cancellationToken)
    {
        string? outputDirectory = Path.GetDirectoryName(outputPath);
        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            throw new ArgumentException("An output directory is required.", nameof(outputPath));
        }

        Mql5ArtifactPathSet paths = Mql5ArtifactOutputGuard.Resolve(sourceRoot, outputPath);
        outputPath = paths.OutputPaths[0];
        outputDirectory = Path.GetDirectoryName(outputPath);
        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            throw new ArgumentException("An output directory is required.", nameof(outputPath));
        }

        Mql5ArtifactOutputGuard.EnsureOutputPathStillSafe(outputPath);
        Directory.CreateDirectory(outputDirectory);
        Mql5ArtifactOutputGuard.EnsureOutputPathStillSafe(outputPath);
        string temporaryPath = outputPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporaryPath, content, OutputEncoding, cancellationToken)
                .ConfigureAwait(false);
            File.Move(temporaryPath, outputPath, overwrite: true);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }
}

public sealed class Mql5AnalyzedCorpus : IDisposable
{
    private int disposed;

    public Mql5AnalyzedCorpus(
        Mql5CorpusManifest manifest,
        IReadOnlyList<Mql5SourceDocument> documents)
    {
        Manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
        Documents = documents ?? throw new ArgumentNullException(nameof(documents));
        if (Manifest.Files.Count != Documents.Count)
        {
            throw new ArgumentException("The source documents do not match the manifest.", nameof(documents));
        }
    }

    public Mql5CorpusManifest Manifest { get; }

    public IReadOnlyList<Mql5SourceDocument> Documents { get; }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        foreach (Mql5SourceDocument document in Documents)
        {
            CryptographicOperations.ZeroMemory(document.Content);
        }

        GC.SuppressFinalize(this);
    }
}
