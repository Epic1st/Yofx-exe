using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace YO4X.StrategyGovernance;

public sealed class Mql5ConversionEvidenceAnalyzer
{
    public const string SchemaVersion = "mql5-conversion-evidence.v1";
    public const string AnalyzerVersion = "yo4x-mql5-conversion-evidence.v1";

    private const int MaximumTokensPerFile = 2_000_000;
    private const int MaximumFindingsPerFile = 256;

    private static readonly HashSet<string> ControlFlowNames = new(
        ["if", "for", "while", "switch", "catch"],
        StringComparer.Ordinal);

    private readonly IMql5StaticInventoryAnalyzer staticAnalyzer;

    public Mql5ConversionEvidenceAnalyzer()
        : this(new Mql5StaticInventoryAnalyzer())
    {
    }

    public Mql5ConversionEvidenceAnalyzer(IMql5StaticInventoryAnalyzer staticAnalyzer)
    {
        this.staticAnalyzer = staticAnalyzer
            ?? throw new ArgumentNullException(nameof(staticAnalyzer));
    }

    public Mql5ConversionCorpusEvidence Analyze(
        IEnumerable<Mql5SourceDocument> sourceDocuments)
    {
        ArgumentNullException.ThrowIfNull(sourceDocuments);

        var snapshots = new List<Mql5SourceDocument>();
        try
        {
            foreach (Mql5SourceDocument document in sourceDocuments)
            {
                ArgumentNullException.ThrowIfNull(document);
                ArgumentNullException.ThrowIfNull(document.Content);
                snapshots.Add(new Mql5SourceDocument(
                    NormalizeRelativePath(document.RelativePath),
                    document.Content.ToArray()));
            }

            Mql5SourceDocument[] documents = snapshots
                .OrderBy(static document => document.RelativePath, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static document => document.RelativePath, StringComparer.Ordinal)
                .ToArray();
            EnsureUniquePaths(documents);

            Mql5CorpusManifest staticManifest = staticAnalyzer.Analyze(documents);
            var documentsByPath = documents.ToDictionary(
                static document => document.RelativePath,
                StringComparer.OrdinalIgnoreCase);
            var staticByPath = staticManifest.Files.ToDictionary(
                static file => file.RelativePath,
                StringComparer.OrdinalIgnoreCase);

            Dictionary<string, string[]> adjacency = BuildAdjacency(staticManifest.Files);
            HashSet<string> cycleMembers = FindCycleMembers(adjacency);
            string dependencyGraphSha256 = ComputeDependencyGraphDigest(staticManifest.Files);

            var syntaxByPath = new Dictionary<string, SyntaxAnalysis>(StringComparer.OrdinalIgnoreCase);
            foreach (Mql5SourceManifest file in staticManifest.Files)
            {
                syntaxByPath.Add(
                    file.RelativePath,
                    AnalyzeSyntax(documentsByPath[file.RelativePath].Content));
            }

            var evidenceFiles = new List<Mql5ConversionFileEvidence>(staticManifest.FileCount);
            foreach (Mql5SourceManifest file in staticManifest.Files)
            {
                Mql5DependencyClosureEvidence closure = BuildDependencyClosure(
                    file.RelativePath,
                    adjacency,
                    cycleMembers);
                SyntaxAnalysis syntax = syntaxByPath[file.RelativePath];
                Mql5ConversionFileEvidence evidence = BuildFileEvidence(
                    file,
                    closure,
                    staticByPath,
                    syntaxByPath,
                    syntax);
                evidenceFiles.Add(evidence with
                {
                    EvidenceSha256 = ComputeFileEvidenceDigest(evidence)
                });
            }

            string evidenceSha256 = ComputeCorpusEvidenceDigest(
                staticManifest,
                dependencyGraphSha256,
                evidenceFiles);
            return new Mql5ConversionCorpusEvidence(
                SchemaVersion,
                AnalyzerVersion,
                staticManifest.SchemaVersion,
                staticManifest.AnalyzerVersion,
                staticManifest.CorpusSha256,
                dependencyGraphSha256,
                evidenceSha256,
                staticManifest.FileCount,
                staticManifest.TotalBytes,
                evidenceFiles);
        }
        finally
        {
            foreach (Mql5SourceDocument snapshot in snapshots)
            {
                CryptographicOperations.ZeroMemory(snapshot.Content);
            }
        }
    }

