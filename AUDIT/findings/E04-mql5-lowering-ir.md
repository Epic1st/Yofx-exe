---
agent_id: E04
lane: mql5-lowering-ir
scope:
  - src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5Lowering.cs
  - src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5IrV2.cs
status: COMPLETE
generated: 2026-08-29T11:26:30Z
counts: { P0: 0, P1: 0, P2: 0, P3: 0 }
---

# E04 — mql5-lowering-ir

## Scope audited

- [Mql5Lowering.cs](file:///C:/Users/Dev23/Desktop/yo4x/src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5Lowering.cs) (1,453 lines)
- [Mql5IrV2.cs](file:///C:/Users/Dev23/Desktop/yo4x/src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5IrV2.cs) (1,520 lines)

Both files were read and verified in full, along with adjacent contracts in [Mql5Syntax.cs](file:///C:/Users/Dev23/Desktop/yo4x/src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5Syntax.cs), [Mql5Parser.cs](file:///C:/Users/Dev23/Desktop/yo4x/src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5Parser.cs), [Mql5FrontEnd.cs](file:///C:/Users/Dev23/Desktop/yo4x/src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5FrontEnd.cs), and test suites in `YO4X.Domain.Tests`.

## Verdict

The MQL5 lowering pass and IR v2 representation are sound, robust, and cleanly designed. The lowering pass is strictly structural (mapping AST syntax 1:1 to IR nodes without premature desugaring, temporary variable hoisting, or short-circuit rewrites) and fails closed on all unrepresented or malformed constructs, poisoning the compilation unit so no corrupted or partially lowered IR can ever reach code generation.

## Findings

None.

The audited components exhibit exceptional semantic fidelity:
1. **Evaluation Order & Temporaries:** Lowering introduces no temporary variables, ensuring execution order and side effects are preserved as written.
2. **Compound Assignments:** Operations such as `a[i++] += x` are lowered into `Mql5IrAssignmentExpression` with the target preserved 1:1, preventing double evaluation.
3. **Short-Circuit Operators:** Logical `&&` and `||` remain `Mql5IrBinaryExpression` nodes; no eager conversion into non-short-circuit forms occurs.
4. **Loop & Control Flow Semantics:** Loop structures (`for`, `while`, `do-while`), switch sections/labels (including default placement and fallthrough), and `break`/`continue` statements are retained without destructive transformations.
5. **Fail-Closed Completeness:** Every AST construct defined in `Mql5Syntax` is matched. Unsupported constructs (`union`, native `#import` bodies, operator overloads, non-identifier scope qualifiers, and recursion depth exceeding 192 levels) fail closed with explicit diagnostic codes, poisoning `Mql5LoweringResult.Module` to null.
6. **Canonical Serialization:** Byte-stable, platform-independent JSON serialization with SHA-256 hashing is implemented with deterministic formatting and invariant culture rules.

## Referrals

None.

## Coverage gaps

- [Mql5Lowering.cs:1430-1450](file:///C:/Users/Dev23/Desktop/yo4x/src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5Lowering.cs#L1430-L1450): Deep recursion limit guarding (`MaximumDepth = 192` returning `MQL5_LOWER_DEPTH_LIMIT_EXCEEDED`) is not covered by an adversarial nested expression test.
- [Mql5Lowering.cs:1049-1057](file:///C:/Users/Dev23/Desktop/yo4x/src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5Lowering.cs#L1049-L1057): Non-identifier qualifier expressions in `TryFlattenScope` returning false (`MQL5_LOWER_UNSUPPORTED_SCOPE_QUALIFIER`) are not covered by unit tests.
- [Mql5Lowering.cs:375-382](file:///C:/Users/Dev23/Desktop/yo4x/src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5Lowering.cs#L375-L382): Rejection of `union` type declarations (`MQL5_LOWER_UNSUPPORTED_UNION`) lacks a dedicated negative unit test.


--- agy: gemini-3.7-flash-high | effort=high | mode=plan (read-only) | 87.7s | 209964 tok | id=fa8f500c-4111-4130-b98c-18d2caefa656
