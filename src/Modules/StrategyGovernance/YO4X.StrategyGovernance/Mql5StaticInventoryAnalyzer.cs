using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace YO4X.StrategyGovernance;

public sealed partial class Mql5StaticInventoryAnalyzer : IMql5StaticInventoryAnalyzer
{
    public const string SchemaVersion = "mql5-static-inventory.v1";
    public const string AnalyzerVersion = "yo4x-mql5-static-analyzer.v3";
    private const int MaximumReportedLinesPerFeature = 32;

    private static readonly Mql5VerificationClaims StaticOnlyClaims = new(
        StaticInventoryCompleted: true,
        ParsedAndTypeChecked: false,
        SemanticConversionProven: false,
        MetaEditorCompileProven: false,
        ReferenceParityProven: false,
        DemoRuntimeProven: false);

    private static readonly FeatureRule[] FeatureRules =
    [
        new("INPUT_PARAMETERS", Mql5FeatureSupport.SupportedSubsetCandidate, InputDeclarationRegex()),
        new("TRADE_ORDER_SEND", Mql5FeatureSupport.ReviewRequired, OrderSendRegex()),
        new("TRADE_CTRADE", Mql5FeatureSupport.ReviewRequired, CTradeRegex()),
        new("TRADE_STATE_READ", Mql5FeatureSupport.SupportedSubsetCandidate, TradeStateReadRegex()),
        new("CUSTOM_INDICATOR", Mql5FeatureSupport.NeedsSource, CustomIndicatorRegex()),
        new("FILE_IO", Mql5FeatureSupport.Unsupported, FileIoRegex()),
        new("NETWORK_IO", Mql5FeatureSupport.Unsupported, NetworkIoRegex()),
        new("OPENCL", Mql5FeatureSupport.Unsupported, OpenClRegex()),
        new("CHART_OR_OBJECT_UI", Mql5FeatureSupport.Unsupported, ChartUiRegex()),
        new("TERMINAL_STATE", Mql5FeatureSupport.Unsupported, TerminalStateRegex()),
        new("PERSISTED_TERMINAL_GLOBALS", Mql5FeatureSupport.Unsupported, TerminalGlobalsRegex()),
        new("TIME_OR_SESSION_DEPENDENCY", Mql5FeatureSupport.ReviewRequired, TimeDependencyRegex()),
        new("SYMBOL_SPECIFICATION_DEPENDENCY", Mql5FeatureSupport.SupportedSubsetCandidate, SymbolSpecificationRegex()),
        new("POSITION_MODE_DEPENDENCY", Mql5FeatureSupport.ReviewRequired, PositionModeRegex()),
        new("HISTORY_OR_BARS_DEPENDENCY", Mql5FeatureSupport.ReviewRequired, HistoryDependencyRegex()),
        new("ERROR_OR_RETRY_DEPENDENCY", Mql5FeatureSupport.ReviewRequired, ErrorHandlingRegex()),
        new("TIMER_API", Mql5FeatureSupport.ReviewRequired, TimerApiRegex()),
        new("UNBOUNDED_LOOP_SHAPE", Mql5FeatureSupport.ReviewRequired, UnboundedLoopRegex()),
        new("LOOP_REQUIRES_BOUND_PROOF", Mql5FeatureSupport.ReviewRequired, AnyLoopRegex()),
        new("BROKER_SYMBOL_LITERAL", Mql5FeatureSupport.ReviewRequired, BrokerSymbolLiteralRegex()),
        new("RESOURCE_DEPENDENCY", Mql5FeatureSupport.ReviewRequired, ResourceDirectiveRegex())
    ];

    private static readonly EventRule[] EventRules =
    [
        new("OnInit", Mql5FeatureSupport.SupportedSubsetCandidate),
        new("OnDeinit", Mql5FeatureSupport.SupportedSubsetCandidate),
        new("OnTick", Mql5FeatureSupport.SupportedSubsetCandidate),
        new("OnTimer", Mql5FeatureSupport.ReviewRequired),
        new("OnTrade", Mql5FeatureSupport.ReviewRequired),
        new("OnTradeTransaction", Mql5FeatureSupport.ReviewRequired),
        new("OnBookEvent", Mql5FeatureSupport.ReviewRequired),
        new("OnChartEvent", Mql5FeatureSupport.Unsupported),
        new("OnCalculate", Mql5FeatureSupport.Unsupported),
        new("OnStart", Mql5FeatureSupport.Unsupported),
        new("OnTester", Mql5FeatureSupport.Unsupported),
        new("OnTesterInit", Mql5FeatureSupport.Unsupported),
        new("OnTesterPass", Mql5FeatureSupport.Unsupported),
        new("OnTesterDeinit", Mql5FeatureSupport.Unsupported)
    ];

