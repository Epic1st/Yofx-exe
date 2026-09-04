---
agent_id: E07
lane: Conversion Evidence Analyzer & Formatter
scope:
  - src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5ConversionEvidence.cs
  - src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5ConversionEvidenceAnalyzer.cs
  - src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5ConversionEvidenceFormatter.cs
status: COMPLETE
generated: 2026-08-29T11:25:04Z
counts: { P0: 0, P1: 1, P2: 2, P3: 1 }
---

# E07 — Conversion Evidence Analyzer & Formatter

## Scope audited
- `src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5ConversionEvidence.cs` (117 lines)
- `src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5ConversionEvidenceAnalyzer.cs` (1606 lines)
- `src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5ConversionEvidenceFormatter.cs` (176 lines)

## Verdict
The conversion evidence subsystem is designed to fail closed: it explicitly disclaims grammar parsing, type checking, and IR lowering proofs (`FullGrammarParseProven`, `TypeCheckProven`, and `RestrictedIrLoweringProven` remain hardcoded `false`, with downstream gates set to `NotAttempted` or `Blocked`). Markdown rendering sanitizes metadata via cell escaping and completely omits raw source bodies and arbitrary identifiers from reports. However, lexical tokenization permits unescaped newlines inside string/character literals without raising an error, allowing unclosed literals to span across lines and swallow intermediate structural tokens; finding truncation causes delimiter and preprocessor balance checks to report false passes; and dependency finding emission omits error records for transitive dependencies with rejected static dispositions.

## Findings

### [P1] Multiline unescaped string literals are parsed without error and swallow intermediate code tokens
- **Where:** `src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5ConversionEvidenceAnalyzer.cs:545-554`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
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
  ```
- **Failure:** In MQL5, string and character literals cannot span lines without an explicit `\` line continuation. If source contains an unclosed string (e.g. `string a = "unclosed;\nvoid OnTick() {}\nstring b = "closed";`), `Tokenize` increments line numbers and continues consuming subsequent lines as string literal content until the next quote character. The intermediate function declarations, preprocessor directives, and delimiter tokens are swallowed into a single `StringLiteral` token, leaving `FunctionDefinitionCount = 0` and omitting `LEXICAL_UNTERMINATED_STRING_LITERAL` because the literal is considered terminated by the later quote.
- **Fix:** Check whether `\n` is preceded by an active backslash escape; if not, emit a `LEXICAL_UNTERMINATED_STRING_LITERAL` error finding and terminate literal tokenization at the end of the line.

### [P2] Finding truncation causes delimiter and preprocessor balance indicators to report false positive passes
- **Where:** `src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5ConversionEvidenceAnalyzer.cs:913-916`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
        bool delimitersBalanced = !findings.Findings.Any(finding =>
            finding.Code.StartsWith("DELIMITER_", StringComparison.Ordinal));
        bool conditionalsBalanced = !findings.Findings.Any(finding =>
            finding.Code.StartsWith("PREPROCESSOR_", StringComparison.Ordinal));
  ```
- **Failure:** When a file has 256 delimiter errors early in the file, `FindingCollector` hits `MaximumFindingsPerFile` (256) and drops all subsequent findings. If an unclosed `#ifdef` directive occurs later in the file, `PREPROCESSOR_CONDITIONAL_WITHOUT_ENDIF` is dropped and not added to `findings.Findings`. `conditionalsBalanced` evaluates `!findings.Findings.Any(...)` to `true`, producing `Mql5StructuralEvidence.ConditionalDirectivesBalanced = true` and hashing `true` into `ComputeFileEvidenceDigest` despite the preprocessor conditional stack being non-empty.
- **Fix:** Determine `delimitersBalanced` and `conditionalsBalanced` directly from parser state (e.g. `delimiters.Count == 0 && !hasDelimiterError` and `conditionals.Count == 0 && !hasPreprocessorError`) rather than querying the truncated `findings.Findings` list.

### [P2] Transitive dependency closures with rejected static disposition produce blocked disposition without error findings
- **Where:** `src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5ConversionEvidenceAnalyzer.cs:326-333`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
        if (transitivePaths.Any(path => staticByPath[path].Disposition == Mql5StaticDisposition.NeedsSource))
        {
            findings.Add(new Mql5ConversionEvidenceFinding(
                "DEPENDENCY_CLOSURE_HAS_MISSING_SOURCE",
                Mql5FindingSeverity.Error,
                "A resolved local dependency has an incomplete source closure.",
                null));
        }
  ```
- **Failure:** In `BuildFileEvidence`, `hasMissingDependency` is set to `true` if any transitive dependency has `Mql5StaticDisposition.Rejected` or invalid include resolutions, correctly setting `disposition = BlockedMissingDependency`. However, `AddDependencyFindings` only checks for `Mql5StaticDisposition.NeedsSource`. When a transitive dependency has `Rejected` disposition (e.g., failed static integrity checks), the parent file's `Findings` collection contains no corresponding error finding explaining why dependency resolution failed.
- **Fix:** Update `AddDependencyFindings` to check `staticByPath[path].Disposition is Mql5StaticDisposition.NeedsSource or Mql5StaticDisposition.Rejected` as well as any unresolved include resolutions.

### [P3] Static input parameters declared with `sinput` are omitted from `InputDeclarationCount`
- **Where:** `src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5ConversionEvidenceAnalyzer.cs:824-828`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
            if (token.Kind == SyntaxTokenKind.Identifier)
            {
                if (TokenEquals(source, token, "input"))
                {
                    inputDeclarationCount++;
                }
  ```
- **Failure:** MQL5 uses `sinput` (static input) for input parameters that are constant across optimization runs. In `AnalyzeStructure`, `inputDeclarationCount` matches only `"input"`. Strategies declaring parameters using `sinput` (e.g. `sinput int MagicNumber = 12345;`) report `InputDeclarationCount = 0` in `Mql5StructuralEvidence`, misrepresenting the parameter inventory in structural evidence.
- **Fix:** Check for both `"input"` and `"sinput"`: `if (TokenEquals(source, token, "input") || TokenEquals(source, token, "sinput"))`.

## Referrals
None.

## Coverage gaps
- `src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5ConversionEvidenceAnalyzer.cs:1578-1582` — The branch appending `STRUCTURAL_FINDINGS_TRUNCATED` when `droppedCount > 0` (file exceeding 256 findings) is not exercised by tests in `Mql5ConversionEvidenceTests.cs`.
- `src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5ConversionEvidenceAnalyzer.cs:633-640` — The branch emitting `LEXICAL_TOKEN_LIMIT_EXCEEDED` when `tokens.Count >= MaximumTokensPerFile` (2,000,000 tokens) is not exercised by tests.
- `src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5ConversionEvidenceAnalyzer.cs:335-342` — The branch emitting `DEPENDENCY_CLOSURE_HAS_UNSUPPORTED_SEMANTICS` when a transitive dependency has `Mql5StaticDisposition.Unsupported` is not covered by a dedicated unit test.


--- agy: gemini-3.7-flash-high | effort=high | mode=plan (read-only) | 99.5s | 167684 tok | id=9a27802b-9d7d-42e9-ab11-7437abb69689