    private static Mql5ConversionFileEvidence BuildFileEvidence(
        Mql5SourceManifest file,
        Mql5DependencyClosureEvidence closure,
        Dictionary<string, Mql5SourceManifest> staticByPath,
        Dictionary<string, SyntaxAnalysis> syntaxByPath,
        SyntaxAnalysis syntax)
    {
        string[] affectedPaths = closure.TransitiveDependencies
            .Prepend(file.RelativePath)
            .ToArray();
        bool hasAllNulSource = affectedPaths.Any(path => string.Equals(
            staticByPath[path].TextEncoding,
            "binary-all-nul",
            StringComparison.Ordinal));
        bool hasBinarySource = affectedPaths.Any(path => string.Equals(
            staticByPath[path].TextEncoding,
            "binary-non-text",
            StringComparison.Ordinal));
        bool hasInvalidSyntax = affectedPaths.Any(path => !syntaxByPath[path].SyntaxPassed);
        bool hasMissingDependency = affectedPaths.Any(path =>
            staticByPath[path].Disposition is Mql5StaticDisposition.NeedsSource
                or Mql5StaticDisposition.Rejected
            || staticByPath[path].Includes.Any(include => include.Resolution is
                Mql5IncludeResolution.MissingSource
                or Mql5IncludeResolution.Ambiguous
                or Mql5IncludeResolution.Invalid));
        bool hasExternalPlatformDependency = affectedPaths.Any(path =>
            staticByPath[path].Includes.Any(include =>
                include.Resolution == Mql5IncludeResolution.PlatformLibrary));
        bool hasDependencyCycle = closure.ReachableCycleMembers.Count > 0;
        bool hasUnsupportedSemantics = affectedPaths.Any(path =>
            staticByPath[path].Disposition == Mql5StaticDisposition.Unsupported);

        Mql5ConversionEvidenceDisposition disposition = hasAllNulSource
            ? Mql5ConversionEvidenceDisposition.BlockedAllNulSource
            : hasBinarySource
                ? Mql5ConversionEvidenceDisposition.BlockedBinarySource
                : hasInvalidSyntax
                    ? Mql5ConversionEvidenceDisposition.BlockedInvalidSyntax
                    : hasMissingDependency
                        ? Mql5ConversionEvidenceDisposition.BlockedMissingDependency
                        : hasDependencyCycle
                            ? Mql5ConversionEvidenceDisposition.BlockedDependencyCycle
                            : hasUnsupportedSemantics
                                ? Mql5ConversionEvidenceDisposition.BlockedUnsupportedSemantics
                                : hasExternalPlatformDependency
                                    ? Mql5ConversionEvidenceDisposition.BlockedExternalDependencySnapshot
                                    : Mql5ConversionEvidenceDisposition.AwaitingIsolatedTypeCheck;

        var findings = new List<Mql5ConversionEvidenceFinding>(syntax.Findings);
        AddDependencyFindings(
            file.RelativePath,
            closure,
            staticByPath,
            syntaxByPath,
            findings);
        findings.Add(new Mql5ConversionEvidenceFinding(
            "FULL_MQL5_GRAMMAR_PARSE_NOT_PERFORMED",
            Mql5FindingSeverity.Information,
            "Only deterministic lexical and delimiter/preprocessor structure analysis was performed; a complete MQL5 grammar parse is not claimed.",
            null));
        findings.Add(new Mql5ConversionEvidenceFinding(
            "MQL5_TYPE_CHECK_NOT_PERFORMED",
            Mql5FindingSeverity.Information,
            "No isolated, identified MQL5 type checker has accepted this dependency closure.",
            null));
        findings.Add(new Mql5ConversionEvidenceFinding(
            "RESTRICTED_IR_LOWERING_NOT_PERFORMED",
            Mql5FindingSeverity.Information,
            "No source construct has been lowered into restricted runtime IR, so semantic conversion is not claimed.",
            null));

        Mql5ConversionEvidenceFinding[] orderedFindings = findings
            .DistinctBy(static finding => (
                finding.Code,
                finding.Message,
                finding.Location?.Line,
                finding.Location?.Column))
            .OrderByDescending(static finding => finding.Severity)
            .ThenBy(static finding => finding.Code, StringComparer.Ordinal)
            .ThenBy(static finding => finding.Location?.Line ?? 0)
            .ThenBy(static finding => finding.Location?.Column ?? 0)
            .ToArray();

        Mql5EvidenceStageStatus dependencyStatus = hasMissingDependency || hasDependencyCycle
            ? Mql5EvidenceStageStatus.Failed
            : hasExternalPlatformDependency
                ? Mql5EvidenceStageStatus.Blocked
                : Mql5EvidenceStageStatus.Passed;
        string dependencyCode = hasMissingDependency
            ? "DEPENDENCY_SOURCE_INCOMPLETE"
            : hasDependencyCycle
                ? "DEPENDENCY_CYCLE_PRESENT"
                : hasExternalPlatformDependency
                    ? "PLATFORM_LIBRARY_SNAPSHOT_REQUIRED"
                    : "CORPUS_DEPENDENCIES_RESOLVED";
        bool prerequisitesPassed = !hasInvalidSyntax
            && !hasMissingDependency
            && !hasDependencyCycle
            && !hasExternalPlatformDependency
            && !hasUnsupportedSemantics;

        Mql5EvidenceStage[] stages =
        [
            new(
                Mql5EvidenceStageName.SourceIntegrity,
                Mql5EvidenceStageStatus.Passed,
                "SOURCE_HASH_BOUND_TO_STATIC_MANIFEST"),
            new(
                Mql5EvidenceStageName.DependencyResolution,
                dependencyStatus,
                dependencyCode),
            new(
                Mql5EvidenceStageName.LexicalAnalysis,
                syntax.LexicalPassed
                    ? Mql5EvidenceStageStatus.Passed
                    : Mql5EvidenceStageStatus.Failed,
                syntax.LexicalPassed
                    ? "DETERMINISTIC_TOKENIZATION_PASSED"
                    : "LEXICAL_ANALYSIS_FAILED"),
            new(
                Mql5EvidenceStageName.StructuralParse,
                syntax.StructuralPassed
                    ? Mql5EvidenceStageStatus.Passed
                    : Mql5EvidenceStageStatus.Failed,
                syntax.StructuralPassed
                    ? "DELIMITER_AND_PREPROCESSOR_STRUCTURE_PASSED"
                    : "STRUCTURAL_PARSE_FAILED"),
            new(
                Mql5EvidenceStageName.TypeChecking,
                prerequisitesPassed
                    ? Mql5EvidenceStageStatus.NotAttempted
                    : Mql5EvidenceStageStatus.Blocked,
                prerequisitesPassed
                    ? "ISOLATED_MQL5_TYPECHECKER_NOT_RUN"
                    : "TYPECHECK_PREREQUISITES_NOT_SATISFIED"),
            new(
                Mql5EvidenceStageName.RestrictedIrLowering,
                Mql5EvidenceStageStatus.Blocked,
                "VERIFIED_TYPECHECK_AND_SEMANTIC_MAPPING_REQUIRED")
        ];

        return new Mql5ConversionFileEvidence(
            file.RelativePath,
            file.Sha256,
            ComputeDependencyClosureDigest(file.RelativePath, closure, staticByPath),
            string.Empty,
            file.TextEncoding,
            file.Kind,
            file.Disposition,
            disposition,
            file.Entrypoints,
            file.Features,
            file.Findings,
            file.Includes.Select(static include => new Mql5DependencyEdgeEvidence(
                    include.DeclaredPath,
                    include.Kind,
                    include.Resolution,
                    include.ResolvedRelativePath,
                    include.Line))
                .ToArray(),
            closure,
            syntax.Lexical,
            syntax.Structural,
            stages,
            orderedFindings);
    }