    public Mql5CorpusManifest Analyze(IEnumerable<Mql5SourceDocument> sourceDocuments)
    {
        ArgumentNullException.ThrowIfNull(sourceDocuments);

        var ownedSnapshots = new List<Mql5SourceDocument>();
        try
        {
            foreach (Mql5SourceDocument document in sourceDocuments)
            {
                ownedSnapshots.Add(ValidateAndNormalizeDocument(document));
            }

            Mql5SourceDocument[] documents = ownedSnapshots
                .OrderBy(static document => document.RelativePath, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static document => document.RelativePath, StringComparer.Ordinal)
                .ToArray();

            EnsureUniquePaths(documents);
            Dictionary<string, string[]> corpusPathIndex = BuildCorpusPathIndex(documents);

            Mql5SourceManifest[] files = documents
                .Select(document => AnalyzeDocument(document, corpusPathIndex))
                .ToArray();

            return new Mql5CorpusManifest(
                SchemaVersion,
                AnalyzerVersion,
                ComputeCorpusDigest(files),
                files.Length,
                files.Sum(static file => file.ByteLength),
                files);
        }
        finally
        {
            foreach (Mql5SourceDocument snapshot in ownedSnapshots)
            {
                CryptographicOperations.ZeroMemory(snapshot.Content);
            }
        }
    }

    private static Mql5SourceDocument ValidateAndNormalizeDocument(Mql5SourceDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(document.RelativePath);
        ArgumentNullException.ThrowIfNull(document.Content);

        string relativePath = NormalizeRelativePath(document.RelativePath);
        string extension = Path.GetExtension(relativePath);
        if (!extension.Equals(".mq5", StringComparison.OrdinalIgnoreCase)
            && !extension.Equals(".mqh", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Only .mq5 and .mqh source documents are accepted.", nameof(document));
        }

        var snapshot = document with
        {
            RelativePath = relativePath,
            Content = document.Content.ToArray()
        };
        try
        {
            Mql5SourceSecretScanner.EnsureNoHighConfidenceSecrets(snapshot);
            return snapshot;
        }
        catch
        {
            CryptographicOperations.ZeroMemory(snapshot.Content);
            throw;
        }
    }

    private static string NormalizeRelativePath(string path)
    {
        string candidate = path.Replace('\\', '/').Trim();
        if (candidate[0] == '/'
            || Path.IsPathRooted(candidate)
            || candidate.Contains('\0'))
        {
            throw new ArgumentException("Source paths must be safe relative paths.", nameof(path));
        }

        var segments = new List<string>();
        foreach (string segment in candidate.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == ".")
            {
                continue;
            }

            if (segment == "..")
            {
                if (segments.Count == 0)
                {
                    throw new ArgumentException("Source paths cannot escape the corpus root.", nameof(path));
                }

                segments.RemoveAt(segments.Count - 1);
                continue;
            }

            segments.Add(segment);
        }

        if (segments.Count == 0)
        {
            throw new ArgumentException("A source filename is required.", nameof(path));
        }

        return string.Join('/', segments);
    }

