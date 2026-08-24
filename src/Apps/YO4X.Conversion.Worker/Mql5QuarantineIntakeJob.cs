using System.Buffers;
using System.Buffers.Binary;
using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using YO4X.StrategyGovernance;

namespace YO4X.Conversion.Worker;

public sealed class Mql5QuarantineIntakeJob
{
    public const int MaximumNonCanonicalFileCount = 256;
    public const long MaximumNonCanonicalFileBytes = 8L * 1024L * 1024L;
    public const long MaximumNonCanonicalTotalBytes = 64L * 1024L * 1024L;
    public const int MaximumArchiveCount = 16;
    public const int MaximumArchiveEntryCount = 128;
    public const long MaximumArchiveEntryBytes = 4L * 1024L * 1024L;
    public const long MaximumArchiveTotalDeclaredBytes = 16L * 1024L * 1024L;
    public const int MaximumArchiveCompressionRatio = 100;
    public const int MaximumRelativePathCharacters = 512;
    public const int MaximumArchivePathDepth = 16;
    public const int MaximumCanonicalMatchSamplesPerObject = 8;
    public const int MaximumArtifactUtf8Bytes = 32 * 1024 * 1024;

    private const uint EndOfCentralDirectorySignature = 0x06054b50;
    private const uint CentralDirectoryEntrySignature = 0x02014b50;
    private const ushort Zip64UShortSentinel = ushort.MaxValue;
    private const uint Zip64UIntSentinel = uint.MaxValue;

    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    private static readonly UnicodeEncoding StrictUtf16LittleEndian = new(
        bigEndian: false,
        byteOrderMark: false,
        throwOnInvalidBytes: true);

    private static readonly UnicodeEncoding StrictUtf16BigEndian = new(
        bigEndian: true,
        byteOrderMark: false,
        throwOnInvalidBytes: true);

    private static readonly (string Code, string[] Needles)[] SourceSignals =
    [
        ("PREPROCESSOR_DIRECTIVE", ["#property", "#include", "#define", "#import"]),
        ("MODERN_EVENT_HANDLER", ["OnInit", "OnTick", "OnDeinit", "OnCalculate", "OnTrade", "OnTimer"]),
        ("LEGACY_EVENT_HANDLER", ["init(", "start(", "deinit("]),
        ("TRADE_API", ["OrderSend", "CTrade", "MqlTradeRequest", ".Buy(", ".Sell("]),
        ("PARAMETER_DECLARATION", ["input ", "sinput ", "extern "]),
        ("MARKET_DATA_API", ["SymbolInfo", "CopyBuffer", "iMA(", "iRSI(", "MarketInfo("]),
        ("MQL_TYPE", ["MqlTick", "MqlTradeResult", "ENUM_"])
    ];

    public static Mql5QuarantineIntakeLimits Limits { get; } = new(
        MaximumNonCanonicalFileCount,
        MaximumNonCanonicalFileBytes,
        MaximumNonCanonicalTotalBytes,
        MaximumArchiveCount,
        MaximumArchiveEntryCount,
        MaximumArchiveEntryBytes,
        MaximumArchiveTotalDeclaredBytes,
        MaximumArchiveCompressionRatio,
        MaximumRelativePathCharacters,
        MaximumArchivePathDepth,
        MaximumCanonicalMatchSamplesPerObject,
        MaximumArtifactUtf8Bytes,
        Mql5CorpusInventoryJob.MaximumFilesystemEntryTraversalCount,
        Mql5CorpusInventoryJob.MaximumDirectoryTraversalCount);

    public async Task<Mql5QuarantineIntakeEvidence> AnalyzeDirectoryAsync(
        string sourceRoot,
        Mql5CorpusManifest canonicalManifest,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceRoot);
        ArgumentNullException.ThrowIfNull(canonicalManifest);

        CanonicalSnapshot canonical = SnapshotCanonicalManifest(canonicalManifest);
        string fullRoot = Path.GetFullPath(sourceRoot);
        var root = new DirectoryInfo(fullRoot);
        if (!root.Exists)
        {
            throw new DirectoryNotFoundException("The MQL5 source root does not exist.");
        }

        EnsureDirectoryChainContainsNoReparsePoint(root);
        List<FileInfo> pendingFiles = EnumerateNonCanonicalFilesBounded(root, cancellationToken);
        FileInfo[] files = pendingFiles
            .OrderBy(file => GetSafeRelativePath(fullRoot, file.FullName), StringComparer.OrdinalIgnoreCase)
            .ThenBy(file => GetSafeRelativePath(fullRoot, file.FullName), StringComparer.Ordinal)
            .ToArray();

        long declaredTotalBytes = 0;
        int archiveCount = 0;
        foreach (FileInfo file in files)
        {
            EnsureOrdinaryNonCanonicalFile(file);
            if (file.Length > MaximumNonCanonicalFileBytes)
            {
                throw new InvalidDataException(
                    "A non-canonical quarantine file exceeds the per-file size limit.");
            }

            if (declaredTotalBytes > MaximumNonCanonicalTotalBytes - file.Length)
            {
                throw new InvalidDataException(
                    "The non-canonical quarantine set exceeds the total size limit.");
            }

            declaredTotalBytes += file.Length;
            if (file.Extension.Equals(".zip", StringComparison.OrdinalIgnoreCase)
                && ++archiveCount > MaximumArchiveCount)
            {
                throw new InvalidDataException("The quarantine set exceeds the archive-count limit.");
            }
        }

