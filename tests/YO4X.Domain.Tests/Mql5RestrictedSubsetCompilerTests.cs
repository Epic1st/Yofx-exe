using System.Security.Cryptography;
using System.Text;
using YO4X.StrategyGovernance;

namespace YO4X.Domain.Tests;

public sealed class Mql5RestrictedSubsetCompilerTests
{
    [Fact]
    public void CompilesDataOnlyTranslationUnitIntoDeterministicIr()
    {
        const string Source = """
            #property strict
            input double Risk = 0.0100;
            input bool Enabled = true;
            enum Side { Flat, Buy = 4, Sell };
            struct Signal { datetime time; double price; char side; };
            """;
        Mql5RestrictedCompilation first = Mql5RestrictedSubsetCompiler.Compile(Document(Source));
        Mql5RestrictedCompilation second = Mql5RestrictedSubsetCompiler.Compile(Document(Source));

        Assert.True(first.Succeeded);
        Assert.NotNull(first.Ir);
        Assert.NotNull(second.Ir);
        Assert.Equal(first.Ir.CanonicalJson, second.Ir.CanonicalJson);
        Assert.Equal("0.01", first.Ir.Inputs[0].CanonicalValue);
        Assert.Equal([0L, 4L, 5L], first.Ir.Enums[0].Members.Select(static member => member.Value));
        Assert.Equal(3, first.Ir.Structures[0].Fields.Count);
        Assert.Matches("^[0-9a-f]{64}$", first.Ir.IrSha256);
        string hashlessJson = first.Ir.CanonicalJson.Replace(
            $",\"irSha256\":\"{first.Ir.IrSha256}\"",
            string.Empty,
            StringComparison.Ordinal);
        Assert.Equal(
            first.Ir.IrSha256,
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(hashlessJson))));
    }

    [Fact]
    public void EmptyTranslationUnitProducesBoundEmptyIr()
    {
        Mql5RestrictedCompilation result = Mql5RestrictedSubsetCompiler.Compile(Document(string.Empty));

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Ir);
        Assert.Empty(result.Ir.Structures);
        Assert.Empty(result.Ir.Enums);
        Assert.Empty(result.Ir.Inputs);
    }

    [Theory]
    [InlineData("void OnTick() {}", "UNSUPPORTED_TOKEN")]
    [InlineData("#include <Trade/Trade.mqh>", "UNSUPPORTED_PREPROCESSOR_DIRECTIVE")]
    [InlineData("input double Risk = DBL_MAX;", "INVALID_NUMERIC_LITERAL")]
    [InlineData("struct S { double values[]; };", "ARRAY_FIELD_NOT_SUPPORTED")]
    [InlineData("enum E { A, A };", "DUPLICATE_ENUM_MEMBER")]
    [InlineData("\"", "UNTERMINATED_STRING")]
    public void UnsupportedOrAmbiguousSemanticsFailClosed(string source, string expectedCode)
    {
        Mql5RestrictedCompilation result = Mql5RestrictedSubsetCompiler.Compile(Document(source));

        Assert.False(result.Succeeded);
        Assert.Null(result.Ir);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == expectedCode);
    }

    [Theory]
    [InlineData("input uchar Value = -1;")]
    [InlineData("input char Value = 128;")]
    [InlineData("input ushort Value = 65536;")]
    [InlineData("input int Value = 2147483648;")]
    [InlineData("input uint Value = 4294967296;")]
    [InlineData("input ulong Value = -1;")]
    public void IntegerInputOutsideDeclaredTypeRangeFailsClosed(string source)
    {
        Mql5RestrictedCompilation result = Mql5RestrictedSubsetCompiler.Compile(Document(source));

        Assert.False(result.Succeeded);
        Assert.Null(result.Ir);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "INTEGER_LITERAL_OUT_OF_RANGE");
    }

    [Fact]
    public void UnsignedLongMaximumIsCanonicalizedWithoutNarrowing()
    {
        Mql5RestrictedCompilation result = Mql5RestrictedSubsetCompiler.Compile(
            Document("input ulong Value = 18446744073709551615;"));

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Ir);
        Assert.Equal("18446744073709551615", Assert.Single(result.Ir.Inputs).CanonicalValue);
    }

    [Fact]
    public void ExactPatternTypesHeaderLowersWithoutExecutingSource()
    {
        string repositoryRoot = FindRepositoryRoot();
        byte[] source = File.ReadAllBytes(Path.Combine(repositoryRoot, "Testing", "Mq5", "PatternTypes.mqh"));

        Mql5RestrictedCompilation result = Mql5RestrictedSubsetCompiler.Compile(
            new("PatternTypes.mqh", source));

        Assert.True(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics));
        Assert.NotNull(result.Ir);
        Assert.Equal(["Swing", "Wave"], result.Ir.Structures.Select(static structure => structure.Name));
        Assert.Equal([3, 7], result.Ir.Structures.Select(static structure => structure.Fields.Count));
    }

    [Fact]
    public void ExactAwaitingCorpusIsExhaustivelyAttemptedAndOnlyProvenUnitsLower()
    {
        string repositoryRoot = FindRepositoryRoot();
        string sourceRoot = Path.Combine(repositoryRoot, "Testing", "Mq5");
        Mql5SourceDocument[] documents = Directory
            .EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories)
            .Where(static path => Path.GetExtension(path) is ".mq5" or ".mqh")
            .Select(path => new Mql5SourceDocument(
                Path.GetRelativePath(sourceRoot, path).Replace('\\', '/'),
                File.ReadAllBytes(path)))
            .ToArray();
        Mql5ConversionCorpusEvidence evidence = new Mql5ConversionEvidenceAnalyzer().Analyze(documents);
        string[] candidates = evidence.Files
            .Where(static file => file.Disposition == Mql5ConversionEvidenceDisposition.AwaitingIsolatedTypeCheck)
            .Select(static file => file.RelativePath)
            .ToArray();

        Dictionary<string, Mql5RestrictedCompilation> results = candidates.ToDictionary(
            static path => path,
            path => Mql5RestrictedSubsetCompiler.Compile(
                documents.Single(document => document.RelativePath.Equals(path, StringComparison.OrdinalIgnoreCase))),
            StringComparer.OrdinalIgnoreCase);

        Assert.Equal(30, results.Count);
        Assert.Equal(
            ["FIB 2.mq5", "PatternTypes.mqh"],
            results.Where(static pair => pair.Value.Succeeded).Select(static pair => pair.Key).Order().ToArray());
        Assert.All(results.Where(static pair => !pair.Value.Succeeded), static pair =>
        {
            Assert.Null(pair.Value.Ir);
            Assert.Contains(pair.Value.Diagnostics, static diagnostic =>
                diagnostic.Severity == Mql5RestrictedDiagnosticSeverity.Error);
        });
    }

    private static Mql5SourceDocument Document(string source) => new("test.mq5", Encoding.UTF8.GetBytes(source));

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "YO4X.sln"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException();
    }
}