    private static void EnsureUniquePaths(IReadOnlyList<Mql5SourceDocument> documents)
    {
        string? duplicate = documents
            .GroupBy(static document => document.RelativePath, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(static group => group.Count() > 1)
            ?.Key;

        if (duplicate is not null)
        {
            throw new ArgumentException($"Duplicate source path '{duplicate}' is not allowed.", nameof(documents));
        }
    }

    private static Dictionary<string, string[]> BuildCorpusPathIndex(
        IEnumerable<Mql5SourceDocument> documents)
    {
        return documents
            .GroupBy(static document => document.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                static group => group.Key,
                static group => group.Select(static item => item.RelativePath).ToArray(),
                StringComparer.OrdinalIgnoreCase);
    }

    private static Mql5SourceManifest AnalyzeDocument(
        Mql5SourceDocument document,
        Dictionary<string, string[]> corpusPathIndex)
    {
        DecodedSource decoded = Decode(document.Content);
        MaskedSource masked = MaskSource(decoded.Text);
        var findings = new List<Mql5CompatibilityFinding>();

        if (decoded.ContentKind == Mql5SourceContentKind.AllNul)
        {
            findings.Add(new Mql5CompatibilityFinding(
                "SOURCE_CONTENT_ALL_NUL",
                Mql5FindingSeverity.Error,
                Mql5FeatureSupport.NeedsSource,
                "The source artifact contains only NUL bytes and has no analyzable MQL5 text.",
                []));
        }
        else if (decoded.ContentKind == Mql5SourceContentKind.Binary)
        {
            findings.Add(new Mql5CompatibilityFinding(
                "SOURCE_CONTENT_BINARY_OR_NON_TEXT",
                Mql5FindingSeverity.Error,
                Mql5FeatureSupport.NeedsSource,
                "The source artifact contains binary/non-text byte structure and requires an owned textual source replacement.",
                []));
        }
        else if (decoded.EncodingName == "windows-1252")
        {
            findings.Add(new Mql5CompatibilityFinding(
                "SOURCE_WINDOWS_1252_ENCODING_REQUIRES_REVIEW",
                Mql5FindingSeverity.Warning,
                Mql5FeatureSupport.ReviewRequired,
                "The file is deterministic Windows-1252 text rather than UTF-8/UTF-16; the raw source hash remains authoritative.",
                []));
        }
        else if (decoded.UsedFallbackEncoding)
        {
            findings.Add(new Mql5CompatibilityFinding(
                "SOURCE_ENCODING_REQUIRES_REVIEW",
                Mql5FindingSeverity.Warning,
                Mql5FeatureSupport.ReviewRequired,
                "The file is not valid UTF-8/UTF-16; identifiers were inventoried with a deterministic single-byte fallback.",
                []));
        }

        if (decoded.ContentKind == Mql5SourceContentKind.Text
            && decoded.ForbiddenControlCharacterCount > 0)
        {
            findings.Add(new Mql5CompatibilityFinding(
                "SOURCE_FORBIDDEN_CONTROL_CHARACTERS",
                Mql5FindingSeverity.Error,
                Mql5FeatureSupport.NeedsSource,
                $"The decoded text contains {decoded.ForbiddenControlCharacterCount.ToString(CultureInfo.InvariantCulture)} forbidden control characters; no characters were removed or replaced.",
                []));
        }

        if (masked.UnterminatedBlockComment || masked.UnterminatedLiteral)
        {
            findings.Add(new Mql5CompatibilityFinding(
                "LEXICAL_STRUCTURE_INCOMPLETE",
                Mql5FindingSeverity.Error,
                Mql5FeatureSupport.ReviewRequired,
                "An unterminated block comment or literal prevents reliable static classification.",
                []));
        }

        int[] lineStarts = BuildLineStarts(masked.CodeOnly);
        List<Mql5DetectedFeature> features = DetectFeatures(
            masked.CodeOnly,
            masked.CommentsRemoved,
            lineStarts);
        string[] entrypoints = DetectEntrypoints(masked.CodeOnly, lineStarts, features, findings);
        Mql5IncludeManifest[] includes = AnalyzeIncludes(
            document.RelativePath,
            masked.CommentsRemoved,
            lineStarts,
            corpusPathIndex,
            features,
            findings);

        features = ConsolidateFeatures(features);
        AddFeatureFindings(features, findings);
        AddProgramShapeFindings(document.RelativePath, entrypoints, findings);

        findings.Add(new Mql5CompatibilityFinding(
            "SEMANTIC_VALIDATION_NOT_PERFORMED",
            Mql5FindingSeverity.Information,
            Mql5FeatureSupport.ReviewRequired,
            "This result is a non-executing lexical inventory, not parser, type-check, conversion, compile, parity, or runtime evidence.",
            []));

        Mql5CompatibilityFinding[] orderedFindings = findings
            .DistinctBy(static finding => (finding.Code, string.Join(',', finding.Lines)))
            .OrderByDescending(static finding => finding.Severity)
            .ThenBy(static finding => finding.Code, StringComparer.Ordinal)
            .ToArray();

        return new Mql5SourceManifest(
            document.RelativePath,
            Path.GetExtension(document.RelativePath).Equals(".mq5", StringComparison.OrdinalIgnoreCase)
                ? Mql5SourceKind.ExpertOrProgram
                : Mql5SourceKind.Header,
            document.Content.LongLength,
            Convert.ToHexString(SHA256.HashData(document.Content)).ToLowerInvariant(),
            decoded.EncodingName,
            entrypoints,
            includes,
            features.OrderBy(static feature => feature.Code, StringComparer.Ordinal).ToArray(),
            orderedFindings,
            DetermineDisposition(orderedFindings),
            StaticOnlyClaims);
    }

    private static List<Mql5DetectedFeature> DetectFeatures(
        string codeOnly,
        string commentsRemoved,
        int[] lineStarts)
    {
        var features = new List<Mql5DetectedFeature>();
        foreach (FeatureRule rule in FeatureRules)
        {
            Match[] matches = rule.Regex.Matches(
                    rule.Code is "BROKER_SYMBOL_LITERAL" or "RESOURCE_DEPENDENCY"
                        ? commentsRemoved
                        : codeOnly)
                .Cast<Match>()
                .ToArray();

            if (matches.Length == 0)
            {
                continue;
            }

            features.Add(new Mql5DetectedFeature(
                rule.Code,
                rule.Support,
                matches.Length,
                GetReportedLines(lineStarts, matches)));
        }

        return features;
    }

    private static string[] DetectEntrypoints(
        string codeOnly,
        int[] lineStarts,
        List<Mql5DetectedFeature> features,
        List<Mql5CompatibilityFinding> findings)
    {
        var entrypoints = new List<string>();
        foreach (EventRule rule in EventRules)
        {
            Regex regex = new(
                $@"\b(?:int|void|double)\s+{Regex.Escape(rule.Name)}\s*\(",
                RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture);
            Match[] matches = regex.Matches(codeOnly).Cast<Match>().ToArray();
            if (matches.Length == 0)
            {
                continue;
            }

            int[] lines = GetReportedLines(lineStarts, matches);
            entrypoints.Add(rule.Name);
            features.Add(new Mql5DetectedFeature(
                $"ENTRYPOINT_{ToUpperSnakeCase(rule.Name)}",
                rule.Support,
                matches.Length,
                lines));

            if (rule.Support == Mql5FeatureSupport.Unsupported)
            {
                findings.Add(new Mql5CompatibilityFinding(
                    $"{ToUpperSnakeCase(rule.Name)}_UNSUPPORTED",
                    Mql5FindingSeverity.Error,
                    Mql5FeatureSupport.Unsupported,
                    $"The {rule.Name} event is outside the initial safe strategy subset.",
                    lines));
            }
            else if (rule.Support == Mql5FeatureSupport.ReviewRequired)
            {
                findings.Add(new Mql5CompatibilityFinding(
                    $"{ToUpperSnakeCase(rule.Name)}_SEMANTICS_REQUIRE_REVIEW",
                    Mql5FindingSeverity.Warning,
                    Mql5FeatureSupport.ReviewRequired,
                    $"The {rule.Name} event requires an explicit event-semantics mapping and bounded execution proof.",
                    lines));
            }
        }

        return entrypoints.Order(StringComparer.Ordinal).ToArray();
    }

    private static Mql5IncludeManifest[] AnalyzeIncludes(
        string sourcePath,
        string commentsRemoved,
        int[] lineStarts,
        Dictionary<string, string[]> corpusPathIndex,
        List<Mql5DetectedFeature> features,
        List<Mql5CompatibilityFinding> findings)
    {
        var includes = new List<Mql5IncludeManifest>();
        foreach (Match match in IncludeDirectiveRegex().Matches(commentsRemoved))
        {
            string declaredPath = match.Groups["path"].Value.Trim().Replace('\\', '/');
            bool local = match.Groups["open"].Value == "\"";
            int line = GetLineNumber(lineStarts, match.Index);
            (Mql5IncludeResolution resolution, string? resolvedPath) = ResolveInclude(
                sourcePath,
                declaredPath,
                local,
                corpusPathIndex);

            includes.Add(new Mql5IncludeManifest(
                declaredPath,
                local ? Mql5IncludeKind.Local : Mql5IncludeKind.PlatformOrSearchPath,
                resolution,
                resolvedPath,
                line));

            if (resolution is Mql5IncludeResolution.MissingSource or Mql5IncludeResolution.Ambiguous)
            {
                findings.Add(new Mql5CompatibilityFinding(
                    resolution == Mql5IncludeResolution.MissingSource
                        ? "INCLUDE_SOURCE_MISSING"
                        : "INCLUDE_SOURCE_AMBIGUOUS",
                    Mql5FindingSeverity.Error,
                    resolution == Mql5IncludeResolution.MissingSource
                        ? Mql5FeatureSupport.NeedsSource
                        : Mql5FeatureSupport.ReviewRequired,
                    resolution == Mql5IncludeResolution.MissingSource
                        ? "An included source dependency is not present in this corpus."
                        : "An included dependency maps to more than one corpus path.",
                    [line]));
            }
            else if (resolution == Mql5IncludeResolution.Invalid)
            {
                findings.Add(new Mql5CompatibilityFinding(
                    "INCLUDE_PATH_INVALID",
                    Mql5FindingSeverity.Error,
                    Mql5FeatureSupport.Unsupported,
                    "An include path is absolute, escapes the corpus root, or cannot be normalized safely.",
                    [line]));
            }

            if (IsOperatingSystemInclude(declaredPath))
            {
                findings.Add(new Mql5CompatibilityFinding(
                    "OPERATING_SYSTEM_INCLUDE_UNSUPPORTED",
                    Mql5FindingSeverity.Error,
                    Mql5FeatureSupport.Unsupported,
                    "The include exposes terminal/operating-system integration outside the restricted runtime.",
                    [line]));
            }
        }

        foreach (Match match in ImportDirectiveRegex().Matches(commentsRemoved))
        {
            string target = match.Groups["path"].Value.Trim();
            int line = GetLineNumber(lineStarts, match.Index);
            bool compiledMql = Path.GetExtension(target).Equals(".ex5", StringComparison.OrdinalIgnoreCase);
            string featureCode = compiledMql ? "COMPILED_MQL_IMPORT" : "NATIVE_OR_EXTERNAL_IMPORT";
            Mql5FeatureSupport support = compiledMql
                ? Mql5FeatureSupport.NeedsSource
                : Mql5FeatureSupport.Unsupported;

            features.Add(new Mql5DetectedFeature(featureCode, support, 1, [line]));
            findings.Add(new Mql5CompatibilityFinding(
                compiledMql ? "COMPILED_DEPENDENCY_SOURCE_REQUIRED" : "DLL_OR_EXTERNAL_IMPORT_UNSUPPORTED",
                Mql5FindingSeverity.Error,
                support,
                compiledMql
                    ? "A compiled MQL dependency cannot be converted without its matching source."
                    : "Native DLL and external imports are prohibited by the restricted runtime.",
                [line]));
        }

        return includes
            .OrderBy(static include => include.Line)
            .ThenBy(static include => include.DeclaredPath, StringComparer.Ordinal)
            .ToArray();
    }

    private static (Mql5IncludeResolution Resolution, string? ResolvedPath) ResolveInclude(
        string sourcePath,
        string declaredPath,
        bool local,
        Dictionary<string, string[]> corpusPathIndex)
    {
        if (string.IsNullOrWhiteSpace(declaredPath)
            || declaredPath[0] == '/'
            || Path.IsPathRooted(declaredPath)
            || declaredPath.Contains('$', StringComparison.Ordinal)
            || declaredPath.Contains('\0'))
        {
            return (Mql5IncludeResolution.Invalid, null);
        }

        string? normalized = TryCombineRelative(sourcePath, declaredPath, local);
        if (normalized is null)
        {
            return (Mql5IncludeResolution.Invalid, null);
        }

        if (corpusPathIndex.TryGetValue(normalized, out string[]? matches))
        {
            return matches.Length == 1
                ? (Mql5IncludeResolution.ResolvedInCorpus, matches[0])
                : (Mql5IncludeResolution.Ambiguous, null);
        }

        string[] suffixMatches = corpusPathIndex.Keys
            .Where(path => path.EndsWith('/' + declaredPath, StringComparison.OrdinalIgnoreCase)
                || path.Equals(declaredPath, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (suffixMatches.Length == 1)
        {
            return (Mql5IncludeResolution.ResolvedInCorpus, suffixMatches[0]);
        }

        if (suffixMatches.Length > 1)
        {
            return (Mql5IncludeResolution.Ambiguous, null);
        }

        if (!local && IsKnownPlatformInclude(declaredPath))
        {
            return (Mql5IncludeResolution.PlatformLibrary, null);
        }

        return (Mql5IncludeResolution.MissingSource, null);
    }

    private static string? TryCombineRelative(string sourcePath, string includePath, bool local)
    {
        var segments = new List<string>();
        if (local)
        {
            int separator = sourcePath.LastIndexOf('/');
            if (separator >= 0)
            {
                segments.AddRange(sourcePath[..separator].Split('/'));
            }
        }

        foreach (string segment in includePath.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == ".")
            {
                continue;
            }

            if (segment == "..")
            {
                if (segments.Count == 0)
                {
                    return null;
                }

                segments.RemoveAt(segments.Count - 1);
                continue;
            }

            segments.Add(segment);
        }

        return segments.Count == 0 ? null : string.Join('/', segments);
    }

    private static bool IsKnownPlatformInclude(string path)
    {
        string normalized = path.Replace('\\', '/');
        string firstSegment = normalized.Split('/', 2)[0];
        return firstSegment.Equals("Trade", StringComparison.OrdinalIgnoreCase)
            || firstSegment.Equals("Indicators", StringComparison.OrdinalIgnoreCase)
            || firstSegment.Equals("Expert", StringComparison.OrdinalIgnoreCase)
            || firstSegment.Equals("Math", StringComparison.OrdinalIgnoreCase)
            || firstSegment.Equals("Arrays", StringComparison.OrdinalIgnoreCase)
            || firstSegment.Equals("Controls", StringComparison.OrdinalIgnoreCase)
            || firstSegment.Equals("Graphics", StringComparison.OrdinalIgnoreCase)
            || firstSegment.Equals("Canvas", StringComparison.OrdinalIgnoreCase)
            || firstSegment.Equals("Charts", StringComparison.OrdinalIgnoreCase)
            || firstSegment.Equals("Files", StringComparison.OrdinalIgnoreCase)
            || firstSegment.Equals("Generic", StringComparison.OrdinalIgnoreCase)
            || firstSegment.Equals("Strings", StringComparison.OrdinalIgnoreCase)
            || firstSegment.Equals("Tools", StringComparison.OrdinalIgnoreCase)
            || firstSegment.Equals("Object.mqh", StringComparison.OrdinalIgnoreCase)
            || firstSegment.Equals("stdlib.mqh", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsOperatingSystemInclude(string path)
    {
        return path.Contains("WinUser", StringComparison.OrdinalIgnoreCase)
            || path.Contains("kernel32", StringComparison.OrdinalIgnoreCase)
            || path.Contains("shell32", StringComparison.OrdinalIgnoreCase);
    }

    private static List<Mql5DetectedFeature> ConsolidateFeatures(
        IEnumerable<Mql5DetectedFeature> features)
    {
        return features
            .GroupBy(static feature => (feature.Code, feature.Support))
            .Select(static group => new Mql5DetectedFeature(
                group.Key.Code,
                group.Key.Support,
                group.Sum(static feature => feature.OccurrenceCount),
                group.SelectMany(static feature => feature.Lines)
                    .Distinct()
                    .Order()
                    .Take(MaximumReportedLinesPerFeature)
                    .ToArray()))
            .ToList();
    }

    private static void AddFeatureFindings(
        IReadOnlyCollection<Mql5DetectedFeature> features,
        List<Mql5CompatibilityFinding> findings)
    {
        AddFindingForFeature(
            features,
            findings,
            "CUSTOM_INDICATOR",
            "CUSTOM_INDICATOR_SOURCE_AND_MAPPING_REQUIRED",
            Mql5FindingSeverity.Error,
            "Custom indicator use requires exact source dependency resolution and a reviewed value-semantics mapping.");
        AddFindingForFeature(
            features,
            findings,
            "FILE_IO",
            "ARBITRARY_FILE_IO_UNSUPPORTED",
            Mql5FindingSeverity.Error,
            "Arbitrary terminal file access cannot be expressed by the restricted strategy runtime.");
        AddFindingForFeature(
            features,
            findings,
            "NETWORK_IO",
            "NETWORK_ACCESS_UNSUPPORTED",
            Mql5FindingSeverity.Error,
            "WebRequest, sockets, and external network access are prohibited in converted strategies.");
        AddFindingForFeature(
            features,
            findings,
            "OPENCL",
            "OPENCL_UNSUPPORTED",
            Mql5FindingSeverity.Error,
            "OpenCL execution is outside the restricted deterministic runtime.");
        AddFindingForFeature(
            features,
            findings,
            "CHART_OR_OBJECT_UI",
            "CHART_UI_UNSUPPORTED",
            Mql5FindingSeverity.Error,
            "Chart objects and terminal UI behavior are outside the strategy runtime.");
        AddFindingForFeature(
            features,
            findings,
            "TERMINAL_STATE",
            "TERMINAL_STATE_UNSUPPORTED",
            Mql5FindingSeverity.Error,
            "Terminal-specific state cannot be relied on by the cloud strategy runtime.");
        AddFindingForFeature(
            features,
            findings,
            "PERSISTED_TERMINAL_GLOBALS",
            "TERMINAL_GLOBAL_VARIABLES_UNSUPPORTED",
            Mql5FindingSeverity.Error,
            "Persisted terminal globals require an explicit bounded state-store redesign.");
        AddFindingForFeature(
            features,
            findings,
            "TRADE_ORDER_SEND",
            "TRADE_RESULT_CONTROL_FLOW_REVIEW_REQUIRED",
            Mql5FindingSeverity.Warning,
            "OrderSend must be lowered to an asynchronous trade intent; immediate result-dependent control flow is not proven safe.");
        AddFindingForFeature(
            features,
            findings,
            "TRADE_CTRADE",
            "TRADE_RESULT_CONTROL_FLOW_REVIEW_REQUIRED",
            Mql5FindingSeverity.Warning,
            "CTrade calls require intent mapping and must not depend on immediate tickets, retcodes, or resulting positions.");
        AddFindingForFeature(
            features,
            findings,
            "TIME_OR_SESSION_DEPENDENCY",
            "TIMEZONE_AND_SESSION_MAPPING_REQUIRED",
            Mql5FindingSeverity.Warning,
            "Terminal/server/local time use requires an explicit timezone and market-session mapping.");
        AddFindingForFeature(
            features,
            findings,
            "POSITION_MODE_DEPENDENCY",
            "HEDGING_NETTING_ASSUMPTION_REVIEW_REQUIRED",
            Mql5FindingSeverity.Warning,
            "Position behavior requires an explicit hedging/netting account-mode contract.");
        AddFindingForFeature(
            features,
            findings,
            "HISTORY_OR_BARS_DEPENDENCY",
            "HISTORICAL_DATA_REQUIREMENT_REVIEW_REQUIRED",
            Mql5FindingSeverity.Warning,
            "Required bars, indicator warm-up, and historical dataset identity are not established by lexical inspection.");
        AddFindingForFeature(
            features,
            findings,
            "UNBOUNDED_LOOP_SHAPE",
            "UNBOUNDED_CONTROL_FLOW_REQUIRES_PROOF",
            Mql5FindingSeverity.Warning,
            "An apparently unbounded loop requires parser-level termination and event-budget verification.");
        AddFindingForFeature(
            features,
            findings,
            "BROKER_SYMBOL_LITERAL",
            "BROKER_SYMBOL_MAPPING_REVIEW_REQUIRED",
            Mql5FindingSeverity.Warning,
            "A literal symbol dependency requires an explicit broker symbol/suffix mapping.");
        AddFindingForFeature(
            features,
            findings,
            "RESOURCE_DEPENDENCY",
            "RESOURCE_DEPENDENCY_REVIEW_REQUIRED",
            Mql5FindingSeverity.Warning,
            "Bundled resources require source, type, rights, and runtime-capability review.");
    }

    private static void AddFindingForFeature(
        IEnumerable<Mql5DetectedFeature> features,
        List<Mql5CompatibilityFinding> findings,
        string featureCode,
        string findingCode,
        Mql5FindingSeverity severity,
        string message)
    {
        Mql5DetectedFeature? feature = features.FirstOrDefault(
            item => item.Code.Equals(featureCode, StringComparison.Ordinal));
        if (feature is null)
        {
            return;
        }

        findings.Add(new Mql5CompatibilityFinding(
            findingCode,
            severity,
            feature.Support,
            message,
            feature.Lines));
    }

    private static void AddProgramShapeFindings(
        string sourcePath,
        string[] entrypoints,
        List<Mql5CompatibilityFinding> findings)
    {
        if (!Path.GetExtension(sourcePath).Equals(".mq5", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (entrypoints.Length == 0)
        {
            findings.Add(new Mql5CompatibilityFinding(
                "PROGRAM_ENTRYPOINT_NOT_IDENTIFIED",
                Mql5FindingSeverity.Warning,
                Mql5FeatureSupport.ReviewRequired,
                "No recognized MQL5 program event handler was identified lexically.",
                []));
        }

        if (entrypoints.Contains("OnCalculate", StringComparer.Ordinal))
        {
            findings.Add(new Mql5CompatibilityFinding(
                "CUSTOM_INDICATOR_PROGRAM_UNSUPPORTED",
                Mql5FindingSeverity.Error,
                Mql5FeatureSupport.Unsupported,
                "The file appears to be a custom indicator, not an initial-subset expert strategy.",
                []));
        }

        if (entrypoints.Contains("OnStart", StringComparer.Ordinal))
        {
            findings.Add(new Mql5CompatibilityFinding(
                "SCRIPT_PROGRAM_UNSUPPORTED",
                Mql5FindingSeverity.Error,
                Mql5FeatureSupport.Unsupported,
                "The file appears to be an MQL5 script rather than an event-driven expert strategy.",
                []));
        }
    }

    private static Mql5StaticDisposition DetermineDisposition(
        IEnumerable<Mql5CompatibilityFinding> findings)
    {
        Mql5FeatureSupport[] support = findings.Select(static finding => finding.Support).ToArray();
        if (support.Contains(Mql5FeatureSupport.Unsupported))
        {
            return Mql5StaticDisposition.Unsupported;
        }

        if (support.Contains(Mql5FeatureSupport.NeedsSource))
        {
            return Mql5StaticDisposition.NeedsSource;
        }

        return Mql5StaticDisposition.NeedsSemanticValidation;
    }

    private static string ComputeCorpusDigest(IEnumerable<Mql5SourceManifest> files)
    {
        var content = new StringBuilder();
        foreach (Mql5SourceManifest file in files)
        {
            content.Append(file.RelativePath)
                .Append('\0')
                .Append(file.Sha256)
                .Append('\n');
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content.ToString())))
            .ToLowerInvariant();
    }

    private static int[] BuildLineStarts(string source)
    {
        var starts = new List<int> { 0 };
        for (int index = 0; index < source.Length; index++)
        {
            if (source[index] == '\n' && index + 1 < source.Length)
            {
                starts.Add(index + 1);
            }
        }

        return starts.ToArray();
    }

    private static int[] GetReportedLines(int[] lineStarts, IEnumerable<Match> matches)
    {
        return matches
            .Select(match => GetLineNumber(lineStarts, match.Index))
            .Distinct()
            .Take(MaximumReportedLinesPerFeature)
            .ToArray();
    }

    private static int GetLineNumber(int[] lineStarts, int index)
    {
        int position = Array.BinarySearch(lineStarts, index);
        return position >= 0 ? position + 1 : ~position;
    }

    private static string ToUpperSnakeCase(string value)
    {
        return UpperSnakeBoundaryRegex().Replace(value, "$1_$2").ToUpperInvariant();
    }

    private static DecodedSource Decode(byte[] content)
    {
        Mql5DecodedSource decoded = Mql5SourceDecoder.Decode(content);
        return new DecodedSource(
            decoded.Text,
            decoded.EncodingName,
            decoded.UsedFallbackEncoding,
            decoded.ContentKind,
            decoded.ForbiddenControlCharacterCount);
    }

    private static MaskedSource MaskSource(string source)
    {
        char[] codeOnly = source.ToCharArray();
        char[] commentsRemoved = source.ToCharArray();
        LexicalState state = LexicalState.Code;
        bool escaped = false;

        for (int index = 0; index < source.Length; index++)
        {
            char current = source[index];
            char next = index + 1 < source.Length ? source[index + 1] : '\0';

            switch (state)
            {
                case LexicalState.Code when current == '/' && next == '/':
                    Mask(codeOnly, index);
                    Mask(commentsRemoved, index);
                    state = LexicalState.LineComment;
                    break;
                case LexicalState.Code when current == '/' && next == '*':
                    Mask(codeOnly, index);
                    Mask(commentsRemoved, index);
                    state = LexicalState.BlockComment;
                    break;
                case LexicalState.Code when current == '"':
                    Mask(codeOnly, index);
                    state = LexicalState.String;
                    escaped = false;
                    break;
                case LexicalState.Code when current == '\'':
                    Mask(codeOnly, index);
                    state = LexicalState.Character;
                    escaped = false;
                    break;
                case LexicalState.LineComment:
                    Mask(codeOnly, index);
                    Mask(commentsRemoved, index);
                    if (current == '\n')
                    {
                        state = LexicalState.Code;
                    }

                    break;
                case LexicalState.BlockComment:
                    Mask(codeOnly, index);
                    Mask(commentsRemoved, index);
                    if (current == '*' && next == '/')
                    {
                        if (index + 1 < source.Length)
                        {
                            Mask(codeOnly, index + 1);
                            Mask(commentsRemoved, index + 1);
                        }

                        index++;
                        state = LexicalState.Code;
                    }

                    break;
                case LexicalState.String:
                    Mask(codeOnly, index);
                    if (current == '"' && !escaped)
                    {
                        state = LexicalState.Code;
                    }

                    escaped = current == '\\' && !escaped;
                    if (current != '\\')
                    {
                        escaped = false;
                    }

                    break;
                case LexicalState.Character:
                    Mask(codeOnly, index);
                    if (current == '\'' && !escaped)
                    {
                        state = LexicalState.Code;
                    }

                    escaped = current == '\\' && !escaped;
                    if (current != '\\')
                    {
                        escaped = false;
                    }

                    break;
            }
        }

        return new MaskedSource(
            new string(codeOnly),
            new string(commentsRemoved),
            state == LexicalState.BlockComment,
            state is LexicalState.String or LexicalState.Character);
    }

    private static void Mask(char[] target, int index)
    {
        if (target[index] is not ('\r' or '\n'))
        {
            target[index] = ' ';
        }
    }

    private enum LexicalState
    {
        Code,
        LineComment,
        BlockComment,
        String,
        Character
    }

    private sealed record DecodedSource(
        string Text,
        string EncodingName,
        bool UsedFallbackEncoding,
        Mql5SourceContentKind ContentKind,
        int ForbiddenControlCharacterCount);

    private sealed record MaskedSource(
        string CodeOnly,
        string CommentsRemoved,
        bool UnterminatedBlockComment,
        bool UnterminatedLiteral);

    private sealed record FeatureRule(string Code, Mql5FeatureSupport Support, Regex Regex);

    private sealed record EventRule(string Name, Mql5FeatureSupport Support);

    [GeneratedRegex(@"(?m)^[\t ]*(?:input|sinput)\b", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture)]
    private static partial Regex InputDeclarationRegex();

    [GeneratedRegex(@"\b(?:OrderSend|OrderSendAsync)\s*\(", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture)]
    private static partial Regex OrderSendRegex();

    [GeneratedRegex(@"\bCTrade\b", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture)]
    private static partial Regex CTradeRegex();

    [GeneratedRegex(@"\b(?:Position|Positions|Order|Orders|History)(?:Get|Select|Total|Ticket|Deal|Value|Type|Info)\w*\s*\(", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture)]
    private static partial Regex TradeStateReadRegex();

    [GeneratedRegex(@"\biCustom\s*\(", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture)]
    private static partial Regex CustomIndicatorRegex();

    [GeneratedRegex(@"\bFile(?:Open|Read|Write|Seek|Tell|Size|Flush|Close|Delete|Move|Copy|IsExist|FindFirst|FindNext|FindClose)\w*\s*\(", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture)]
    private static partial Regex FileIoRegex();

    [GeneratedRegex(@"\b(?:WebRequest|SocketCreate|SocketConnect|SocketSend|SocketRead|SocketTlsHandshake|SocketClose)\s*\(", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture)]
    private static partial Regex NetworkIoRegex();

    [GeneratedRegex(@"\bCL(?:Context|Buffer|Program|Kernel|Execute|SetKernelArg|GetDeviceInfo|Release)\w*\s*\(", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture)]
    private static partial Regex OpenClRegex();

    [GeneratedRegex(@"\b(?:Chart|Object|Objects|EventChartCustom)(?:ID|Open|Close|ApplyTemplate|SaveTemplate|WindowFind|Redraw|Navigate|Set|Get|Create|Delete|Find|Move|SetInteger|SetDouble|SetString|GetInteger|GetDouble|GetString|Total|DeleteAll)?\w*\s*\(", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture)]
    private static partial Regex ChartUiRegex();

    [GeneratedRegex(@"\b(?:TerminalInfoInteger|TerminalInfoDouble|TerminalInfoString|MQLInfoInteger|MQLInfoString)\s*\(", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture)]
    private static partial Regex TerminalStateRegex();

    [GeneratedRegex(@"\bGlobalVariable(?:Check|Time|Del|Get|Name|Set|SetOnCondition|Temp|Total|DeleteAll)?\s*\(", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture)]
    private static partial Regex TerminalGlobalsRegex();

    [GeneratedRegex(@"\b(?:TimeCurrent|TimeTradeServer|TimeLocal|TimeGMT|TimeGMTOffset|TimeDaylightSavings)\s*\(", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture)]
    private static partial Regex TimeDependencyRegex();

    [GeneratedRegex(@"\b(?:SymbolInfoInteger|SymbolInfoDouble|SymbolInfoString|SymbolInfoTick|Symbol|_Point|_Digits)\b", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture)]
    private static partial Regex SymbolSpecificationRegex();

    [GeneratedRegex(@"\b(?:ACCOUNT_MARGIN_MODE\w*|PositionSelectByTicket|PositionGetTicket|PositionsTotal)\b", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture)]
    private static partial Regex PositionModeRegex();

    [GeneratedRegex(@"\b(?:CopyRates|CopyTicks|CopyTicksRange|CopyBuffer|Bars|BarsCalculated|HistorySelect|HistorySelectByPosition)\s*\(", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture)]
    private static partial Regex HistoryDependencyRegex();

    [GeneratedRegex(@"\b(?:GetLastError|ResetLastError|ResultRetcode|ResultOrder|ResultDeal|ResultRetcodeDescription)\s*\(", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture)]
    private static partial Regex ErrorHandlingRegex();

    [GeneratedRegex(@"\b(?:EventSetTimer|EventSetMillisecondTimer|EventKillTimer)\s*\(", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture)]
    private static partial Regex TimerApiRegex();

    [GeneratedRegex(@"\b(?:while\s*\(\s*(?:true|1)\s*\)|for\s*\(\s*;\s*;\s*\))", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture)]
    private static partial Regex UnboundedLoopRegex();

    [GeneratedRegex(@"\b(?:for|while|do)\b", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture)]
    private static partial Regex AnyLoopRegex();

    [GeneratedRegex("\\b(?:SymbolSelect|SymbolInfoInteger|SymbolInfoDouble|SymbolInfoString|i[A-Z][A-Za-z0-9_]*)\\s*\\(\\s*\"[^\"\\r\\n]+\"", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture)]
    private static partial Regex BrokerSymbolLiteralRegex();

    [GeneratedRegex(@"(?m)^[\t ]*#resource\b", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture)]
    private static partial Regex ResourceDirectiveRegex();

    [GeneratedRegex("(?m)^[\\t ]*#include[\\t ]*(?<open>[<\\\"])(?<path>[^>\\\"\\r\\n]+)[>\\\"]", RegexOptions.CultureInvariant)]
    private static partial Regex IncludeDirectiveRegex();

    [GeneratedRegex("(?m)^[\\t ]*#import[\\t ]*\\\"(?<path>[^\\\"\\r\\n]+)\\\"", RegexOptions.CultureInvariant)]
    private static partial Regex ImportDirectiveRegex();

    [GeneratedRegex("([a-z0-9])([A-Z])", RegexOptions.CultureInvariant)]
    private static partial Regex UpperSnakeBoundaryRegex();
}
