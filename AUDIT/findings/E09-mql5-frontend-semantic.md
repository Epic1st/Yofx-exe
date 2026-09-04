---
agent_id: E09
lane: mql5-frontend-semantic
scope:
  - src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5FrontEnd.cs
  - src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5SemanticModel.cs
  - src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5Syntax.cs
  - src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5CompileContracts.cs
status: COMPLETE
generated: 2026-08-29T11:27:00Z
counts: { P0: 0, P1: 0, P2: 0, P3: 0 }
---

# E09 — mql5-frontend-semantic

## Scope audited
- `src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5FrontEnd.cs` (101 lines) — Front-end pipeline entry point (`Decode` → `Lex/Parse` → `Lower`), stage tracking, diagnostic collection, and front-end outcome records.
- `src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5SemanticModel.cs` (367 lines) — Semantic type system (`Mql5ResolvedType`), symbol descriptors (`Mql5ResolvedSymbol`), semantic model container, bind statistics, and diagnostic codes.
- `src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5Syntax.cs` (359 lines) — AST definitions spanning expressions, type references, statements, directives, and declarations.
- `src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5CompileContracts.cs` (566 lines) — Isolated compilation contracts, compile profiles, runner attestation records, transition validation, security boundary policies, and path/resource validation.

## Verdict
The front-end pipeline wiring, semantic model, syntax tree definitions, and isolated compilation contracts are clean, robust, and correctly implemented. Pipeline execution is strictly sequential and fail-closed: decode, parse, and lowering errors immediately halt forward progression, collect diagnostics across all stages, and zero out output modules. The front-end and semantic models maintain zero mutable static state between compilation runs, eliminating cross-compilation leakage, and the syntax model fully covers all grammar constructs emitted by the parser and consumed by downstream lowering.

## Findings
None. The audited area is structurally sound and satisfies all pipeline wiring, state isolation, and contract integrity requirements:
- In `Mql5FrontEnd.cs:44-99`, the compilation pipeline strictly orders decoding, parsing, and lowering. Errors from decoding (`decoded.ContentKind != Mql5SourceContentKind.Text`) and parsing (`!parsed.Succeeded || parsed.Unit is null`) short-circuit execution, return the exact stopped stage, and forward all diagnostics without generating partial or corrupted IR modules.
- State is completely isolated across invocations: no mutable static variables or caches exist in `Mql5FrontEnd`, `Mql5SemanticModel`, `Mql5Syntax`, or `Mql5CompileContracts`. Every compilation creates isolated, immutable data structures.
- `Mql5SemanticModel.cs` enforces reference equality via `Mql5ExpressionReferenceComparer` to prevent collisions between identical AST subtrees, and `Mql5Syntax.cs` provides node models for all expressions, statements, types, and declarations supported by the parser.
- `Mql5CompileContracts.cs` enforces strict parameter bounds, fail-closed isolation checks, canonical JSON profile hashing, and constant-time cryptographic hash verification with buffer zeroing (`CryptographicOperations.ZeroMemory`).

## Referrals
- `src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5Parser.cs` — Local struct, class, and union declarations inside function bodies (`ParseStatementCore:2091-2097`) are reduced via `NestedDeclarationStatement` to `Mql5EmptyStatement`, discarding local type definitions from the parsed AST.

## Coverage gaps
- `src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5FrontEnd.cs:53-56` — Untested distinction between `MQL5_FRONTEND_ALL_NUL_SOURCE` and `MQL5_FRONTEND_BINARY_SOURCE` diagnostic codes when non-text source buffers are supplied.
- `src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5SemanticModel.cs:108-111` — Untested multi-dimensional string array indexing path in `Mql5ResolvedType.ElementType()`, verifying that rank reduction correctly precedes `Mql5IrScalarKind.Natural16` element type extraction on single-dimensional strings.
- `src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5CompileContracts.cs:531-534` — Untested `OverflowException` branch in `Mql5CompileValidation.ValidateSourceReferences` when cumulative source sizes exceed `long.MaxValue`.


--- agy: gemini-3.7-flash-high | effort=high | mode=plan (read-only) | 133.8s | 309643 tok | id=aa8bbf26-f994-4394-9102-1a3ef6d383cc
