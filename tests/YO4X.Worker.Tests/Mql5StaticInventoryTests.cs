using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using YO4X.Conversion.Worker;
using YO4X.StrategyGovernance;

namespace YO4X.Worker.Tests;

public sealed class Mql5StaticInventoryTests
{
    [Fact]
    public void DetectsCapabilitiesWithoutExecutingOrMatchingCommentsAndStrings()
    {
        const string source = """
            #include <Trade/Trade.mqh>
            #import "unsafe.dll"
            int OnInit() { return INIT_SUCCEEDED; }
            void OnTick()
            {
                // WebRequest("GET", "ignored", "", "", 10, payload, 0, response, headers);
                string ignored = "FileOpen(also_ignored)";
                CTrade trade;
                OrderSend(request, result);
                iCustom(_Symbol, PERIOD_CURRENT, "OwnedIndicator");
            }
            """;

        Mql5CorpusManifest corpus = Analyze(("main.mq5", source));
        Mql5SourceManifest file = Assert.Single(corpus.Files);

        Assert.Equal(["OnInit", "OnTick"], file.Entrypoints);
        Assert.Contains(file.Features, feature => feature.Code == "TRADE_CTRADE");
        Assert.Contains(file.Features, feature => feature.Code == "TRADE_ORDER_SEND");
        Assert.Contains(file.Features, feature => feature.Code == "CUSTOM_INDICATOR");
        Assert.Contains(file.Features, feature => feature.Code == "NATIVE_OR_EXTERNAL_IMPORT");
        Assert.DoesNotContain(file.Features, feature => feature.Code == "NETWORK_IO");
        Assert.DoesNotContain(file.Features, feature => feature.Code == "FILE_IO");
        Assert.Equal(Mql5IncludeResolution.PlatformLibrary, Assert.Single(file.Includes).Resolution);
        Assert.Equal(Mql5StaticDisposition.Unsupported, file.Disposition);
        Assert.True(file.Verification.StaticInventoryCompleted);
        Assert.False(file.Verification.ParsedAndTypeChecked);
        Assert.False(file.Verification.SemanticConversionProven);
        Assert.False(file.Verification.MetaEditorCompileProven);
        Assert.False(file.Verification.ReferenceParityProven);
        Assert.False(file.Verification.DemoRuntimeProven);
    }

    [Fact]
    public void ResolvesCorpusIncludesAndReportsMissingSource()
    {
        Mql5CorpusManifest corpus = Analyze(
            ("main.mq5", "#include \"lib/helper.mqh\"\n#include \"missing.mqh\"\nvoid OnTick() {}"),
            ("lib/helper.mqh", "double Signal() { return 1.0; }"));

        Mql5SourceManifest main = Assert.Single(corpus.Files, file => file.RelativePath == "main.mq5");
        Assert.Collection(
            main.Includes,
            include =>
            {
                Assert.Equal(Mql5IncludeResolution.ResolvedInCorpus, include.Resolution);
                Assert.Equal("lib/helper.mqh", include.ResolvedRelativePath);
            },
            include => Assert.Equal(Mql5IncludeResolution.MissingSource, include.Resolution));
        Assert.Contains(main.Findings, finding => finding.Code == "INCLUDE_SOURCE_MISSING");
        Assert.Equal(Mql5StaticDisposition.NeedsSource, main.Disposition);
    }

    [Fact]
    public void ManifestAndJsonAreDeterministicAcrossInputOrdering()
    {
        (string Path, string Source) first = ("z.mqh", "double Z() { return 2; }");
        (string Path, string Source) second = ("a.mq5", "void OnTick() {}\n");

        Mql5CorpusManifest forward = Analyze(first, second);
        Mql5CorpusManifest reverse = Analyze(second, first);

        Assert.Equal(forward.CorpusSha256, reverse.CorpusSha256);
        Assert.Equal(Mql5InventoryFormatter.ToJson(forward), Mql5InventoryFormatter.ToJson(reverse));
        Assert.Equal(["a.mq5", "z.mqh"], forward.Files.Select(file => file.RelativePath));
    }

