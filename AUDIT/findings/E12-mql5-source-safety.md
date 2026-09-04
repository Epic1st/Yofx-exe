---
agent_id: E12
lane: mql5-source-safety
scope:
  - src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5SourceDecoder.cs
  - src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5SourceSecretScanner.cs
  - src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5MarkdownEscaper.cs
  - src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5CompilerOutputParser.cs
status: COMPLETE
generated: 2026-08-29T11:26:15Z
counts: { P0: 0, P1: 2, P2: 3, P3: 2 }
---

# E12 — mql5-source-safety

## Scope audited
- `src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5SourceDecoder.cs` (278 lines)
- `src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5SourceSecretScanner.cs` (226 lines)
- `src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5MarkdownEscaper.cs` (53 lines)
- `src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5CompilerOutputParser.cs` (371 lines)

## Verdict
The audited governance components implement strict fail-closed guards, bounded input handling, and rigorous property whitelisting. However, several critical semantic edge cases exist: BOM-less UTF-16LE source files under 64 bytes are misclassified as binary payloads, the secret scanner exhibits significant false negative vectors against common MQL5 macro and split string patterns, and Unicode line/paragraph separators bypass markdown table escaping. Addressing these gaps is necessary to ensure consistent source ingestion and credential protection across the platform.

## Findings