    private static void AddDependencyFindings(
        string sourcePath,
        Mql5DependencyClosureEvidence closure,
        Dictionary<string, Mql5SourceManifest> staticByPath,
        Dictionary<string, SyntaxAnalysis> syntaxByPath,
        List<Mql5ConversionEvidenceFinding> findings)
    {
        if (closure.ReachableCycleMembers.Count > 0)
        {
            findings.Add(new Mql5ConversionEvidenceFinding(
                "DEPENDENCY_CYCLE_BLOCKS_ORDERING",
                Mql5FindingSeverity.Error,
                "At least one local include cycle is reachable, so a unique dependency-first processing order is not proven.",
                null));
        }

        string[] transitivePaths = closure.TransitiveDependencies
            .Where(path => !path.Equals(sourcePath, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (transitivePaths.Any(path => !syntaxByPath[path].SyntaxPassed))
        {
            findings.Add(new Mql5ConversionEvidenceFinding(
                "DEPENDENCY_CLOSURE_HAS_INVALID_STRUCTURE",
                Mql5FindingSeverity.Error,
                "A resolved local dependency has lexical or structural errors.",
                null));
        }

        if (transitivePaths.Any(path => staticByPath[path].Disposition == Mql5StaticDisposition.NeedsSource))
        {
            findings.Add(new Mql5ConversionEvidenceFinding(
                "DEPENDENCY_CLOSURE_HAS_MISSING_SOURCE",
                Mql5FindingSeverity.Error,
                "A resolved local dependency has an incomplete source closure.",
                null));
        }

        if (transitivePaths.Any(path => staticByPath[path].Disposition == Mql5StaticDisposition.Unsupported))
        {
            findings.Add(new Mql5ConversionEvidenceFinding(
                "DEPENDENCY_CLOSURE_HAS_UNSUPPORTED_SEMANTICS",
                Mql5FindingSeverity.Error,
                "A resolved local dependency contains semantics outside the restricted conversion subset.",
                null));
        }

        bool platformDependency = closure.TransitiveDependencies
            .Prepend(sourcePath)
            .Any(path => staticByPath[path].Includes.Any(include =>
                include.Resolution == Mql5IncludeResolution.PlatformLibrary));
        if (platformDependency)
        {
            findings.Add(new Mql5ConversionEvidenceFinding(
                "PLATFORM_LIBRARY_SNAPSHOT_REQUIRED",
                Mql5FindingSeverity.Warning,
                "A platform/search-path include requires a version-bound MQL5 library snapshot before type checking.",
                null));
        }
    }

    private static SyntaxAnalysis AnalyzeSyntax(byte[] content)
    {
        string source = Decode(content);
        var collector = new FindingCollector();
        LexicalScan scan = Tokenize(source, collector);
        StructuralScan structural = AnalyzeStructure(source, scan.Tokens, collector);
        bool lexicalPassed = !collector.Findings.Any(finding =>
            finding.Severity == Mql5FindingSeverity.Error
            && finding.Code.StartsWith("LEXICAL_", StringComparison.Ordinal));
        bool structuralPassed = lexicalPassed
            && !collector.Findings.Any(finding =>
                finding.Severity == Mql5FindingSeverity.Error
                && (finding.Code.StartsWith("DELIMITER_", StringComparison.Ordinal)
                    || finding.Code.StartsWith("PREPROCESSOR_", StringComparison.Ordinal)));

        return new SyntaxAnalysis(
            new Mql5LexicalEvidence(
                scan.Tokens.Count,
                scan.IdentifierCount,
                scan.NumericLiteralCount,
                scan.StringLiteralCount,
                scan.CharacterLiteralCount,
                scan.CommentCount,
                scan.NulCharacterCount,
                scan.ForbiddenControlCharacterCount,
                scan.PreprocessorDirectiveCount,
                structural.MaximumDelimiterDepth),
            new Mql5StructuralEvidence(
                structural.FunctionDefinitionCount,
                structural.TypeDeclarationCount,
                structural.InputDeclarationCount,
                structural.StatementTerminatorCount,
                structural.MacroDefinitionCount,
                structural.ConditionalDirectiveCount,
                structural.DelimitersBalanced,
                structural.ConditionalDirectivesBalanced,
                FullGrammarParseProven: false,
                TypeCheckProven: false,
                RestrictedIrLoweringProven: false),
            collector.ToArray(),
            lexicalPassed,
            structuralPassed);
    }

    private static LexicalScan Tokenize(string source, FindingCollector findings)
    {
        var tokens = new List<SyntaxToken>(Math.Min(source.Length / 3, 256_000));
        int identifierCount = 0;
        int numericLiteralCount = 0;
        int stringLiteralCount = 0;
        int characterLiteralCount = 0;
        int commentCount = 0;
        ControlCharacterScan controlCharacters = AnalyzeControlCharacters(source, findings);
        int preprocessorDirectiveCount = 0;
        int index = 0;
        int line = 1;
        int column = 1;
        bool onlyWhitespaceOnLine = true;
        bool directiveLine = false;
        bool tokenLimitReported = false;

        while (index < source.Length)
        {
            char current = source[index];
            char next = index + 1 < source.Length ? source[index + 1] : '\0';
            if (char.IsWhiteSpace(current))
            {
                if (current == '\n')
                {
                    int previous = index - 1;
                    if (previous >= 0 && source[previous] == '\r')
                    {
                        previous--;
                    }

                    bool continuation = directiveLine
                        && previous >= 0
                        && source[previous] == '\\';
                    index++;
                    line++;
                    column = 1;
                    onlyWhitespaceOnLine = true;
                    directiveLine = continuation;
                }
                else
                {
                    index++;
                    column++;
                }

                continue;
            }

            if (current == '/' && next == '/')
            {
                commentCount++;
                index += 2;
                column += 2;
                while (index < source.Length && source[index] != '\n')
                {
                    index++;
                    column++;
                }

                continue;
            }

            if (current == '/' && next == '*')
            {
                int startLine = line;
                int startColumn = column;
                commentCount++;
                index += 2;
                column += 2;
                bool terminated = false;
                while (index < source.Length)
                {
                    if (source[index] == '*' && index + 1 < source.Length && source[index + 1] == '/')
                    {
                        index += 2;
                        column += 2;
                        terminated = true;
                        break;
                    }

                    if (source[index] == '\n')
                    {
                        index++;
                        line++;
                        column = 1;
                        onlyWhitespaceOnLine = true;
                        directiveLine = false;
                    }
                    else
                    {
                        index++;
                        column++;
                    }
                }

                if (!terminated)
                {
                    findings.Add(
                        "LEXICAL_UNTERMINATED_BLOCK_COMMENT",
                        Mql5FindingSeverity.Error,
                        "A block comment reaches end-of-file without a closing delimiter.",
                        startLine,
                        startColumn);
                }

                continue;
            }

            int tokenStart = index;
            int tokenLine = line;
            int tokenColumn = column;
            bool tokenOnDirectiveLine = directiveLine;
            SyntaxTokenKind kind;

            if (current is '"' or '\'')
            {
                char quote = current;
                kind = quote == '"' ? SyntaxTokenKind.StringLiteral : SyntaxTokenKind.CharacterLiteral;
                if (kind == SyntaxTokenKind.StringLiteral)
                {
                    stringLiteralCount++;
                }
                else
                {
                    characterLiteralCount++;
                }

                index++;
                column++;
                bool escaped = false;
                bool terminated = false;
                while (index < source.Length)
                {
                    char literalCharacter = source[index];
                    if (literalCharacter == quote && !escaped)
                    {
                        index++;
                        column++;
                        terminated = true;
                        break;
                    }

                    if (literalCharacter == '\n')
                    {
                        index++;
                        line++;
                        column = 1;
                        onlyWhitespaceOnLine = true;
                        directiveLine = false;
                        escaped = false;
                        continue;
                    }

                    escaped = literalCharacter == '\\' && !escaped;
                    if (literalCharacter != '\\')
                    {
                        escaped = false;
                    }

                    index++;
                    column++;
                }

                if (!terminated)
                {
                    findings.Add(
                        kind == SyntaxTokenKind.StringLiteral
                            ? "LEXICAL_UNTERMINATED_STRING_LITERAL"
                            : "LEXICAL_UNTERMINATED_CHARACTER_LITERAL",
                        Mql5FindingSeverity.Error,
                        "A literal reaches end-of-file without a closing delimiter.",
                        tokenLine,
                        tokenColumn);
                }
            }
            else if (IsIdentifierStart(current))
            {
                kind = SyntaxTokenKind.Identifier;
                identifierCount++;
                index++;
                column++;
                while (index < source.Length && IsIdentifierPart(source[index]))
                {
                    index++;
                    column++;
                }
            }
            else if (char.IsDigit(current)
                || current == '.' && char.IsDigit(next))
            {
                kind = SyntaxTokenKind.NumericLiteral;
                numericLiteralCount++;
                index++;
                column++;
                while (index < source.Length
                    && (char.IsLetterOrDigit(source[index])
                        || source[index] is '.' or '_'))
                {
                    index++;
                    column++;
                }
            }
            else
            {
                kind = current == '#' && onlyWhitespaceOnLine
                    ? SyntaxTokenKind.PreprocessorStart
                    : SyntaxTokenKind.Punctuation;
                if (kind == SyntaxTokenKind.PreprocessorStart)
                {
                    preprocessorDirectiveCount++;
                    directiveLine = true;
                    tokenOnDirectiveLine = true;
                }

                index++;
                column++;
            }

            if (tokens.Count < MaximumTokensPerFile)
            {
                tokens.Add(new SyntaxToken(
                    kind,
                    tokenStart,
                    index - tokenStart,
                    tokenLine,
                    tokenColumn,
                    tokenOnDirectiveLine));
            }
            else if (!tokenLimitReported)
            {
                tokenLimitReported = true;
                findings.Add(
                    "LEXICAL_TOKEN_LIMIT_EXCEEDED",
                    Mql5FindingSeverity.Error,
                    "The file exceeds the deterministic token-analysis limit.",
                    tokenLine,
                    tokenColumn);
            }

            onlyWhitespaceOnLine = false;
        }

        return new LexicalScan(
            tokens,
            identifierCount,
            numericLiteralCount,
            stringLiteralCount,
            characterLiteralCount,
            commentCount,
            controlCharacters.NulCharacterCount,
            controlCharacters.ForbiddenControlCharacterCount,
            preprocessorDirectiveCount);
    }

    private static ControlCharacterScan AnalyzeControlCharacters(
        string source,
        FindingCollector findings)
    {
        int nulCount = 0;
        int forbiddenCount = 0;
        int firstNulLine = 0;
        int firstNulColumn = 0;
        int firstForbiddenLine = 0;
        int firstForbiddenColumn = 0;
        int line = 1;
        int column = 1;
        foreach (char character in source)
        {
            if (character == '\0')
            {
                nulCount++;
                if (firstNulLine == 0)
                {
                    firstNulLine = line;
                    firstNulColumn = column;
                }
            }
            else if (IsForbiddenControl(character))
            {
                forbiddenCount++;
                if (firstForbiddenLine == 0)
                {
                    firstForbiddenLine = line;
                    firstForbiddenColumn = column;
                }
            }

            if (character == '\n')
            {
                line++;
                column = 1;
            }
            else
            {
                column++;
            }
        }

        if (nulCount > 0)
        {
            findings.Add(
                "LEXICAL_NUL_CHARACTERS_PRESENT",
                Mql5FindingSeverity.Error,
                $"The decoded source contains {nulCount.ToString(CultureInfo.InvariantCulture)} NUL characters; one aggregate finding is emitted.",
                firstNulLine,
                firstNulColumn);
        }

        if (forbiddenCount > 0)
        {
            findings.Add(
                "LEXICAL_FORBIDDEN_CONTROL_CHARACTERS_PRESENT",
                Mql5FindingSeverity.Error,
                $"The decoded source contains {forbiddenCount.ToString(CultureInfo.InvariantCulture)} forbidden control characters; one aggregate finding is emitted and no character is removed.",
                firstForbiddenLine,
                firstForbiddenColumn);
        }

        return new ControlCharacterScan(nulCount, forbiddenCount);
    }

    private static StructuralScan AnalyzeStructure(
        string source,
        IReadOnlyList<SyntaxToken> tokens,
        FindingCollector findings)
    {
        var delimiters = new Stack<DelimiterFrame>();
        var conditionals = new Stack<ConditionalFrame>();
        int maximumDelimiterDepth = 0;
        int functionDefinitionCount = 0;
        int typeDeclarationCount = 0;
        int inputDeclarationCount = 0;
        int statementTerminatorCount = 0;
        int macroDefinitionCount = 0;
        int conditionalDirectiveCount = 0;

        for (int tokenIndex = 0; tokenIndex < tokens.Count; tokenIndex++)
        {
            SyntaxToken token = tokens[tokenIndex];
            if (token.Kind == SyntaxTokenKind.PreprocessorStart)
            {
                int directiveIndex = tokenIndex + 1;
                if (directiveIndex >= tokens.Count
                    || tokens[directiveIndex].Line != token.Line
                    || tokens[directiveIndex].Kind != SyntaxTokenKind.Identifier)
                {
                    findings.Add(
                        "PREPROCESSOR_DIRECTIVE_NAME_MISSING",
                        Mql5FindingSeverity.Error,
                        "A preprocessor marker is not followed by a directive name on the same line.",
                        token.Line,
                        token.Column);
                    continue;
                }

                SyntaxToken directive = tokens[directiveIndex];
                if (TokenEquals(source, directive, "define"))
                {
                    macroDefinitionCount++;
                }
                else if (TokenEquals(source, directive, "if")
                    || TokenEquals(source, directive, "ifdef")
                    || TokenEquals(source, directive, "ifndef"))
                {
                    conditionalDirectiveCount++;
                    conditionals.Push(new ConditionalFrame(directive.Line, directive.Column, false));
                }
                else if (TokenEquals(source, directive, "else")
                    || TokenEquals(source, directive, "elif"))
                {
                    conditionalDirectiveCount++;
                    if (conditionals.Count == 0)
                    {
                        findings.Add(
                            "PREPROCESSOR_CONDITIONAL_WITHOUT_OPEN",
                            Mql5FindingSeverity.Error,
                            "A conditional branch directive has no matching opening directive.",
                            directive.Line,
                            directive.Column);
                    }
                    else
                    {
                        ConditionalFrame frame = conditionals.Pop();
                        if (frame.ElseSeen)
                        {
                            findings.Add(
                                "PREPROCESSOR_BRANCH_AFTER_ELSE",
                                Mql5FindingSeverity.Error,
                                "A conditional branch appears after an else branch.",
                                directive.Line,
                                directive.Column);
                        }

                        conditionals.Push(frame with
                        {
                            ElseSeen = frame.ElseSeen || TokenEquals(source, directive, "else")
                        });
                    }
                }
                else if (TokenEquals(source, directive, "endif"))
                {
                    conditionalDirectiveCount++;
                    if (!conditionals.TryPop(out _))
                    {
                        findings.Add(
                            "PREPROCESSOR_ENDIF_WITHOUT_OPEN",
                            Mql5FindingSeverity.Error,
                            "An endif directive has no matching opening directive.",
                            directive.Line,
                            directive.Column);
                    }
                }
            }

            if (token.IsDirectiveLine)
            {
                continue;
            }

            if (token.Kind == SyntaxTokenKind.Identifier)
            {
                if (TokenEquals(source, token, "input"))
                {
                    inputDeclarationCount++;
                }

                if (TokenEquals(source, token, "class")
                    || TokenEquals(source, token, "struct")
                    || TokenEquals(source, token, "enum")
                    || TokenEquals(source, token, "union"))
                {
                    typeDeclarationCount++;
                }
            }

            if (token.Kind != SyntaxTokenKind.Punctuation || token.Length != 1)
            {
                continue;
            }

            char punctuation = source[token.Start];
            if (punctuation == ';')
            {
                statementTerminatorCount++;
                continue;
            }

            if (punctuation is '(' or '[' or '{')
            {
                if (punctuation == '{'
                    && IsFunctionOpeningBrace(source, tokens, tokenIndex))
                {
                    functionDefinitionCount++;
                }

                delimiters.Push(new DelimiterFrame(
                    punctuation,
                    token.Line,
                    token.Column));
                maximumDelimiterDepth = Math.Max(maximumDelimiterDepth, delimiters.Count);
                continue;
            }

            if (punctuation is not (')' or ']' or '}'))
            {
                continue;
            }

            if (!delimiters.TryPop(out DelimiterFrame opening))
            {
                findings.Add(
                    "DELIMITER_CLOSE_WITHOUT_OPEN",
                    Mql5FindingSeverity.Error,
                    "A closing delimiter has no matching opening delimiter.",
                    token.Line,
                    token.Column);
                continue;
            }

            if (!DelimitersMatch(opening.Character, punctuation))
            {
                findings.Add(
                    "DELIMITER_KIND_MISMATCH",
                    Mql5FindingSeverity.Error,
                    "An opening delimiter is closed by a different delimiter kind.",
                    token.Line,
                    token.Column);
            }
        }

        while (delimiters.TryPop(out DelimiterFrame opening))
        {
            findings.Add(
                "DELIMITER_OPEN_WITHOUT_CLOSE",
                Mql5FindingSeverity.Error,
                "An opening delimiter reaches end-of-file without a matching close.",
                opening.Line,
                opening.Column);
        }

        while (conditionals.TryPop(out ConditionalFrame conditional))
        {
            findings.Add(
                "PREPROCESSOR_CONDITIONAL_WITHOUT_ENDIF",
                Mql5FindingSeverity.Error,
                "A conditional compilation region reaches end-of-file without endif.",
                conditional.Line,
                conditional.Column);
        }

        bool delimitersBalanced = !findings.Findings.Any(finding =>
            finding.Code.StartsWith("DELIMITER_", StringComparison.Ordinal));
        bool conditionalsBalanced = !findings.Findings.Any(finding =>
            finding.Code.StartsWith("PREPROCESSOR_", StringComparison.Ordinal));
        return new StructuralScan(
            functionDefinitionCount,
            typeDeclarationCount,
            inputDeclarationCount,
            statementTerminatorCount,
            macroDefinitionCount,
            conditionalDirectiveCount,
            maximumDelimiterDepth,
            delimitersBalanced,
            conditionalsBalanced);
    }

    private static bool IsFunctionOpeningBrace(
        string source,
        IReadOnlyList<SyntaxToken> tokens,
        int braceIndex)
    {
        int closeIndex = braceIndex - 1;
        if (closeIndex < 0
            || tokens[closeIndex].Kind != SyntaxTokenKind.Punctuation
            || tokens[closeIndex].Length != 1
            || source[tokens[closeIndex].Start] != ')')
        {
            return false;
        }

        int depth = 0;
        for (int index = closeIndex; index >= 0; index--)
        {
            SyntaxToken token = tokens[index];
            if (token.IsDirectiveLine
                || token.Kind != SyntaxTokenKind.Punctuation
                || token.Length != 1)
            {
                continue;
            }

            char punctuation = source[token.Start];
            if (punctuation == ')')
            {
                depth++;
            }
            else if (punctuation == '(')
            {
                depth--;
                if (depth == 0)
                {
                    int nameIndex = index - 1;
                    if (nameIndex < 0 || tokens[nameIndex].Kind != SyntaxTokenKind.Identifier)
                    {
                        return false;
                    }

                    string name = source.Substring(
                        tokens[nameIndex].Start,
                        tokens[nameIndex].Length);
                    return !ControlFlowNames.Contains(name);
                }
            }
        }

        return false;
    }

    private static bool TokenEquals(string source, SyntaxToken token, string expected) =>
        source.AsSpan(token.Start, token.Length).SequenceEqual(expected.AsSpan());

    private static bool DelimitersMatch(char opening, char closing) =>
        opening switch
        {
            '(' => closing == ')',
            '[' => closing == ']',
            '{' => closing == '}',
            _ => false
        };

    private static Dictionary<string, string[]> BuildAdjacency(
        IEnumerable<Mql5SourceManifest> files)
    {
        return files.ToDictionary(
            static file => file.RelativePath,
            static file => file.Includes
                .Where(static include => include.Resolution == Mql5IncludeResolution.ResolvedInCorpus)
                .Select(static include => include.ResolvedRelativePath!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ThenBy(static path => path, StringComparer.Ordinal)
                .ToArray(),
            StringComparer.OrdinalIgnoreCase);
    }

    private static HashSet<string> FindCycleMembers(
        Dictionary<string, string[]> adjacency)
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var finishOrder = new List<string>(adjacency.Count);
        foreach (string start in adjacency.Keys
                     .Order(StringComparer.OrdinalIgnoreCase)
                     .ThenBy(static path => path, StringComparer.Ordinal))
        {
            if (!visited.Add(start))
            {
                continue;
            }

            var stack = new Stack<GraphFrame>();
            stack.Push(new GraphFrame(start, 0));
            while (stack.TryPeek(out GraphFrame frame))
            {
                string[] neighbors = adjacency[frame.Path];
                if (frame.NextNeighborIndex < neighbors.Length)
                {
                    string neighbor = neighbors[frame.NextNeighborIndex];
                    stack.Pop();
                    stack.Push(frame with { NextNeighborIndex = frame.NextNeighborIndex + 1 });
                    if (visited.Add(neighbor))
                    {
                        stack.Push(new GraphFrame(neighbor, 0));
                    }

                    continue;
                }

                stack.Pop();
                finishOrder.Add(frame.Path);
            }
        }

        var reverse = adjacency.Keys.ToDictionary(
            static path => path,
            static _ => new List<string>(),
            StringComparer.OrdinalIgnoreCase);
        foreach ((string source, string[] targets) in adjacency)
        {
            foreach (string target in targets)
            {
                reverse[target].Add(source);
            }
        }

        foreach (List<string> neighbors in reverse.Values)
        {
            neighbors.Sort(StringComparer.OrdinalIgnoreCase);
        }

        var assigned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var cycleMembers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int orderIndex = finishOrder.Count - 1; orderIndex >= 0; orderIndex--)
        {
            string start = finishOrder[orderIndex];
            if (!assigned.Add(start))
            {
                continue;
            }

            var component = new List<string>();
            var stack = new Stack<string>();
            stack.Push(start);
            while (stack.TryPop(out string? path))
            {
                component.Add(path);
                foreach (string neighbor in reverse[path])
                {
                    if (assigned.Add(neighbor))
                    {
                        stack.Push(neighbor);
                    }
                }
            }

            bool selfCycle = component.Count == 1
                && adjacency[component[0]].Contains(component[0], StringComparer.OrdinalIgnoreCase);
            if (component.Count > 1 || selfCycle)
            {
                cycleMembers.UnionWith(component);
            }
        }

        return cycleMembers;
    }

    private static Mql5DependencyClosureEvidence BuildDependencyClosure(
        string sourcePath,
        Dictionary<string, string[]> adjacency,
        IReadOnlySet<string> cycleMembers)
    {
        string[] direct = adjacency[sourcePath];
        var reachable = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pending = new Stack<string>(direct.Reverse());
        while (pending.TryPop(out string? path))
        {
            if (!reachable.Add(path))
            {
                continue;
            }

            foreach (string dependency in adjacency[path].Reverse())
            {
                pending.Push(dependency);
            }
        }

        reachable.Remove(sourcePath);

        string[] transitive = reachable
            .Order(StringComparer.OrdinalIgnoreCase)
            .ThenBy(static path => path, StringComparer.Ordinal)
            .ToArray();
        string[] reachableCycles = reachable
            .Append(sourcePath)
            .Where(cycleMembers.Contains)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ThenBy(static path => path, StringComparer.Ordinal)
            .ToArray();
        bool orderProven = reachableCycles.Length == 0;
        string[] dependencyFirstOrder = orderProven
            ? BuildDependencyFirstOrder(sourcePath, adjacency)
            : [];
        return new Mql5DependencyClosureEvidence(
            direct,
            transitive,
            dependencyFirstOrder,
            reachableCycles,
            orderProven);
    }

    private static string[] BuildDependencyFirstOrder(
        string sourcePath,
        Dictionary<string, string[]> adjacency)
    {
        var closure = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pending = new Stack<string>(adjacency[sourcePath].Reverse());
        while (pending.TryPop(out string? path))
        {
            if (!closure.Add(path))
            {
                continue;
            }

            foreach (string dependency in adjacency[path].Reverse())
            {
                pending.Push(dependency);
            }
        }

        closure.Remove(sourcePath);
        var dependencyCounts = closure.ToDictionary(
            static path => path,
            path => adjacency[path].Count(closure.Contains),
            StringComparer.OrdinalIgnoreCase);
        var dependents = closure.ToDictionary(
            static path => path,
            static _ => new List<string>(),
            StringComparer.OrdinalIgnoreCase);
        foreach (string path in closure)
        {
            foreach (string dependency in adjacency[path].Where(closure.Contains))
            {
                dependents[dependency].Add(path);
            }
        }

        var ready = new SortedSet<string>(StablePathComparer.Instance);
        ready.UnionWith(dependencyCounts
            .Where(static pair => pair.Value == 0)
            .Select(static pair => pair.Key));
        var order = new List<string>(closure.Count);
        while (ready.Count > 0)
        {
            string path = ready.Min!;
            ready.Remove(path);
            order.Add(path);
            foreach (string dependent in dependents[path])
            {
                dependencyCounts[dependent]--;
                if (dependencyCounts[dependent] == 0)
                {
                    ready.Add(dependent);
                }
            }
        }

        return order.ToArray();
    }

    private static string ComputeDependencyGraphDigest(
        IEnumerable<Mql5SourceManifest> files)
    {
        var canonical = new StringBuilder();
        foreach (Mql5SourceManifest file in files)
        {
            AppendCanonical(canonical, file.RelativePath);
            foreach (Mql5IncludeManifest include in file.Includes)
            {
                AppendCanonical(canonical, include.DeclaredPath);
                AppendCanonical(canonical, include.Kind.ToString());
                AppendCanonical(canonical, include.Resolution.ToString());
                AppendCanonical(canonical, include.ResolvedRelativePath ?? string.Empty);
                AppendCanonical(canonical, include.Line);
            }
        }

        return HashCanonical(canonical);
    }

    private static string ComputeFileEvidenceDigest(Mql5ConversionFileEvidence file)
    {
        var canonical = new StringBuilder();
        AppendCanonical(canonical, file.RelativePath);
        AppendCanonical(canonical, file.SourceSha256);
        AppendCanonical(canonical, file.DependencyClosureSha256);
        AppendCanonical(canonical, file.TextEncoding);
        AppendCanonical(canonical, file.Kind.ToString());
        AppendCanonical(canonical, file.StaticDisposition.ToString());
        AppendCanonical(canonical, file.Disposition.ToString());
        foreach (string entrypoint in file.Entrypoints)
        {
            AppendCanonical(canonical, entrypoint);
        }

        foreach (Mql5DetectedFeature feature in file.StaticFeatures)
        {
            AppendCanonical(canonical, feature.Code);
            AppendCanonical(canonical, feature.Support.ToString());
            AppendCanonical(canonical, feature.OccurrenceCount);
            foreach (int line in feature.Lines)
            {
                AppendCanonical(canonical, line);
            }
        }

        foreach (Mql5CompatibilityFinding finding in file.StaticFindings)
        {
            AppendCanonical(canonical, finding.Code);
            AppendCanonical(canonical, finding.Severity.ToString());
            AppendCanonical(canonical, finding.Support.ToString());
            AppendCanonical(canonical, finding.Message);
            foreach (int line in finding.Lines)
            {
                AppendCanonical(canonical, line);
            }
        }

        foreach (Mql5DependencyEdgeEvidence include in file.Includes)
        {
            AppendCanonical(canonical, include.DeclaredPath);
            AppendCanonical(canonical, include.Kind.ToString());
            AppendCanonical(canonical, include.Resolution.ToString());
            AppendCanonical(canonical, include.ResolvedRelativePath ?? string.Empty);
            AppendCanonical(canonical, include.Line);
        }

        foreach (string path in file.DependencyClosure.DirectDependencies)
        {
            AppendCanonical(canonical, path);
        }

        foreach (string path in file.DependencyClosure.TransitiveDependencies)
        {
            AppendCanonical(canonical, path);
        }

        foreach (string path in file.DependencyClosure.DependencyFirstOrder)
        {
            AppendCanonical(canonical, path);
        }

        foreach (string path in file.DependencyClosure.ReachableCycleMembers)
        {
            AppendCanonical(canonical, path);
        }

        AppendCanonical(canonical, file.DependencyClosure.DependencyFirstOrderProven);
        foreach (int value in new[]
                 {
                     file.Lexical.TokenCount,
                     file.Lexical.IdentifierCount,
                     file.Lexical.NumericLiteralCount,
                     file.Lexical.StringLiteralCount,
                     file.Lexical.CharacterLiteralCount,
                     file.Lexical.CommentCount,
                     file.Lexical.NulCharacterCount,
                     file.Lexical.ForbiddenControlCharacterCount,
                     file.Lexical.PreprocessorDirectiveCount,
                     file.Lexical.MaximumDelimiterDepth,
                     file.Structural.FunctionDefinitionCount,
                     file.Structural.TypeDeclarationCount,
                     file.Structural.InputDeclarationCount,
                     file.Structural.StatementTerminatorCount,
                     file.Structural.MacroDefinitionCount,
                     file.Structural.ConditionalDirectiveCount
                 })
        {
            AppendCanonical(canonical, value);
        }

        AppendCanonical(canonical, file.Structural.DelimitersBalanced);
        AppendCanonical(canonical, file.Structural.ConditionalDirectivesBalanced);
        AppendCanonical(canonical, file.Structural.FullGrammarParseProven);
        AppendCanonical(canonical, file.Structural.TypeCheckProven);
        AppendCanonical(canonical, file.Structural.RestrictedIrLoweringProven);
        foreach (Mql5EvidenceStage stage in file.Stages)
        {
            AppendCanonical(canonical, stage.Name.ToString());
            AppendCanonical(canonical, stage.Status.ToString());
            AppendCanonical(canonical, stage.EvidenceCode);
        }

        foreach (Mql5ConversionEvidenceFinding finding in file.Findings)
        {
            AppendCanonical(canonical, finding.Code);
            AppendCanonical(canonical, finding.Severity.ToString());
            AppendCanonical(canonical, finding.Message);
            AppendCanonical(canonical, finding.Location?.Line ?? 0);
            AppendCanonical(canonical, finding.Location?.Column ?? 0);
        }

        return HashCanonical(canonical);
    }

    private static string ComputeDependencyClosureDigest(
        string sourcePath,
        Mql5DependencyClosureEvidence closure,
        Dictionary<string, Mql5SourceManifest> staticByPath)
    {
        var canonical = new StringBuilder();
        AppendCanonical(canonical, sourcePath);
        foreach (string path in closure.DirectDependencies)
        {
            AppendCanonical(canonical, path);
        }

        foreach (string path in closure.TransitiveDependencies)
        {
            AppendCanonical(canonical, path);
            AppendCanonical(canonical, staticByPath[path].Sha256);
        }

        foreach (string path in closure.DependencyFirstOrder)
        {
            AppendCanonical(canonical, path);
        }

        foreach (string path in closure.ReachableCycleMembers)
        {
            AppendCanonical(canonical, path);
        }

        AppendCanonical(canonical, closure.DependencyFirstOrderProven);
        return HashCanonical(canonical);
    }

    private static string ComputeCorpusEvidenceDigest(
        Mql5CorpusManifest staticManifest,
        string dependencyGraphSha256,
        IEnumerable<Mql5ConversionFileEvidence> files)
    {
        var canonical = new StringBuilder();
        AppendCanonical(canonical, SchemaVersion);
        AppendCanonical(canonical, AnalyzerVersion);
        AppendCanonical(canonical, staticManifest.SchemaVersion);
        AppendCanonical(canonical, staticManifest.AnalyzerVersion);
        AppendCanonical(canonical, staticManifest.CorpusSha256);
        AppendCanonical(canonical, dependencyGraphSha256);
        foreach (Mql5ConversionFileEvidence file in files)
        {
            AppendCanonical(canonical, file.RelativePath);
            AppendCanonical(canonical, file.EvidenceSha256);
        }

        return HashCanonical(canonical);
    }

    private static void AppendCanonical(StringBuilder target, string value)
    {
        target.Append(value.Length.ToString(CultureInfo.InvariantCulture))
            .Append(':')
            .Append(value);
    }

    private static void AppendCanonical(StringBuilder target, int value) =>
        AppendCanonical(target, value.ToString(CultureInfo.InvariantCulture));

    private static void AppendCanonical(StringBuilder target, bool value) =>
        AppendCanonical(target, value ? "true" : "false");

    private static string HashCanonical(StringBuilder canonical) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())))
            .ToLowerInvariant();

    private static string Decode(byte[] content) => Mql5SourceDecoder.Decode(content).Text;

    private static string NormalizeRelativePath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string candidate = path.Replace('\\', '/').Trim();
        if (candidate.Length == 0
            || candidate[0] == '/'
            || candidate.Contains(':', StringComparison.Ordinal)
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

        string normalized = string.Join('/', segments);
        string extension = Path.GetExtension(normalized);
        if (!extension.Equals(".mq5", StringComparison.OrdinalIgnoreCase)
            && !extension.Equals(".mqh", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Only .mq5 and .mqh source documents are accepted.", nameof(path));
        }

        return normalized;
    }

    private static void EnsureUniquePaths(IReadOnlyList<Mql5SourceDocument> documents)
    {
        string? duplicate = documents
            .GroupBy(static document => document.RelativePath, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(static group => group.Count() > 1)
            ?.Key;
        if (duplicate is not null)
        {
            throw new ArgumentException(
                $"Duplicate source path '{duplicate}' is not allowed.",
                nameof(documents));
        }
    }

    private static bool IsIdentifierStart(char character) =>
        character == '_' || char.IsLetter(character);

    private static bool IsIdentifierPart(char character) =>
        character == '_' || char.IsLetterOrDigit(character);

    private static bool IsForbiddenControl(char character) =>
        (character < ' ' && character is not ('\0' or '\t' or '\n' or '\r'))
        || character == '\u007f';

    private enum SyntaxTokenKind
    {
        Identifier,
        NumericLiteral,
        StringLiteral,
        CharacterLiteral,
        PreprocessorStart,
        Punctuation
    }

    private readonly record struct SyntaxToken(
        SyntaxTokenKind Kind,
        int Start,
        int Length,
        int Line,
        int Column,
        bool IsDirectiveLine);

    private readonly record struct DelimiterFrame(char Character, int Line, int Column);

    private readonly record struct ConditionalFrame(int Line, int Column, bool ElseSeen);

    private readonly record struct ControlCharacterScan(
        int NulCharacterCount,
        int ForbiddenControlCharacterCount);

    private readonly record struct GraphFrame(string Path, int NextNeighborIndex);

    private sealed record LexicalScan(
        IReadOnlyList<SyntaxToken> Tokens,
        int IdentifierCount,
        int NumericLiteralCount,
        int StringLiteralCount,
        int CharacterLiteralCount,
        int CommentCount,
        int NulCharacterCount,
        int ForbiddenControlCharacterCount,
        int PreprocessorDirectiveCount);

    private sealed record StructuralScan(
        int FunctionDefinitionCount,
        int TypeDeclarationCount,
        int InputDeclarationCount,
        int StatementTerminatorCount,
        int MacroDefinitionCount,
        int ConditionalDirectiveCount,
        int MaximumDelimiterDepth,
        bool DelimitersBalanced,
        bool ConditionalDirectivesBalanced);

    private sealed record SyntaxAnalysis(
        Mql5LexicalEvidence Lexical,
        Mql5StructuralEvidence Structural,
        IReadOnlyList<Mql5ConversionEvidenceFinding> Findings,
        bool LexicalPassed,
        bool StructuralPassed)
    {
        public bool SyntaxPassed => LexicalPassed && StructuralPassed;
    }

    private sealed class FindingCollector
    {
        private readonly List<Mql5ConversionEvidenceFinding> findings = [];
        private int droppedCount;

        public IReadOnlyList<Mql5ConversionEvidenceFinding> Findings => findings;

        public void Add(
            string code,
            Mql5FindingSeverity severity,
            string message,
            int line,
            int column)
        {
            if (findings.Count < MaximumFindingsPerFile)
            {
                findings.Add(new Mql5ConversionEvidenceFinding(
                    code,
                    severity,
                    message,
                    new Mql5EvidenceLocation(line, column)));
            }
            else
            {
                droppedCount++;
            }
        }

        public Mql5ConversionEvidenceFinding[] ToArray()
        {
            IEnumerable<Mql5ConversionEvidenceFinding> result = findings;
            if (droppedCount > 0)
            {
                result = result.Append(new Mql5ConversionEvidenceFinding(
                    "STRUCTURAL_FINDINGS_TRUNCATED",
                    Mql5FindingSeverity.Warning,
                    $"{droppedCount.ToString(CultureInfo.InvariantCulture)} additional structural findings were omitted after the per-file evidence limit.",
                    null));
            }

            return result
                .OrderByDescending(static finding => finding.Severity)
                .ThenBy(static finding => finding.Code, StringComparer.Ordinal)
                .ThenBy(static finding => finding.Location?.Line ?? 0)
                .ThenBy(static finding => finding.Location?.Column ?? 0)
                .ToArray();
        }
    }

    private sealed class StablePathComparer : IComparer<string>
    {
        public static StablePathComparer Instance { get; } = new();

        public int Compare(string? left, string? right)
        {
            int insensitive = StringComparer.OrdinalIgnoreCase.Compare(left, right);
            return insensitive != 0
                ? insensitive
                : StringComparer.Ordinal.Compare(left, right);
        }
    }
}