    [Fact]
    public void PersistenceJsonFragmentsExactlyMatchManifestEnumRepresentation()
    {
        Mql5CorpusManifest corpus = Analyze((
            "main.mq5",
            "#include <Trade/Trade.mqh>\n#import \"native.dll\"\nvoid OnTick() { OrderSend(request, result); }"));
        Mql5SourceManifest file = Assert.Single(corpus.Files);
        JsonNode manifestFile = JsonNode.Parse(Mql5InventoryFormatter.ToJson(corpus))!["files"]![0]!;

        Assert.True(JsonNode.DeepEquals(
            manifestFile["includes"],
            JsonNode.Parse(Mql5InventoryFormatter.ToJsonFragment(file.Includes))));
        Assert.True(JsonNode.DeepEquals(
            manifestFile["features"],
            JsonNode.Parse(Mql5InventoryFormatter.ToJsonFragment(file.Features))));
        Assert.True(JsonNode.DeepEquals(
            manifestFile["findings"],
            JsonNode.Parse(Mql5InventoryFormatter.ToJsonFragment(file.Findings))));
        Assert.True(JsonNode.DeepEquals(
            manifestFile["verification"],
            JsonNode.Parse(Mql5InventoryFormatter.ToJsonFragment(file.Verification))));
    }

    [Fact]
    public void RecognizesUtf16AndNeverLeaksSourceBodiesIntoReports()
    {
        const string source = "void OnTick() { double PRIVATE_ALPHA_LOGIC = 7; }";
        byte[] utf16 = Encoding.Unicode.GetPreamble()
            .Concat(Encoding.Unicode.GetBytes(source))
            .ToArray();
        var analyzer = new Mql5StaticInventoryAnalyzer();

        Mql5CorpusManifest corpus = analyzer.Analyze([new Mql5SourceDocument("main.mq5", utf16)]);
        string report = Mql5InventoryFormatter.ToMarkdown(corpus);
        string manifest = Mql5InventoryFormatter.ToJson(corpus);

        Assert.Equal("utf-16le", Assert.Single(corpus.Files).TextEncoding);
        Assert.DoesNotContain("PRIVATE_ALPHA_LOGIC", report, StringComparison.Ordinal);
        Assert.DoesNotContain("PRIVATE_ALPHA_LOGIC", manifest, StringComparison.Ordinal);
    }

