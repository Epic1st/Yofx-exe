using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using YO4X.Conversion.Worker;
using YO4X.StrategyGovernance;

namespace YO4X.Worker.Tests;

public sealed class Mql5QuarantineIntakeTests
{
    [Fact]
    public async Task IntakeIsDeterministicMetadataOnlyAndKeepsCanonicalCorpusSeparate()
    {
        string root = CreateTemporaryRoot("evidence");
        try
        {
            byte[] canonicalSource = Encoding.UTF8.GetBytes(
                "#property strict\nvoid OnTick() { /* SYNTHETIC_CANONICAL_BODY */ }\n");
            await File.WriteAllBytesAsync(
                Path.Combine(root, "main.mq5"),
                canonicalSource,
                TestContext.Current.CancellationToken);
            await File.WriteAllTextAsync(
                Path.Combine(root, "renamed mq5.txt"),
                "#property strict\ninput int Period = 5;\nvoid OnTick() { CTrade trade; }\n"
                    + "// SYNTHETIC_QUARANTINE_BODY",
                TestContext.Current.CancellationToken);
            await File.WriteAllTextAsync(
                Path.Combine(root, "legacy.mq4"),
                "#property strict\nextern int Period = 5;\nint start() { return OrderSend(0,0,0,0,0,0); }",
                Encoding.Unicode,
                TestContext.Current.CancellationToken);
            byte[] compiled = [0x45, 0x58, 0x34, 0x00, 0x10, 0x20];
            await File.WriteAllBytesAsync(
                Path.Combine(root, "first.ex4"),
                compiled,
                TestContext.Current.CancellationToken);
            await File.WriteAllBytesAsync(
                Path.Combine(root, "second.ex4"),
                compiled,
                TestContext.Current.CancellationToken);
            CreateZip(
                Path.Combine(root, "bundle.zip"),
                ("nested/main.mq5", canonicalSource),
                ("nested/tool.ex4", compiled));

            Mql5CorpusManifest canonicalManifest = new Mql5StaticInventoryAnalyzer().Analyze(
                [new Mql5SourceDocument("main.mq5", canonicalSource)]);
            var job = new Mql5QuarantineIntakeJob();

            Mql5QuarantineIntakeEvidence first = await job.AnalyzeDirectoryAsync(
                root,
                canonicalManifest,
                TestContext.Current.CancellationToken);
            Mql5QuarantineIntakeEvidence second = await job.AnalyzeDirectoryAsync(
                root,
                canonicalManifest,
                TestContext.Current.CancellationToken);
            string json = Mql5QuarantineIntakeFormatter.ToJson(first);
            Assert.DoesNotContain('\r', json);
            Assert.EndsWith("\n", json, StringComparison.Ordinal);

            Assert.Equal(1, first.CanonicalCorpus.FileCount);
            Assert.Equal(5, first.Summary.NonCanonicalFileCount);
            Assert.Equal(1, first.Summary.SourceLikeTextCandidateCount);
            Assert.Equal(1, first.Summary.LegacyMql4SourceCount);
            Assert.Equal(2, first.Summary.CompiledMql4BinaryCount);
            Assert.Equal(1, first.Summary.ArchiveCount);
            Assert.Equal(2, first.Summary.VerifiedArchiveFileEntryCount);
            Assert.Equal(1, first.Summary.VerifiedObjectsMatchingCanonicalCount);
            Assert.Equal(1, first.Summary.CanonicalPathsMatched);
            Assert.Equal(1, first.Summary.ExactIntakeDuplicateGroupCount);
            Assert.True(Mql5QuarantineIntakeFormatter.HasValidEvidenceDigest(first));
            Assert.Equal(json, Mql5QuarantineIntakeFormatter.ToJson(second));
            Assert.DoesNotContain("SYNTHETIC_CANONICAL_BODY", json, StringComparison.Ordinal);
            Assert.DoesNotContain("SYNTHETIC_QUARANTINE_BODY", json, StringComparison.Ordinal);

            Mql5QuarantineFileEvidence renamed = Assert.Single(
                first.Files,
                static file => file.RelativePath == "renamed mq5.txt");
            Assert.Equal(
                Mql5QuarantineClassification.SourceLikeTextCandidate,
                renamed.Classification);
            Assert.NotEmpty(renamed.SourceSignalCodes);

            Mql5QuarantineFileEvidence legacy = Assert.Single(
                first.Files,
                static file => file.RelativePath == "legacy.mq4");
            Assert.Equal("utf-16le", legacy.TextEncoding);

            Mql5QuarantineFileEvidence archive = Assert.Single(
                first.Files,
                static file => file.RelativePath == "bundle.zip");
            Assert.Equal(Mql5QuarantineArchiveState.Inspected, archive.Archive!.State);
            Mql5QuarantineArchiveEntryEvidence archivedSource = Assert.Single(
                archive.Archive.Entries,
                static entry => entry.RelativePath == "nested/main.mq5");
            Assert.Equal(
                Mql5QuarantineArchiveEntryContentState.VerifiedDigest,
                archivedSource.ContentState);
            Assert.Equal(1, archivedSource.ExactCanonicalMatchCount);
            Assert.Equal(["main.mq5"], archivedSource.ExactCanonicalMatchSamples);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task UnsafeArchivePathIsRejectedWithoutExtraction()
    {
        string testRoot = CreateTemporaryRoot("unsafe-archive");
        string sourceRoot = Path.Combine(testRoot, "source");
        Directory.CreateDirectory(sourceRoot);
        try
        {
            byte[] canonicalSource = Encoding.UTF8.GetBytes("void OnTick() {}\n");
            await File.WriteAllBytesAsync(
                Path.Combine(sourceRoot, "main.mq5"),
                canonicalSource,
                TestContext.Current.CancellationToken);
            CreateZip(
                Path.Combine(sourceRoot, "unsafe.zip"),
                ("../escape.mq5", canonicalSource));
            Mql5CorpusManifest canonicalManifest = new Mql5StaticInventoryAnalyzer().Analyze(
                [new Mql5SourceDocument("main.mq5", canonicalSource)]);

            Mql5QuarantineIntakeEvidence evidence = await new Mql5QuarantineIntakeJob()
                .AnalyzeDirectoryAsync(
                    sourceRoot,
                    canonicalManifest,
                    TestContext.Current.CancellationToken);

            Mql5QuarantineArchiveEvidence archive = Assert.Single(evidence.Files).Archive!;
            Assert.Equal(Mql5QuarantineArchiveState.RejectedUnsafeMetadata, archive.State);
            Assert.Equal("RELATIVE_PATH_INVALID", archive.ReasonCode);
            Assert.Empty(archive.Entries);
            Assert.False(File.Exists(Path.Combine(testRoot, "escape.mq5")));
        }
        finally
        {
            Directory.Delete(testRoot, recursive: true);
        }
    }

    [Fact]
    public async Task CanonicalDuplicateMatchesRetainExactCountWithOnlyBoundedSamples()
    {
        string root = CreateTemporaryRoot("canonical-match-samples");
        try
        {
            await File.WriteAllBytesAsync(
                Path.Combine(root, "matching.txt"),
                [],
                TestContext.Current.CancellationToken);
            Mql5CorpusManifest baseline = new Mql5StaticInventoryAnalyzer().Analyze(
                [new Mql5SourceDocument("baseline.mq5", [])]);
            Mql5SourceManifest template = Assert.Single(baseline.Files);
            Mql5SourceManifest[] files = Enumerable.Range(0, Mql5CorpusInventoryJob.MaximumFileCount)
                .Select(index => template with
                {
                    RelativePath = $"canonical/{index:D5}.mq5"
                })
                .ToArray();
            var corpusMaterial = new StringBuilder();
            foreach (Mql5SourceManifest file in files)
            {
                corpusMaterial.Append(file.RelativePath)
                    .Append('\0')
                    .Append(file.Sha256)
                    .Append('\n');
            }

            string corpusSha256 = Convert.ToHexString(SHA256.HashData(
                    Encoding.UTF8.GetBytes(corpusMaterial.ToString())))
                .ToLowerInvariant();
            Mql5CorpusManifest canonicalManifest = baseline with
            {
                CorpusSha256 = corpusSha256,
                FileCount = files.Length,
                TotalBytes = 0,
                Files = Array.AsReadOnly(files)
            };

            Mql5QuarantineIntakeEvidence evidence = await new Mql5QuarantineIntakeJob()
                .AnalyzeDirectoryAsync(
                    root,
                    canonicalManifest,
                    TestContext.Current.CancellationToken);

            Mql5QuarantineFileEvidence match = Assert.Single(evidence.Files);
            Assert.Equal(Mql5CorpusInventoryJob.MaximumFileCount, match.ExactCanonicalMatchCount);
            Assert.Equal(
                Mql5QuarantineIntakeJob.MaximumCanonicalMatchSamplesPerObject,
                match.ExactCanonicalMatchSamples.Count);
            Assert.Equal(
                Mql5CorpusInventoryJob.MaximumFileCount,
                evidence.Summary.CanonicalPathsMatched);
            Assert.True(Mql5QuarantineIntakeFormatter.HasValidEvidenceDigest(evidence));
            Assert.True(
                Encoding.UTF8.GetByteCount(Mql5QuarantineIntakeFormatter.ToJson(evidence))
                    < Mql5QuarantineIntakeJob.MaximumArtifactUtf8Bytes);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ArchiveEntryNamesAreRenderedAsInertMarkdownMetadata()
    {
        string root = CreateTemporaryRoot("report-escaping");
        try
        {
            byte[] canonicalSource = Encoding.UTF8.GetBytes("void OnTick() {}\n");
            await File.WriteAllBytesAsync(
                Path.Combine(root, "main.mq5"),
                canonicalSource,
                TestContext.Current.CancellationToken);
            const string HostileEntry =
                "nested/[click](javascript-alert)<img src=x>`tick`&name.mq5";
            CreateZip(Path.Combine(root, "hostile.zip"), (HostileEntry, canonicalSource));
            Mql5CorpusManifest canonicalManifest = new Mql5StaticInventoryAnalyzer().Analyze(
                [new Mql5SourceDocument("main.mq5", canonicalSource)]);

            Mql5QuarantineIntakeEvidence evidence = await new Mql5QuarantineIntakeJob()
                .AnalyzeDirectoryAsync(
                    root,
                    canonicalManifest,
                    TestContext.Current.CancellationToken);
            string report = Mql5QuarantineIntakeFormatter.ToMarkdown(evidence);

            Assert.DoesNotContain("[click](javascript-alert)", report, StringComparison.Ordinal);
            Assert.DoesNotContain("<img", report, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("`tick`", report, StringComparison.Ordinal);
            Assert.Contains("&#91;click&#93;&#40;javascript-alert&#41;", report, StringComparison.Ordinal);
            Assert.Contains("&lt;img src=x&gt;", report, StringComparison.Ordinal);
            Assert.Contains("&#96;tick&#96;&amp;name.mq5", report, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    public async Task QuarantineCommandRejectsEveryOutputInsideSourceRoot(
        int hostileOutputIndex,
        bool useCaseAlias)
    {
        string root = CreateTemporaryRoot("output-boundary");
        string sourceRoot = Path.Combine(root, "Source");
        Directory.CreateDirectory(sourceRoot);
        try
        {
            string sourcePath = Path.Combine(sourceRoot, "main.mq5");
            const string Source = "void OnTick() {}";
            await File.WriteAllTextAsync(
                sourcePath,
                Source,
                TestContext.Current.CancellationToken);
            string[] outputs =
            [
                Path.Combine(root, "quarantine.json"),
                Path.Combine(root, "quarantine.md")
            ];
            string aliasedRoot = useCaseAlias ? Path.Combine(root, "sOURCE") : sourceRoot;
            outputs[hostileOutputIndex] = hostileOutputIndex == 0
                ? Path.Combine(aliasedRoot, "main.mq5")
                : Path.Combine(aliasedRoot, "nested", "quarantine.md");

            int exitCode = await Mql5QuarantineIntakeCommand.RunAsync(
            [
                "--quarantine-intake",
                "--source-root", sourceRoot,
                "--evidence-output", outputs[0],
                "--report-output", outputs[1]
            ], TestContext.Current.CancellationToken);

            Assert.Equal(2, exitCode);
            Assert.Equal(Source, await File.ReadAllTextAsync(
                sourcePath,
                TestContext.Current.CancellationToken));
            Assert.False(File.Exists(Path.Combine(root, "quarantine.json")));
            Assert.False(File.Exists(Path.Combine(root, "quarantine.md")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ArchiveCompressionRatioLimitRejectsBombShapeBeforeEntryRead()
    {
        string root = CreateTemporaryRoot("ratio-limit");
        try
        {
            byte[] canonicalSource = Encoding.UTF8.GetBytes("void OnTick() {}\n");
            await File.WriteAllBytesAsync(
                Path.Combine(root, "main.mq5"),
                canonicalSource,
                TestContext.Current.CancellationToken);
            CreateZip(
                Path.Combine(root, "ratio.zip"),
                ("compressed.mq5", new byte[1024 * 1024]));
            Mql5CorpusManifest canonicalManifest = new Mql5StaticInventoryAnalyzer().Analyze(
                [new Mql5SourceDocument("main.mq5", canonicalSource)]);

            Mql5QuarantineIntakeEvidence evidence = await new Mql5QuarantineIntakeJob()
                .AnalyzeDirectoryAsync(
                    root,
                    canonicalManifest,
                    TestContext.Current.CancellationToken);

            Mql5QuarantineArchiveEvidence archive = Assert.Single(evidence.Files).Archive!;
            Assert.Equal(Mql5QuarantineArchiveState.RejectedLimit, archive.State);
            Assert.Equal("ZIP_COMPRESSION_RATIO_LIMIT", archive.ReasonCode);
            Assert.Empty(archive.Entries);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void CanonicalEnumeratorStopsAtConfiguredFilesystemEntryLimit()
    {
        string root = CreateTemporaryRoot("entry-limit");
        try
        {
            File.WriteAllText(Path.Combine(root, "one.txt"), string.Empty);
            File.WriteAllText(Path.Combine(root, "two.txt"), string.Empty);
            File.WriteAllText(Path.Combine(root, "three.txt"), string.Empty);

            InvalidDataException error = Assert.Throws<InvalidDataException>(
                () => Mql5CorpusInventoryJob.EnumerateAllowedFilesBounded(
                        new DirectoryInfo(root),
                        maximumFilesystemEntries: 2,
                        maximumDirectories: 2,
                        TestContext.Current.CancellationToken)
                    .ToArray());

            Assert.Equal(
                "The MQL5 source tree exceeds the filesystem-entry traversal limit.",
                error.Message);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void CanonicalEnumeratorStopsAtConfiguredDirectoryLimit()
    {
        string root = CreateTemporaryRoot("directory-limit");
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "one"));
            Directory.CreateDirectory(Path.Combine(root, "two"));

            InvalidDataException error = Assert.Throws<InvalidDataException>(
                () => Mql5CorpusInventoryJob.EnumerateAllowedFilesBounded(
                        new DirectoryInfo(root),
                        maximumFilesystemEntries: 10,
                        maximumDirectories: 2,
                        TestContext.Current.CancellationToken)
                    .ToArray());

            Assert.Equal(
                "The MQL5 source tree exceeds the directory traversal limit.",
                error.Message);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task CanonicalManifestSnapshotUsesOnlyBoundedIndexesAndNormalizesIndexerFailure()
    {
        string root = CreateTemporaryRoot("hostile-manifest");
        try
        {
            byte[] source = Encoding.UTF8.GetBytes("void OnTick() {}\n");
            await File.WriteAllBytesAsync(
                Path.Combine(root, "main.mq5"),
                source,
                TestContext.Current.CancellationToken);
            Mql5CorpusManifest baseline = new Mql5StaticInventoryAnalyzer().Analyze(
                [new Mql5SourceDocument("main.mq5", source)]);
            Mql5CorpusManifest indexOnly = baseline with
            {
                Files = new IndexOnlyManifestFiles(baseline.Files)
            };

            Mql5QuarantineIntakeEvidence evidence = await new Mql5QuarantineIntakeJob()
                .AnalyzeDirectoryAsync(
                    root,
                    indexOnly,
                    TestContext.Current.CancellationToken);

            Assert.Equal(0, evidence.Summary.NonCanonicalFileCount);

            Mql5CorpusManifest throwing = baseline with
            {
                Files = new ThrowingIndexerManifestFiles(baseline.Files.Count)
            };
            ArgumentException error = await Assert.ThrowsAsync<ArgumentException>(
                () => new Mql5QuarantineIntakeJob().AnalyzeDirectoryAsync(
                    root,
                    throwing,
                    TestContext.Current.CancellationToken));
            Assert.StartsWith(
                "The canonical MQL5 manifest file collection is invalid.",
                error.Message,
                StringComparison.Ordinal);
            Assert.DoesNotContain("HOSTILE_INDEXER_DETAIL", error.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateTemporaryRoot(string label)
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            $"yo4x-mql5-quarantine-{label}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        return root;
    }

    private static void CreateZip(
        string path,
        params (string RelativePath, byte[] Content)[] entries)
    {
        using FileStream output = File.Create(path);
        using var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: false);
        foreach ((string relativePath, byte[] content) in entries)
        {
            ZipArchiveEntry entry = archive.CreateEntry(relativePath, CompressionLevel.Optimal);
            using Stream stream = entry.Open();
            stream.Write(content);
        }
    }

    private sealed class IndexOnlyManifestFiles(IReadOnlyList<Mql5SourceManifest> source)
        : IReadOnlyList<Mql5SourceManifest>
    {
        public int Count => source.Count;

        public Mql5SourceManifest this[int index] => source[index];

        public IEnumerator<Mql5SourceManifest> GetEnumerator() =>
            throw new InvalidOperationException("The unbounded enumerator must not be used.");

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() =>
            GetEnumerator();
    }

    private sealed class ThrowingIndexerManifestFiles(int count)
        : IReadOnlyList<Mql5SourceManifest>
    {
        public int Count => count;

        public Mql5SourceManifest this[int index] =>
            throw new InvalidOperationException("HOSTILE_INDEXER_DETAIL");

        public IEnumerator<Mql5SourceManifest> GetEnumerator() =>
            throw new InvalidOperationException("HOSTILE_ENUMERATOR_DETAIL");

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() =>
            GetEnumerator();
    }
}