        var evidenceFiles = new List<Mql5QuarantineFileEvidence>(files.Length);
        long actualTotalBytes = 0;
        foreach (FileInfo file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            byte[] content = await ReadBoundedSnapshotAsync(file, cancellationToken)
                .ConfigureAwait(false);
            try
            {
                if (actualTotalBytes > MaximumNonCanonicalTotalBytes - content.LongLength)
                {
                    throw new InvalidDataException(
                        "The non-canonical quarantine set exceeds the total size limit.");
                }

                actualTotalBytes += content.LongLength;
                string relativePath = GetSafeRelativePath(fullRoot, file.FullName);
                string extension = Path.GetExtension(relativePath).ToLowerInvariant();
                string sha256 = ToSha256(content);
                (string encoding, string[] sourceSignals) = InspectSourceSignals(
                    extension,
                    relativePath,
                    content);
                Mql5QuarantineClassification classification = Classify(
                    extension,
                    relativePath,
                    sourceSignals.Length);
                Mql5QuarantineArchiveEvidence? archive = extension == ".zip"
                    ? await InspectArchiveAsync(content, canonical.PathsBySha256, cancellationToken)
                        .ConfigureAwait(false)
                    : null;
                CanonicalMatches canonicalMatches = GetCanonicalMatches(
                    sha256,
                    canonical.PathsBySha256);

                evidenceFiles.Add(new Mql5QuarantineFileEvidence(
                    relativePath,
                    extension,
                    content.LongLength,
                    sha256,
                    classification,
                    encoding,
                    sourceSignals,
                    canonicalMatches.Count,
                    canonicalMatches.Samples,
                    0,
                    archive));
            }
            finally
            {
                CryptographicOperations.ZeroMemory(content);
            }
        }