    [Fact]
    public void RecognizesBomlessUtf16WithoutMisclassifyingItAsNulFilledUtf8()
    {
        const string source = "void OnTick() { double value = 7; }";
        byte[] utf16WithoutBom = Encoding.Unicode.GetBytes(source);
        var document = new Mql5SourceDocument("main.mq5", utf16WithoutBom);

        Mql5SourceManifest staticFile = Assert.Single(
            new Mql5StaticInventoryAnalyzer().Analyze([document]).Files);
        Mql5ConversionFileEvidence conversionFile = Assert.Single(
            new Mql5ConversionEvidenceAnalyzer().Analyze([document]).Files);

        Assert.Equal("utf-16le-no-bom", staticFile.TextEncoding);
        Assert.Equal(["OnTick"], staticFile.Entrypoints);
        Assert.DoesNotContain(
            conversionFile.Findings,
            finding => finding.Code == "LEXICAL_NUL_CHARACTER");
        Assert.True(conversionFile.Structural.DelimitersBalanced);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void MalformedBomUtf16IsRejectedWithoutReplacementDecoding(
        bool bigEndian,
        bool oddTrailingByte)
    {
        byte[] preamble = bigEndian
            ? Encoding.BigEndianUnicode.GetPreamble()
            : Encoding.Unicode.GetPreamble();
        byte[] malformedPayload = oddTrailingByte
            ? [0x00]
            : bigEndian
                ? [0xd8, 0x00]
                : [0x00, 0xd8];
        byte[] malformed = preamble.Concat(malformedPayload).ToArray();
        var document = new Mql5SourceDocument("malformed.mq5", malformed);

        Mql5SourceManifest staticFile = Assert.Single(
            new Mql5StaticInventoryAnalyzer().Analyze([document]).Files);
        Mql5ConversionFileEvidence conversionFile = Assert.Single(
            new Mql5ConversionEvidenceAnalyzer().Analyze([document]).Files);

        Assert.Equal("binary-non-text", staticFile.TextEncoding);
        Assert.Contains(
            staticFile.Findings,
            finding => finding.Code == "SOURCE_CONTENT_BINARY_OR_NON_TEXT");
        Assert.Equal(
            Mql5ConversionEvidenceDisposition.BlockedBinarySource,
            conversionFile.Disposition);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BomUtf16EmbeddedNulIsReportedAndFailsClosed(bool bigEndian)
    {
        const string source = "void OnTick() {}\0";
        byte[] encoded = bigEndian
            ? Encoding.BigEndianUnicode.GetPreamble()
                .Concat(Encoding.BigEndianUnicode.GetBytes(source))
                .ToArray()
            : Encoding.Unicode.GetPreamble()
                .Concat(Encoding.Unicode.GetBytes(source))
                .ToArray();
        var document = new Mql5SourceDocument("embedded-nul.mq5", encoded);

        Mql5SourceManifest staticFile = Assert.Single(
            new Mql5StaticInventoryAnalyzer().Analyze([document]).Files);
        Mql5ConversionFileEvidence conversionFile = Assert.Single(
            new Mql5ConversionEvidenceAnalyzer().Analyze([document]).Files);

        Assert.Equal(bigEndian ? "utf-16be" : "utf-16le", staticFile.TextEncoding);
        Assert.Contains(
            staticFile.Findings,
            finding => finding.Code == "SOURCE_FORBIDDEN_CONTROL_CHARACTERS");
        Assert.Equal(1, conversionFile.Lexical.NulCharacterCount);
        Assert.Single(
            conversionFile.Findings,
            finding => finding.Code == "LEXICAL_NUL_CHARACTERS_PRESENT");
        Assert.Equal(
            Mql5ConversionEvidenceDisposition.BlockedInvalidSyntax,
            conversionFile.Disposition);
    }

    [Fact]
    public void DecodesWindows1252StrictlyAndAggregatesForbiddenControls()
    {
        byte[] prefix = Encoding.ASCII.GetBytes("void OnTick() {} // copyright ");
        byte[] suffix = Encoding.ASCII.GetBytes("\n");
        byte[] windows1252 = prefix
            .Concat(new byte[] { 0xa9, 0x14 })
            .Concat(suffix)
            .ToArray();
        var document = new Mql5SourceDocument("main.mq5", windows1252);

        Mql5SourceManifest staticFile = Assert.Single(
            new Mql5StaticInventoryAnalyzer().Analyze([document]).Files);
        Mql5ConversionFileEvidence conversionFile = Assert.Single(
            new Mql5ConversionEvidenceAnalyzer().Analyze([document]).Files);

        Assert.Equal("windows-1252", staticFile.TextEncoding);
        Assert.Equal(["OnTick"], staticFile.Entrypoints);
        Assert.Contains(
            staticFile.Findings,
            finding => finding.Code == "SOURCE_WINDOWS_1252_ENCODING_REQUIRES_REVIEW");
        Assert.Contains(
            staticFile.Findings,
            finding => finding.Code == "SOURCE_FORBIDDEN_CONTROL_CHARACTERS");
        Assert.Equal(1, conversionFile.Lexical.ForbiddenControlCharacterCount);
        Assert.Single(
            conversionFile.Findings,
            finding => finding.Code == "LEXICAL_FORBIDDEN_CONTROL_CHARACTERS_PRESENT");
    }

    [Fact]
    public void ClassifiesAllNulAndBinaryArtifactsSeparatelyWithAggregateFindings()
    {
        byte[] allNul = new byte[512];
        byte[] binary = [0x45, 0x58, 0x2d, 0x02, 0x70, 0x62, 0x09, 0x00, 0x10, 0xff];
        var documents = new[]
        {
            new Mql5SourceDocument("all-nul.mq5", allNul),
            new Mql5SourceDocument("binary.mq5", binary)
        };

        Mql5CorpusManifest staticEvidence = new Mql5StaticInventoryAnalyzer().Analyze(documents);
        Mql5ConversionCorpusEvidence conversionEvidence = new Mql5ConversionEvidenceAnalyzer()
            .Analyze(documents);
        Mql5SourceManifest allNulStatic = Assert.Single(
            staticEvidence.Files,
            file => file.RelativePath == "all-nul.mq5");
        Mql5SourceManifest binaryStatic = Assert.Single(
            staticEvidence.Files,
            file => file.RelativePath == "binary.mq5");
        Mql5ConversionFileEvidence allNulConversion = Assert.Single(
            conversionEvidence.Files,
            file => file.RelativePath == "all-nul.mq5");
        Mql5ConversionFileEvidence binaryConversion = Assert.Single(
            conversionEvidence.Files,
            file => file.RelativePath == "binary.mq5");

        Assert.Equal("binary-all-nul", allNulStatic.TextEncoding);
        Assert.Contains(allNulStatic.Findings, finding => finding.Code == "SOURCE_CONTENT_ALL_NUL");
        Assert.Equal(
            Mql5ConversionEvidenceDisposition.BlockedAllNulSource,
            allNulConversion.Disposition);
        Assert.Equal(512, allNulConversion.Lexical.NulCharacterCount);
        Assert.Single(
            allNulConversion.Findings,
            finding => finding.Code == "LEXICAL_NUL_CHARACTERS_PRESENT");

        Assert.Equal("binary-non-text", binaryStatic.TextEncoding);
        Assert.Contains(
            binaryStatic.Findings,
            finding => finding.Code == "SOURCE_CONTENT_BINARY_OR_NON_TEXT");
        Assert.Equal(
            Mql5ConversionEvidenceDisposition.BlockedBinarySource,
            binaryConversion.Disposition);
    }

    [Fact]
    public async Task DirectoryJobReadsOnlyAllowedSourceExtensions()
    {
        string root = Path.Combine(Path.GetTempPath(), "yo4x-mql5-static-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "strategy.mq5"), "void OnTick() {}");
            await File.WriteAllTextAsync(Path.Combine(root, "credentials.txt"), "SYNTHETIC_SECRET_MUST_NOT_APPEAR");
            var job = new Mql5CorpusInventoryJob(new Mql5StaticInventoryAnalyzer());

            Mql5CorpusManifest corpus = await job.AnalyzeDirectoryAsync(root);
            string serialized = Mql5InventoryFormatter.ToJson(corpus);

            Assert.Equal(1, corpus.FileCount);
            Assert.Equal("strategy.mq5", Assert.Single(corpus.Files).RelativePath);
            Assert.DoesNotContain("credentials", serialized, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("SYNTHETIC_SECRET_MUST_NOT_APPEAR", serialized, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task DirectoryJobZeroesRetainedSourceWhenAnalysisFails()
    {
        string root = Path.Combine(Path.GetTempPath(), "yo4x-mql5-failure-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "strategy.mq5"), "void OnTick() {}");
            var analyzer = new CapturingFailingAnalyzer();
            var job = new Mql5CorpusInventoryJob(analyzer);

            await Assert.ThrowsAsync<InvalidDataException>(
                () => job.AnalyzeDirectoryForPersistenceAsync(root));

            Assert.NotNull(analyzer.CapturedContent);
            Assert.All(analyzer.CapturedContent!, static value => Assert.Equal(0, value));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task PersistenceCapabilityCopyAndDisposalNeverExposePartiallyClearedBytes()
    {
        byte[] expected = Enumerable.Range(1, 32).Select(static value => (byte)value).ToArray();
        var request = new Mql5CorpusPersistenceRequest(Guid.NewGuid(), expected);
        using var start = new ManualResetEventSlim(false);
        Task<(byte[]? Copy, Exception? Error)>[] attempts = Enumerable.Range(0, 64)
            .Select(_ => Task.Run(() =>
            {
                start.Wait();
                try
                {
                    return (request.CopyCapability(), (Exception?)null);
                }
                catch (Exception exception)
                {
                    return ((byte[]?)null, exception);
                }
            }))
            .ToArray();
        Task dispose = Task.Run(() =>
        {
            start.Wait();
            request.Dispose();
        });

        start.Set();
        await Task.WhenAll(attempts.Cast<Task>().Append(dispose));

        foreach ((byte[]? copy, Exception? error) in attempts.Select(static task => task.Result))
        {
            if (error is not null)
            {
                Assert.IsType<ObjectDisposedException>(error);
                continue;
            }

            Assert.Equal(expected, copy);
            CryptographicOperations.ZeroMemory(copy!);
        }
    }

    [Fact]
    public void PersistenceRebuildsTrustedAnalysisAndRejectsCallerSuppliedEvidence()
    {
        byte[] source = Encoding.UTF8.GetBytes("void OnTick() { OrderSend(request, result); }");
        var document = new Mql5SourceDocument("main.mq5", source);
        Mql5CorpusManifest trusted = new Mql5StaticInventoryAnalyzer().Analyze([document]);
        Mql5SourceManifest fabricatedFile = Assert.Single(trusted.Files) with
        {
            Disposition = Mql5StaticDisposition.Rejected
        };
        Mql5CorpusManifest fabricated = trusted with { Files = [fabricatedFile] };
        using var corpus = new Mql5AnalyzedCorpus(fabricated, [document]);

        InvalidDataException error = Assert.Throws<InvalidDataException>(
            () => PostgresMql5CorpusStore.ValidateAndRebuildCorpus(corpus));

        Assert.Equal(
            "The corpus does not exactly match trusted static-inventory analysis.",
            error.Message);
    }

    private static Mql5CorpusManifest Analyze(params (string Path, string Source)[] sources)
    {
        var analyzer = new Mql5StaticInventoryAnalyzer();
        return analyzer.Analyze(sources.Select(source => new Mql5SourceDocument(
            source.Path,
            Encoding.UTF8.GetBytes(source.Source))));
    }

    private sealed class CapturingFailingAnalyzer : IMql5StaticInventoryAnalyzer
    {
        public byte[]? CapturedContent { get; private set; }

        public Mql5CorpusManifest Analyze(IEnumerable<Mql5SourceDocument> sourceDocuments)
        {
            CapturedContent = Assert.Single(sourceDocuments).Content;
            throw new InvalidDataException("Synthetic analyzer failure.");
        }
    }
}