### [P1] BOM-less UTF-16LE sources shorter than 64 bytes misclassified as binary
- **Where:** `src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5SourceDecoder.cs:248-251`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
    private static UnicodeEncoding? DetectBomlessUtf16(ReadOnlySpan<byte> content)
    {
        if (content.Length < 64 || content.Length % 2 != 0)
        {
            return null;
        }
  ```
- **Failure:** Valid short MQL5 scripts or include headers (e.g. 40-50 bytes) encoded in UTF-16LE without a BOM return `null` from `DetectBomlessUtf16`. The decoder subsequently detects zero bytes (`content.Contains((byte)0)` at line 90) and invokes `CreateBinary`, returning `Mql5SourceContentKind.Binary` decoded via Latin-1. Downstream consumers (`Mql5FrontEnd.Compile`) abort with `MQL5_FRONTEND_BINARY_SOURCE`, and `Mql5SourceSecretScanner` fails to detect secrets due to interleaved NUL bytes.
- **Fix:** Remove the arbitrary 64-byte minimum in `DetectBomlessUtf16` or evaluate zero-byte distribution for small even-length spans before falling back to binary classification.

### [P1] Secret scanner fails to detect preprocessor macros, split strings, and non-whitelisted variable names
- **Where:** `src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5SourceSecretScanner.cs:79-86`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
    private static readonly Regex SensitiveStringAssignment = new(
        "^[ \\t]*(?:(?:input|sinput)[ \\t]+)?(?:const[ \\t]+)?string[ \\t]+"
        + "(?<name>(?:telegram(?:bot)?token|telegramchatid|emailaddress|"
        + "(?:openai|anthropic|github|stripe|slack|aws)?_?api_?key|"
        + "(?:account|broker|database|db)?_?password|passphrase|client_?secret|access_?token))"
        + "[ \\t]*=[ \\t]*\"(?<value>(?:\\\\.|[^\"\\r\\n])*)\"",
        RuleOptions | RegexOptions.IgnoreCase | RegexOptions.Multiline,
        MatchTimeout);
  ```
- **Failure:** Plaintext credentials defined using `#define` macros (e.g. `#define BROKER_PASSWORD "RealPass123"`), multiline assignments (`string password =\n "Secret";`), string concatenation (`string api_key = "sk-" + "...";`), or common variable names (`secret_key`, `master_password`, `botToken`, `auth_token`) do not match `SensitiveStringAssignment`. The scanner produces false negatives, allowing raw secret material to pass intake undetected.
- **Fix:** Extend regex pattern matching to include `#define` directives, allow newline whitespace around `=`, expand identifier matching keywords, and evaluate concatenated string literals.

### [P2] UTF-32LE BOM misidentified as UTF-16LE producing corrupted text with embedded NULs
- **Where:** `src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5SourceDecoder.cs:47-56`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
        if (content.StartsWith(Encoding.Unicode.Preamble))
        {
            return DecodeStrictOrBinary(
                content,
                content[Encoding.Unicode.Preamble.Length..],
                StrictUtf16LittleEndian,
                "utf-16le",
                usedFallbackEncoding: false);
        }
  ```
- **Failure:** A UTF-32LE source payload starts with preamble `0xFF 0xFE 0x00 0x00`. Because `Encoding.Unicode.Preamble` is `0xFF 0xFE`, `content.StartsWith` matches, strips 2 bytes, and decodes the remainder with `StrictUtf16LittleEndian`. Since `0x0000` is valid UTF-16, decoding succeeds without error and returns `ContentKind.Text` with `EncodingName: "utf-16le"`, yielding a corrupted string containing alternating NUL characters.
- **Fix:** Check for 4-byte UTF-32LE (`FF FE 00 00`) and UTF-32BE (`00 00 FE FF`) preambles before matching UTF-16 BOM prefixes.

### [P2] Unicode Line and Paragraph Separators unescaped in Markdown table cells
- **Where:** `src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5MarkdownEscaper.cs:14-19`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
        foreach (char character in value)
        {
            if (char.IsControl(character)
                || char.GetUnicodeCategory(character) == UnicodeCategory.Format)
            {
                escaped.Append(' ');
                continue;
            }
  ```
- **Failure:** In .NET, Unicode characters `\u2028` (Line Separator) and `\u2029` (Paragraph Separator) belong to `UnicodeCategory.LineSeparator` and `UnicodeCategory.ParagraphSeparator`, returning `false` for `char.IsControl`. They pass through `EscapeTableCell` unreplaced, causing Markdown table rows in generated evidence reports to split and allowing unescaped block-level Markdown injection.
- **Fix:** Add `UnicodeCategory.LineSeparator` and `UnicodeCategory.ParagraphSeparator` to the character replacement predicate.

### [P2] Cyrillic Windows-1251 sources misidentified as Windows-1252 producing Mojibake
- **Where:** `src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5SourceDecoder.cs:104-110`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
        catch (DecoderFallbackException)
        {
            if (TryDecodeWindows1252(content, out string? windows1252))
            {
                return CreateText(
                    windows1252!,
                    "windows-1252",
                    usedFallbackEncoding: true);
            }
  ```
- **Failure:** MQL5 sources encoded in Windows-1251 (standard Cyrillic ANSI in the MetaTrader ecosystem) with bytes `0xC0..0xFF` fail UTF-8 decoding and fall into `TryDecodeWindows1252`. Because `TryDecodeWindows1252` treats any byte `>= 0xA0` as a valid direct char cast `(char)value`, it succeeds and labels the file as `windows-1252`, corrupting Cyrillic comments and string literals into Latin-1 characters without warning.
- **Fix:** Avoid assuming non-UTF8 ANSI bytes are Windows-1252 or validate multi-byte / codepage heuristics before declaring valid Latin-1 text.

### [P3] Compiler output preflight byte limit permits oversized string allocations before character count check
- **Where:** `src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5CompilerOutputParser.cs:133-134`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
            invalidRecordLength |= recordLength is < 2 or > MaximumRecordUtf8Bytes;
            recordStart = index + 1;
  ```
- **Failure:** `MaximumRecordUtf8Bytes` is set to `262,144` bytes (4x `MaximumRecordCharacters` of `65,536`). An ASCII record of 150,000 bytes passes `PreflightRecords` and forces `Parse` to materialize a 150,000-character string via `StrictUtf8.GetString` before rejecting it at `line.Length > MaximumRecordCharacters` (line 90), bypassing early memory-bounding guarantees during preflight.
- **Fix:** Tighten preflight record byte bounds or check decoded buffer lengths before full string instantiation.

### [P3] Single-document secret scan interface neglects `#include` dependencies
- **Where:** `src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5SourceSecretScanner.cs:88-95`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
    public static Mql5SourceSecretFinding? FindFirst(Mql5SourceDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(document.RelativePath);
        ArgumentNullException.ThrowIfNull(document.Content);

        string source = Mql5SourceDecoder.Decode(document.Content).Text;
  ```
- **Failure:** `Mql5SourceSecretScanner` operates strictly on single isolated documents and lacks an include-aware or corpus-wide traversal method. If caller pipelines scan an entrypoint `.mq5` document without explicitly discovering and iterating all referenced `.mqh` files, secrets residing in included files bypass detection.
- **Fix:** Introduce an overloaded scanner accepting a complete document corpus or document that include closure resolution is caller-mandatory.

## Referrals
- `src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5FrontEnd.cs` — Does not check `ForbiddenControlCharacterCount` when `ContentKind == Text`, proceeding to parse texts containing control characters.
- `src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5IsolatedCompileOrchestrator.cs:863` — Hardcodes `maximumRecords: 1` in `Mql5CompilerOutputParser.Parse`, preventing multi-target batch compile evidence handling.

## Coverage gaps
- `src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5SourceDecoder.cs:83-87` — `DetectBomlessUtf16` branch for `StrictUtf16BigEndian` is not covered by unit tests.
- `src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5SourceSecretScanner.cs:136-141` — `RegexMatchTimeoutException` handling branch is untested under catastrophic backtracking inputs.
- `src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5CompilerOutputParser.cs:244-251` — `Mql5FileCompileStatus.Failed` validation branch where `exitCode == 0` or missing error diagnostics is untested.


--- agy: gemini-3.7-flash-high | effort=high | mode=plan (read-only) | 139.8s | 271156 tok | id=23c5a65a-586f-4e0e-846c-db5216ef0509