        Mql5QuarantineFileEvidence[] withDuplicates = AddIntakeDuplicateCounts(evidenceFiles);
        Mql5QuarantineIntakeSummary summary = BuildSummary(
            withDuplicates,
            canonical.PathsBySha256);
        var binding = new Mql5QuarantineCanonicalBinding(
            canonical.CorpusSha256,
            canonical.FileCount,
            canonical.TotalBytes,
            [".mq5", ".mqh"]);
        return Mql5QuarantineIntakeFormatter.Create(binding, Limits, summary, withDuplicates);
    }

    internal static async Task WriteArtifactsAsync(
        Mql5QuarantineIntakeEvidence evidence,
        string sourceRoot,
        string evidenceOutputPath,
        string reportOutputPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(evidenceOutputPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(reportOutputPath);
        if (!Mql5QuarantineIntakeFormatter.HasValidEvidenceDigest(evidence))
        {
            throw new InvalidDataException("The quarantine evidence digest is invalid.");
        }

        string fullEvidencePath = Path.GetFullPath(evidenceOutputPath);
        string fullReportPath = Path.GetFullPath(reportOutputPath);
        if (fullEvidencePath.Equals(fullReportPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Evidence and report outputs must use different paths.");
        }

        string json = Mql5QuarantineIntakeFormatter.ToJson(evidence);
        string report = Mql5QuarantineIntakeFormatter.ToMarkdown(evidence);
        EnsureArtifactSize(json);
        EnsureArtifactSize(report);

        await WriteAtomicallyAsync(
                sourceRoot,
                fullEvidencePath,
                json,
                cancellationToken)
            .ConfigureAwait(false);
        await WriteAtomicallyAsync(
                sourceRoot,
                fullReportPath,
                report,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static void EnsureArtifactSize(string artifact)
    {
        if (Encoding.UTF8.GetByteCount(artifact) > MaximumArtifactUtf8Bytes)
        {
            throw new InvalidDataException(
                "A quarantine evidence artifact exceeds the bounded output limit.");
        }
    }

    private static CanonicalSnapshot SnapshotCanonicalManifest(Mql5CorpusManifest manifest)
    {
        if (!IsSha256(manifest.CorpusSha256)
            || manifest.FileCount is <= 0 or > Mql5CorpusInventoryJob.MaximumFileCount
            || manifest.Files is null
            || manifest.TotalBytes < 0)
        {
            throw new ArgumentException("The canonical MQL5 manifest is invalid.", nameof(manifest));
        }

        Mql5SourceManifest[] ownedFiles = SnapshotCanonicalFiles(
            manifest.Files,
            manifest.FileCount,
            nameof(manifest));
        var files = new List<(string RelativePath, long ByteLength, string Sha256)>(
            manifest.FileCount);
        long totalBytes = 0;
        foreach (Mql5SourceManifest file in ownedFiles)
        {
            ArgumentNullException.ThrowIfNull(file);
            string relativePath = ValidateRelativePath(file.RelativePath, allowTrailingSlash: false);
            string extension = Path.GetExtension(relativePath);
            if (!extension.Equals(".mq5", StringComparison.OrdinalIgnoreCase)
                && !extension.Equals(".mqh", StringComparison.OrdinalIgnoreCase)
                || !IsSha256(file.Sha256)
                || file.ByteLength < 0
                || file.ByteLength > Mql5CorpusInventoryJob.MaximumFileBytes
                || totalBytes > Mql5CorpusInventoryJob.MaximumCorpusBytes - file.ByteLength)
            {
                throw new ArgumentException("The canonical MQL5 manifest is invalid.", nameof(manifest));
            }

            totalBytes += file.ByteLength;
            files.Add((relativePath, file.ByteLength, file.Sha256));
        }

        (string RelativePath, long ByteLength, string Sha256)[] ordered = files
            .OrderBy(static file => file.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static file => file.RelativePath, StringComparer.Ordinal)
            .ToArray();
        if (ordered.Select(static file => file.RelativePath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count() != ordered.Length
            || totalBytes != manifest.TotalBytes)
        {
            throw new ArgumentException("The canonical MQL5 manifest is invalid.", nameof(manifest));
        }

        var corpusMaterial = new StringBuilder();
        foreach ((string relativePath, _, string sha256) in ordered)
        {
            corpusMaterial.Append(relativePath).Append('\0').Append(sha256).Append('\n');
        }

        string recomputedCorpusSha256 = ToSha256(Encoding.UTF8.GetBytes(corpusMaterial.ToString()));
        if (!recomputedCorpusSha256.Equals(manifest.CorpusSha256, StringComparison.Ordinal))
        {
            throw new ArgumentException("The canonical MQL5 manifest binding is invalid.", nameof(manifest));
        }

        Dictionary<string, string[]> pathsBySha256 = ordered
            .GroupBy(static file => file.Sha256, StringComparer.Ordinal)
            .ToDictionary(
                static group => group.Key,
                static group => group.Select(static file => file.RelativePath)
                    .Order(StringComparer.Ordinal)
                    .ToArray(),
                StringComparer.Ordinal);
        return new CanonicalSnapshot(
            manifest.CorpusSha256,
            manifest.FileCount,
            manifest.TotalBytes,
            pathsBySha256);
    }

    private static Mql5SourceManifest[] SnapshotCanonicalFiles(
        IReadOnlyList<Mql5SourceManifest> supplied,
        int expectedCount,
        string parameterName)
    {
        int suppliedCount;
        try
        {
            suppliedCount = supplied.Count;
        }
        catch (Exception exception) when (exception is not
            (OutOfMemoryException or StackOverflowException or AccessViolationException))
        {
            throw new ArgumentException(
                "The canonical MQL5 manifest file collection is invalid.",
                parameterName,
                exception);
        }

        if (suppliedCount != expectedCount)
        {
            throw new ArgumentException("The canonical MQL5 manifest is invalid.", parameterName);
        }

        var owned = new Mql5SourceManifest[expectedCount];
        for (int index = 0; index < owned.Length; index++)
        {
            try
            {
                owned[index] = supplied[index] ?? throw new InvalidDataException(
                    "A canonical manifest file cannot be null.");
            }
            catch (Exception exception) when (exception is not
                (OutOfMemoryException or StackOverflowException or AccessViolationException))
            {
                throw new ArgumentException(
                    "The canonical MQL5 manifest file collection is invalid.",
                    parameterName,
                    exception);
            }
        }

        return owned;
    }

    private static List<FileInfo> EnumerateNonCanonicalFilesBounded(
        DirectoryInfo root,
        CancellationToken cancellationToken)
    {
        var files = new List<FileInfo>();
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
                if (traversedEntries == Mql5CorpusInventoryJob.MaximumFilesystemEntryTraversalCount)
                {
                    throw new InvalidDataException(
                        "The quarantine source tree exceeds the filesystem-entry traversal limit.");
                }

                traversedEntries++;
                if ((item.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    continue;
                }

                if (item is DirectoryInfo childDirectory)
                {
                    if (traversedDirectories == Mql5CorpusInventoryJob.MaximumDirectoryTraversalCount)
                    {
                        throw new InvalidDataException(
                            "The quarantine source tree exceeds the directory traversal limit.");
                    }

                    traversedDirectories++;
                    pending.Push(childDirectory);
                    continue;
                }

                if (item is not FileInfo file
                    || file.Extension.Equals(".mq5", StringComparison.OrdinalIgnoreCase)
                    || file.Extension.Equals(".mqh", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (files.Count == MaximumNonCanonicalFileCount)
                {
                    throw new InvalidDataException(
                        "The quarantine set exceeds the non-canonical file-count limit.");
                }

                files.Add(file);
            }
        }

        return files;
    }

    private static async Task<Mql5QuarantineArchiveEvidence> InspectArchiveAsync(
        byte[] snapshot,
        IReadOnlyDictionary<string, string[]> canonicalPathsBySha256,
        CancellationToken cancellationToken)
    {
        CentralDirectoryMetadata metadata;
        try
        {
            metadata = ParseCentralDirectory(snapshot);
        }
        catch (ArchiveEvidenceException exception)
        {
            return new Mql5QuarantineArchiveEvidence(
                exception.State,
                exception.ReasonCode,
                0,
                0,
                0,
                []);
        }

        try
        {
            using var memory = new MemoryStream(snapshot, writable: false);
            using var archive = new ZipArchive(memory, ZipArchiveMode.Read, leaveOpen: false);
            if (archive.Entries.Count != metadata.Entries.Count)
            {
                return InvalidArchive("ZIP_CENTRAL_DIRECTORY_MISMATCH");
            }

            var entries = new List<Mql5QuarantineArchiveEntryEvidence>(metadata.Entries.Count);
            bool unavailable = false;
            for (int index = 0; index < metadata.Entries.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                CentralEntryMetadata central = metadata.Entries[index];
                ZipArchiveEntry entry = archive.Entries[index];
                string zipArchiveName = entry.FullName.Replace('\\', '/');
                if (!zipArchiveName.Equals(central.RelativePath, StringComparison.Ordinal)
                    || entry.Length != central.UncompressedLength
                    || entry.CompressedLength != central.CompressedLength)
                {
                    return InvalidArchive("ZIP_CENTRAL_DIRECTORY_MISMATCH");
                }

                Mql5QuarantineArchiveEntryContentState state;
                string? sha256 = null;
                if (central.IsDirectory)
                {
                    state = Mql5QuarantineArchiveEntryContentState.Directory;
                }
                else if (central.Encrypted)
                {
                    state = Mql5QuarantineArchiveEntryContentState.Encrypted;
                    unavailable = true;
                }
                else if (central.CompressionMethod is not (0 or 8))
                {
                    state = Mql5QuarantineArchiveEntryContentState.UnsupportedCompression;
                    unavailable = true;
                }
                else
                {
                    (state, sha256) = await HashArchiveEntryAsync(
                            entry,
                            central.UncompressedLength,
                            central.Crc32,
                            cancellationToken)
                        .ConfigureAwait(false);
                    unavailable |= state != Mql5QuarantineArchiveEntryContentState.VerifiedDigest;
                }

                CanonicalMatches canonicalMatches = sha256 is null
                    ? CanonicalMatches.Empty
                    : GetCanonicalMatches(sha256, canonicalPathsBySha256);
                entries.Add(new Mql5QuarantineArchiveEntryEvidence(
                    central.RelativePath,
                    Path.GetExtension(central.RelativePath).ToLowerInvariant(),
                    central.UncompressedLength,
                    central.CompressedLength,
                    central.Crc32.ToString("x8", CultureInfo.InvariantCulture),
                    state,
                    sha256,
                    canonicalMatches.Count,
                    canonicalMatches.Samples,
                    0));
            }

            return new Mql5QuarantineArchiveEvidence(
                unavailable
                    ? Mql5QuarantineArchiveState.ContainsUnavailableEntryContent
                    : Mql5QuarantineArchiveState.Inspected,
                unavailable ? "ARCHIVE_ENTRY_CONTENT_UNAVAILABLE" : null,
                entries.Count,
                entries.Count(static entry =>
                    entry.ContentState != Mql5QuarantineArchiveEntryContentState.Directory),
                metadata.TotalUncompressedBytes,
                entries);
        }
        catch (InvalidDataException)
        {
            return InvalidArchive("ZIP_CONTAINER_INVALID");
        }
        catch (NotSupportedException)
        {
            return InvalidArchive("ZIP_CONTAINER_UNSUPPORTED");
        }
    }

    private static CentralDirectoryMetadata ParseCentralDirectory(ReadOnlySpan<byte> content)
    {
        if (content.Length < 22)
        {
            throw ArchiveEvidenceException.Invalid("ZIP_END_RECORD_MISSING");
        }

        int minimumOffset = Math.Max(0, content.Length - 65_557);
        int endOffset = -1;
        for (int candidate = content.Length - 22; candidate >= minimumOffset; candidate--)
        {
            if (ReadUInt32(content, candidate) != EndOfCentralDirectorySignature)
            {
                continue;
            }

            ushort commentLength = ReadUInt16(content, candidate + 20);
            if (candidate + 22 + commentLength == content.Length)
            {
                endOffset = candidate;
                break;
            }
        }

        if (endOffset < 0)
        {
            throw ArchiveEvidenceException.Invalid("ZIP_END_RECORD_MISSING");
        }

        ushort diskNumber = ReadUInt16(content, endOffset + 4);
        ushort centralDiskNumber = ReadUInt16(content, endOffset + 6);
        ushort entriesOnDisk = ReadUInt16(content, endOffset + 8);
        ushort totalEntries = ReadUInt16(content, endOffset + 10);
        uint centralSize = ReadUInt32(content, endOffset + 12);
        uint centralOffset = ReadUInt32(content, endOffset + 16);
        if (diskNumber != 0 || centralDiskNumber != 0 || entriesOnDisk != totalEntries)
        {
            throw ArchiveEvidenceException.Invalid("ZIP_MULTIDISK_UNSUPPORTED");
        }

        if (totalEntries == Zip64UShortSentinel
            || centralSize == Zip64UIntSentinel
            || centralOffset == Zip64UIntSentinel)
        {
            throw ArchiveEvidenceException.Limit("ZIP64_UNSUPPORTED");
        }

        if (totalEntries > MaximumArchiveEntryCount)
        {
            throw ArchiveEvidenceException.Limit("ZIP_ENTRY_COUNT_LIMIT");
        }

        long centralEnd = (long)centralOffset + centralSize;
        if (centralOffset > content.Length
            || centralEnd != endOffset)
        {
            throw ArchiveEvidenceException.Invalid("ZIP_CENTRAL_DIRECTORY_INVALID");
        }

        var entries = new List<CentralEntryMetadata>(totalEntries);
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int position = checked((int)centralOffset);
        long totalUncompressedBytes = 0;
        for (int index = 0; index < totalEntries; index++)
        {
            if (position > endOffset - 46
                || ReadUInt32(content, position) != CentralDirectoryEntrySignature)
            {
                throw ArchiveEvidenceException.Invalid("ZIP_CENTRAL_ENTRY_INVALID");
            }

            ushort flags = ReadUInt16(content, position + 8);
            ushort compressionMethod = ReadUInt16(content, position + 10);
            uint crc32 = ReadUInt32(content, position + 16);
            uint compressedLength = ReadUInt32(content, position + 20);
            uint uncompressedLength = ReadUInt32(content, position + 24);
            ushort nameLength = ReadUInt16(content, position + 28);
            ushort extraLength = ReadUInt16(content, position + 30);
            ushort commentLength = ReadUInt16(content, position + 32);
            ushort startingDisk = ReadUInt16(content, position + 34);
            uint localHeaderOffset = ReadUInt32(content, position + 42);
            int recordLength;
            try
            {
                recordLength = checked(46 + nameLength + extraLength + commentLength);
            }
            catch (OverflowException)
            {
                throw ArchiveEvidenceException.Invalid("ZIP_CENTRAL_ENTRY_INVALID");
            }

            if (startingDisk != 0
                || localHeaderOffset >= centralOffset
                || position > endOffset - recordLength)
            {
                throw ArchiveEvidenceException.Invalid("ZIP_CENTRAL_ENTRY_INVALID");
            }

            if (nameLength == 0 || nameLength > MaximumRelativePathCharacters * 4)
            {
                throw ArchiveEvidenceException.Unsafe("ZIP_ENTRY_PATH_INVALID");
            }

            string relativePath = DecodeEntryName(
                content.Slice(position + 46, nameLength),
                utf8: (flags & 0x0800) != 0);
            relativePath = ValidateRelativePath(relativePath, allowTrailingSlash: true);
            if (!paths.Add(relativePath))
            {
                throw ArchiveEvidenceException.Unsafe("ZIP_ENTRY_PATH_COLLISION");
            }

            bool isDirectory = relativePath.EndsWith('/');
            if (isDirectory && uncompressedLength != 0)
            {
                throw ArchiveEvidenceException.Unsafe("ZIP_DIRECTORY_LENGTH_INVALID");
            }

            if (uncompressedLength > MaximumArchiveEntryBytes)
            {
                throw ArchiveEvidenceException.Limit("ZIP_ENTRY_SIZE_LIMIT");
            }

            if (totalUncompressedBytes > MaximumArchiveTotalDeclaredBytes - uncompressedLength)
            {
                throw ArchiveEvidenceException.Limit("ZIP_TOTAL_SIZE_LIMIT");
            }

            totalUncompressedBytes += uncompressedLength;
            if (compressedLength == 0 && uncompressedLength != 0
                || compressedLength != 0
                    && uncompressedLength > (long)compressedLength * MaximumArchiveCompressionRatio)
            {
                throw ArchiveEvidenceException.Limit("ZIP_COMPRESSION_RATIO_LIMIT");
            }

            entries.Add(new CentralEntryMetadata(
                relativePath,
                uncompressedLength,
                compressedLength,
                crc32,
                compressionMethod,
                (flags & 0x0001) != 0 || (flags & 0x0040) != 0,
                isDirectory));
            position += recordLength;
        }

        if (position != endOffset)
        {
            throw ArchiveEvidenceException.Invalid("ZIP_CENTRAL_DIRECTORY_INVALID");
        }

        return new CentralDirectoryMetadata(entries, totalUncompressedBytes);
    }

    private static async Task<(Mql5QuarantineArchiveEntryContentState State, string? Sha256)>
        HashArchiveEntryAsync(
            ZipArchiveEntry entry,
            long declaredLength,
            uint expectedCrc32,
            CancellationToken cancellationToken)
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(64 * 1024);
        try
        {
            using IncrementalHash sha256 = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            uint crc32 = uint.MaxValue;
            long actualLength = 0;
            try
            {
                await using Stream stream = entry.Open();
                while (true)
                {
                    int maximumRead = (int)Math.Min(
                        buffer.Length,
                        declaredLength - actualLength + 1);
                    if (maximumRead <= 0)
                    {
                        return (Mql5QuarantineArchiveEntryContentState.IntegrityMismatch, null);
                    }

                    int read = await stream.ReadAsync(
                            buffer.AsMemory(0, maximumRead),
                            cancellationToken)
                        .ConfigureAwait(false);
                    if (read == 0)
                    {
                        break;
                    }

                    actualLength += read;
                    if (actualLength > declaredLength
                        || actualLength > MaximumArchiveEntryBytes)
                    {
                        return (Mql5QuarantineArchiveEntryContentState.IntegrityMismatch, null);
                    }

                    sha256.AppendData(buffer, 0, read);
                    crc32 = UpdateCrc32(crc32, buffer.AsSpan(0, read));
                }
            }
            catch (InvalidDataException)
            {
                return (Mql5QuarantineArchiveEntryContentState.Unreadable, null);
            }
            catch (NotSupportedException)
            {
                return (Mql5QuarantineArchiveEntryContentState.Unreadable, null);
            }

            crc32 = ~crc32;
            if (actualLength != declaredLength || crc32 != expectedCrc32)
            {
                return (Mql5QuarantineArchiveEntryContentState.IntegrityMismatch, null);
            }

            return (
                Mql5QuarantineArchiveEntryContentState.VerifiedDigest,
                Convert.ToHexString(sha256.GetHashAndReset()).ToLowerInvariant());
        }
        finally
        {
            CryptographicOperations.ZeroMemory(buffer);
            ArrayPool<byte>.Shared.Return(buffer, clearArray: false);
        }
    }

    private static uint UpdateCrc32(uint crc32, ReadOnlySpan<byte> content)
    {
        foreach (byte value in content)
        {
            crc32 ^= value;
            for (int bit = 0; bit < 8; bit++)
            {
                uint mask = (uint)-(int)(crc32 & 1);
                crc32 = crc32 >> 1 ^ 0xedb88320u & mask;
            }
        }

        return crc32;
    }

    private static Mql5QuarantineFileEvidence[] AddIntakeDuplicateCounts(
        IReadOnlyList<Mql5QuarantineFileEvidence> files)
    {
        Dictionary<string, int> digestCounts = files
            .Select(static file => file.Sha256)
            .Concat(files.SelectMany(static file => file.Archive?.Entries ?? [])
                .Where(static entry => entry.Sha256 is not null)
                .Select(static entry => entry.Sha256!))
            .GroupBy(static digest => digest, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.Count(), StringComparer.Ordinal);

        return files.Select(file => file with
        {
            ExactIntakeDuplicateCount = digestCounts[file.Sha256] - 1,
            Archive = file.Archive is null
                    ? null
                    : file.Archive with
                    {
                        Entries = file.Archive.Entries.Select(entry => entry with
                        {
                            ExactIntakeDuplicateCount = entry.Sha256 is null
                                    ? 0
                                    : digestCounts[entry.Sha256] - 1
                        }).ToArray()
                    }
        })
            .ToArray();
    }

    private static Mql5QuarantineIntakeSummary BuildSummary(
        Mql5QuarantineFileEvidence[] files,
        IReadOnlyDictionary<string, string[]> canonicalPathsBySha256)
    {
        Mql5QuarantineArchiveEntryEvidence[] entries = files
            .SelectMany(static file => file.Archive?.Entries ?? [])
            .ToArray();
        int verifiedMatchingCanonical = files.Count(static file =>
                file.ExactCanonicalMatchCount > 0)
            + entries.Count(static entry => entry.ExactCanonicalMatchCount > 0);
        int canonicalPathsMatched = files
            .Where(static file => file.ExactCanonicalMatchCount > 0)
            .Select(static file => file.Sha256)
            .Concat(entries.Where(static entry => entry.ExactCanonicalMatchCount > 0)
                .Select(static entry => entry.Sha256!))
            .Distinct(StringComparer.Ordinal)
            .Sum(digest => canonicalPathsBySha256[digest].Length);
        int duplicateGroups = files.Select(static file => file.Sha256)
            .Concat(entries.Where(static entry => entry.Sha256 is not null)
                .Select(static entry => entry.Sha256!))
            .GroupBy(static digest => digest, StringComparer.Ordinal)
            .Count(static group => group.Count() > 1);

        return new Mql5QuarantineIntakeSummary(
            files.Length,
            files.Sum(static file => file.ByteLength),
            Count(Mql5QuarantineClassification.SourceLikeTextCandidate),
            Count(Mql5QuarantineClassification.LegacyMql4Source),
            Count(Mql5QuarantineClassification.CompiledMql4Binary),
            Count(Mql5QuarantineClassification.ZipArchive),
            Count(Mql5QuarantineClassification.OfficeDocumentContainer),
            Count(Mql5QuarantineClassification.UnknownQuarantined),
            entries.Length,
            entries.Count(static entry =>
                entry.ContentState != Mql5QuarantineArchiveEntryContentState.Directory),
            entries.Count(static entry =>
                entry.ContentState == Mql5QuarantineArchiveEntryContentState.VerifiedDigest),
            entries.Count(static entry => entry.ContentState is not
                (Mql5QuarantineArchiveEntryContentState.VerifiedDigest
                    or Mql5QuarantineArchiveEntryContentState.Directory)),
            verifiedMatchingCanonical,
            canonicalPathsMatched,
            duplicateGroups,
            0,
            0,
            0);

        int Count(Mql5QuarantineClassification classification) =>
            files.Count(file => file.Classification == classification);
    }

    private static (string Encoding, string[] Signals) InspectSourceSignals(
        string extension,
        string relativePath,
        ReadOnlySpan<byte> content)
    {
        bool sourceCandidateExtension = extension is ".txt" or ".mq4"
            || relativePath.Contains(".mq5", StringComparison.OrdinalIgnoreCase);
        if (!sourceCandidateExtension)
        {
            return ("not-inspected", []);
        }

        string text;
        string encoding;
        try
        {
            if (content.StartsWith(Encoding.UTF8.Preamble))
            {
                text = StrictUtf8.GetString(content[Encoding.UTF8.Preamble.Length..]);
                encoding = "utf-8-bom";
            }
            else if (content.StartsWith(Encoding.Unicode.Preamble))
            {
                text = StrictUtf16LittleEndian.GetString(
                    content[Encoding.Unicode.Preamble.Length..]);
                encoding = "utf-16le";
            }
            else if (content.StartsWith(Encoding.BigEndianUnicode.Preamble))
            {
                text = StrictUtf16BigEndian.GetString(
                    content[Encoding.BigEndianUnicode.Preamble.Length..]);
                encoding = "utf-16be";
            }
            else if (TryDetectBomlessUtf16(content, out UnicodeEncoding? bomlessEncoding))
            {
                text = bomlessEncoding!.GetString(content);
                encoding = bomlessEncoding.CodePage == Encoding.Unicode.CodePage
                    ? "utf-16le-no-bom"
                    : "utf-16be-no-bom";
            }
            else if (content.Contains((byte)0))
            {
                return ("binary-not-inspected", []);
            }
            else
            {
                text = StrictUtf8.GetString(content);
                encoding = "utf-8";
            }
        }
        catch (DecoderFallbackException)
        {
            if (content.Contains((byte)0))
            {
                return ("binary-not-inspected", []);
            }

            text = Encoding.Latin1.GetString(content);
            encoding = "single-byte-fallback";
        }

        string[] signals = SourceSignals
            .Where(signal => signal.Needles.Any(
                needle => text.Contains(needle, StringComparison.Ordinal)))
            .Select(static signal => signal.Code)
            .Order(StringComparer.Ordinal)
            .ToArray();
        return (encoding, signals);
    }

    private static bool TryDetectBomlessUtf16(
        ReadOnlySpan<byte> content,
        out UnicodeEncoding? encoding)
    {
        encoding = null;
        if (content.Length < 64 || content.Length % 2 != 0)
        {
            return false;
        }

        int sampleLength = Math.Min(content.Length, 4096) & ~1;
        int pairCount = sampleLength / 2;
        int evenNulCount = 0;
        int oddNulCount = 0;
        for (int index = 0; index < sampleLength; index += 2)
        {
            evenNulCount += content[index] == 0 ? 1 : 0;
            oddNulCount += content[index + 1] == 0 ? 1 : 0;
        }

        int dominantThreshold = pairCount * 3 / 4;
        int sparseThreshold = pairCount / 20;
        if (oddNulCount >= dominantThreshold && evenNulCount <= sparseThreshold)
        {
            encoding = StrictUtf16LittleEndian;
            return true;
        }

        if (evenNulCount >= dominantThreshold && oddNulCount <= sparseThreshold)
        {
            encoding = StrictUtf16BigEndian;
            return true;
        }

        return false;
    }

    private static Mql5QuarantineClassification Classify(
        string extension,
        string relativePath,
        int sourceSignalCount) => extension switch
        {
            ".zip" => Mql5QuarantineClassification.ZipArchive,
            ".docx" => Mql5QuarantineClassification.OfficeDocumentContainer,
            ".ex4" => Mql5QuarantineClassification.CompiledMql4Binary,
            ".mq4" => Mql5QuarantineClassification.LegacyMql4Source,
            _ when sourceSignalCount >= 2
                && (extension == ".txt"
                    || relativePath.Contains(".mq5", StringComparison.OrdinalIgnoreCase)) =>
                Mql5QuarantineClassification.SourceLikeTextCandidate,
            _ => Mql5QuarantineClassification.UnknownQuarantined
        };

    private static async Task<byte[]> ReadBoundedSnapshotAsync(
        FileInfo file,
        CancellationToken cancellationToken)
    {
        EnsureOrdinaryNonCanonicalFile(file);
        long expectedLength = file.Length;
        DateTime expectedLastWriteUtc = file.LastWriteTimeUtc;
        if (expectedLength > MaximumNonCanonicalFileBytes)
        {
            throw new InvalidDataException(
                "A non-canonical quarantine file exceeds the per-file size limit.");
        }

        await using var stream = new FileStream(
            file.FullName,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        EnsureOrdinaryNonCanonicalFile(file);
        if (stream.Length != expectedLength || stream.Length > MaximumNonCanonicalFileBytes)
        {
            throw new InvalidDataException("A quarantine file changed while it was being read.");
        }

        var content = new byte[checked((int)stream.Length)];
        try
        {
            await stream.ReadExactlyAsync(content, cancellationToken).ConfigureAwait(false);
            EnsureOrdinaryNonCanonicalFile(file);
            if (stream.Length != content.LongLength
                || stream.Position != content.LongLength
                || file.Length != expectedLength
                || file.LastWriteTimeUtc != expectedLastWriteUtc)
            {
                throw new InvalidDataException("A quarantine file changed while it was being read.");
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

    private static void EnsureOrdinaryNonCanonicalFile(FileInfo file)
    {
        file.Refresh();
        FileAttributes rejectedAttributes = FileAttributes.Directory
            | FileAttributes.ReparsePoint
            | FileAttributes.Device;
        if (!file.Exists
            || (file.Attributes & rejectedAttributes) != 0
            || file.Extension.Equals(".mq5", StringComparison.OrdinalIgnoreCase)
            || file.Extension.Equals(".mqh", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "Only ordinary, non-reparse, non-canonical files are accepted by quarantine intake.");
        }

        if (file.Directory is null)
        {
            throw new InvalidDataException("A quarantine file must have a parent directory.");
        }

        EnsureDirectoryChainContainsNoReparsePoint(file.Directory);
    }

    private static string GetSafeRelativePath(string fullRoot, string fullPath)
    {
        string relativePath = Path.GetRelativePath(fullRoot, fullPath).Replace('\\', '/');
        return ValidateRelativePath(relativePath, allowTrailingSlash: false);
    }

    private static string ValidateRelativePath(string path, bool allowTrailingSlash)
    {
        if (string.IsNullOrWhiteSpace(path)
            || path.Length > MaximumRelativePathCharacters
            || path.Contains('\\', StringComparison.Ordinal)
            || path[0] == '/'
            || Path.IsPathRooted(path)
            || path.Contains('\0')
            || path.Any(static character => char.IsControl(character) || char.GetUnicodeCategory(character)
                == UnicodeCategory.Format))
        {
            throw ArchiveEvidenceException.Unsafe("RELATIVE_PATH_INVALID");
        }

        bool trailingSlash = path.EndsWith('/');
        if (trailingSlash && !allowTrailingSlash)
        {
            throw ArchiveEvidenceException.Unsafe("RELATIVE_PATH_INVALID");
        }

        string[] segments = path.Split('/');
        int segmentCount = trailingSlash ? segments.Length - 1 : segments.Length;
        if (segmentCount is <= 0 or > MaximumArchivePathDepth
            || trailingSlash && segments[^1].Length != 0)
        {
            throw ArchiveEvidenceException.Unsafe("RELATIVE_PATH_INVALID");
        }

        for (int index = 0; index < segmentCount; index++)
        {
            string segment = segments[index];
            if (segment.Length == 0
                || segment is "." or ".."
                || index == 0 && segment.Length >= 2 && char.IsAsciiLetter(segment[0])
                    && segment[1] == ':')
            {
                throw ArchiveEvidenceException.Unsafe("RELATIVE_PATH_INVALID");
            }
        }

        return path;
    }

    private static string DecodeEntryName(ReadOnlySpan<byte> encoded, bool utf8)
    {
        if (!utf8 && encoded.ContainsAnyExceptInRange((byte)0x20, (byte)0x7e))
        {
            throw ArchiveEvidenceException.Unsafe("ZIP_ENTRY_NAME_ENCODING_UNSUPPORTED");
        }

        try
        {
            return utf8 ? StrictUtf8.GetString(encoded) : Encoding.ASCII.GetString(encoded);
        }
        catch (DecoderFallbackException)
        {
            throw ArchiveEvidenceException.Unsafe("ZIP_ENTRY_NAME_ENCODING_INVALID");
        }
    }

    private static Mql5QuarantineArchiveEvidence InvalidArchive(string reasonCode) => new(
        Mql5QuarantineArchiveState.InvalidContainer,
        reasonCode,
        0,
        0,
        0,
        []);

    private static CanonicalMatches GetCanonicalMatches(
        string sha256,
        IReadOnlyDictionary<string, string[]> canonicalPathsBySha256)
    {
        if (!canonicalPathsBySha256.TryGetValue(sha256, out string[]? paths))
        {
            return CanonicalMatches.Empty;
        }

        return new CanonicalMatches(
            paths.Length,
            paths.Take(MaximumCanonicalMatchSamplesPerObject).ToArray());
    }

    private static ushort ReadUInt16(ReadOnlySpan<byte> content, int offset)
    {
        if ((uint)offset > (uint)(content.Length - sizeof(ushort)))
        {
            throw ArchiveEvidenceException.Invalid("ZIP_METADATA_TRUNCATED");
        }

        return BinaryPrimitives.ReadUInt16LittleEndian(content[offset..]);
    }

    private static uint ReadUInt32(ReadOnlySpan<byte> content, int offset)
    {
        if ((uint)offset > (uint)(content.Length - sizeof(uint)))
        {
            throw ArchiveEvidenceException.Invalid("ZIP_METADATA_TRUNCATED");
        }

        return BinaryPrimitives.ReadUInt32LittleEndian(content[offset..]);
    }

    private static string ToSha256(ReadOnlySpan<byte> content) =>
        Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();

    private static bool IsSha256(string value) =>
        value is { Length: 64 }
        && value.All(static character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

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
            await File.WriteAllTextAsync(
                    temporaryPath,
                    content,
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                    cancellationToken)
                .ConfigureAwait(false);
            File.Move(temporaryPath, outputPath, overwrite: true);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

    private sealed record CanonicalSnapshot(
        string CorpusSha256,
        int FileCount,
        long TotalBytes,
        IReadOnlyDictionary<string, string[]> PathsBySha256);

    private sealed record CanonicalMatches(int Count, string[] Samples)
    {
        public static CanonicalMatches Empty { get; } = new(0, []);
    }

    private sealed record CentralDirectoryMetadata(
        IReadOnlyList<CentralEntryMetadata> Entries,
        long TotalUncompressedBytes);

    private sealed record CentralEntryMetadata(
        string RelativePath,
        long UncompressedLength,
        long CompressedLength,
        uint Crc32,
        ushort CompressionMethod,
        bool Encrypted,
        bool IsDirectory);

    private sealed class ArchiveEvidenceException : IOException
    {
        private ArchiveEvidenceException(
            Mql5QuarantineArchiveState state,
            string reasonCode)
            : base("Archive metadata cannot be admitted to quarantine evidence.")
        {
            State = state;
            ReasonCode = reasonCode;
        }

        public Mql5QuarantineArchiveState State { get; }

        public string ReasonCode { get; }

        public static ArchiveEvidenceException Invalid(string reasonCode) => new(
            Mql5QuarantineArchiveState.InvalidContainer,
            reasonCode);

        public static ArchiveEvidenceException Unsafe(string reasonCode) => new(
            Mql5QuarantineArchiveState.RejectedUnsafeMetadata,
            reasonCode);

        public static ArchiveEvidenceException Limit(string reasonCode) => new(
            Mql5QuarantineArchiveState.RejectedLimit,
            reasonCode);
    }
}
