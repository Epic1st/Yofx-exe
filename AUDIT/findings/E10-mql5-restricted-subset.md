---
agent_id: E10
lane: MQL5 Restricted Subset Security & Lowering
scope:
  - src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5RestrictedSubsetCompiler.cs
  - src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5RestrictedSubsetContracts.cs
  - src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5RestrictedCorpusArtifact.cs
  - src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5RestrictedCorpusArtifactFormatter.cs
status: COMPLETE
generated: 2026-08-29T11:27:00Z
counts: { P0: 0, P1: 0, P2: 0, P3: 0 }
---

# E10 — MQL5 Restricted Subset Security & Lowering

## Scope audited
- `src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5RestrictedSubsetCompiler.cs` (475 lines)
- `src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5RestrictedSubsetContracts.cs` (46 lines)
- `src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5RestrictedCorpusArtifact.cs` (176 lines)
- `src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5RestrictedCorpusArtifactFormatter.cs` (41 lines)

## Verdict
The restricted subset compilation subsystem is robust, secure, and strictly fails closed against construct escape. It enforces an explicit allow-list for lexical tokens, top-level grammar constructs, primitive scalar types, and literal value ranges, rejecting any unrecognized syntax prior to lowering. Code execution, macro expansion, external includes, and dynamic runtime compilation are completely absent from this boundary, ensuring untrusted MQL5 source cannot escape data-only IR lowering.

## Findings
None. The audited area enforces fail-closed containment through strict allow-lists across all compiler stages:
1. **Lexical Allow-List**: Lexing only permits whitespace, line/block comments, benign include guards/properties (`#property`, `#ifndef`, `#define`, `#endif`), identifiers, numeric/string literals, and a strict symbol set (`{};,=[]-`). Any other symbol (including parentheses `()`, operators, or punctuation required for executable logic) triggers `UNSUPPORTED_TOKEN`.
2. **Grammar Allow-List**: The parser top-level strictly permits only `struct`, `enum`, `input`, and `sinput` declarations. Functions, event handlers (e.g. `OnTick`), classes, expressions, and statements immediately fail with `UNSUPPORTED_TOP_LEVEL_CONSTRUCT`.
3. **Type & Value Bounds**: Struct fields and input declarations only allow 14 primitive scalar types (`ScalarTypes`). Arrays are rejected (`ARRAY_FIELD_NOT_SUPPORTED`), while numeric literals undergo `BigInteger` bounds checks per declared scalar integer width and finite checks for floating-point values.
4. **Macro & Include Isolation**: `#include` directives are disallowed (`UNSUPPORTED_PREPROCESSOR_DIRECTIVE`), and `#define` directives are skipped without macro substitution, preventing indirect token re-injection.
5. **Execution Isolation**: Lowering produces static data structures (`Mql5RestrictedIr`) with deterministic canonical JSON serialization and SHA-256 integrity binding; no code generation, compilation hosts, or native execution paths are invoked.

## Referrals
None.

## Coverage gaps
- `src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5RestrictedSubsetCompiler.cs:28-31` — Error branch when source document length exceeds the 16 MiB maximum source size limit (`SOURCE_SIZE_LIMIT_EXCEEDED`).
- `src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5RestrictedSubsetCompiler.cs:34-37` — Error branch when decoded source content kind is non-text (`SOURCE_NOT_TEXT`).
- `src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5RestrictedSubsetCompiler.cs:264-268` — Error branch when token count exceeds the 2,000,000 token ceiling (`TOKEN_LIMIT_EXCEEDED`).
- `src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5RestrictedSubsetCompiler.cs:343` — Enum auto-increment arithmetic overflow branch when `checked(value + 1)` exceeds `long.MaxValue` (`ENUM_VALUE_OVERFLOW`).
- `src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5RestrictedSubsetCompiler.cs:446` — Duplicate top-level symbol registration error when two disparate constructs (e.g. an input and an enum) declare the same identifier (`DUPLICATE_TOP_LEVEL_SYMBOL`).
- `src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5RestrictedCorpusArtifact.cs:141-150` — Diagnostic bounding branch in `BoundDiagnostics` when compilation yields >32 diagnostics, validating that `DIAGNOSTICS_TRUNCATED` is appended.


--- agy: gemini-3.7-flash-high | effort=high | mode=plan (read-only) | 81.2s | 140607 tok | id=37bba2cf-0f8b-4faa-a699-b8bd010dfc8d
