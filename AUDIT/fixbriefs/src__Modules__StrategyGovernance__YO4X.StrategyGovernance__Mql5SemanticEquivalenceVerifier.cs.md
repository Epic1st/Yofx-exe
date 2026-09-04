You are a fix agent on YO4X, a LIVE MetaTrader 5 / MQL5 algorithmic trading platform (.NET 10 backend, React frontend, an MQL5-to-C# transpiler, a deterministic backtest engine). An audit found defects in ONE file. Fix them.

THE ONLY FILE YOU MAY MODIFY:
  src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5SemanticEquivalenceVerifier.cs

Read that file completely first. You may read any other file for context, but you must not edit any other file, create files, delete files, or run commands.

FINDINGS TO FIX (3):

[1] [P1] Dual-Limit Numeric Tolerance Comparison Causes False Parity Rejections Near Zero
    Where:   src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5SemanticEquivalenceVerifier.cs:303-306
    Failure: When comparing small numeric quantities near zero (such as floating-point indicator deltas `0.0` vs `0.00001` or minimal profit adjustments), the absolute error is `0.00001` (well below a typical `MaximumAbsoluteError` of `0.001`), but the relative error is `1.0` (100%). Because the verifier uses logical OR (`||`), `toleranceExceeded` evaluates to `true`, rejecting legitimate floating-point approximations as `SEMANTIC_TRACE_NUMERIC_TOLERANCE_EXCEEDED` unless relative tolerance is configured to `>= 1.0` (which eliminates relative error protection for large balances/volumes).
    Suggested fix: Implement standard numerical closeness evaluation where an event is accepted if error is within absolute tolerance OR relative tolerance (`error <= MaximumAbsoluteError || error <= MaximumRelativeError * Math.Abs(referenceValue)`), rather than requiring simultaneous satisfaction of both thresholds for near-zero values.

[2] [P1] Semantic Equivalence Verifier Claims Parity "Proven" on Finite Non-Adversarial Sample Traces
    Where:   src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5SemanticEquivalenceVerifier.cs:356
    Failure: A transpiled MQL5-to-C# strategy containing serious divergence in unexercised trading branches (such as stop-loss/take-profit triggers, margin call liquidation, multi-currency data synchronization, or bar boundary rollover) is tested against a small or non-adversarial input trace (`InputEventCount` can be as low as 1 event per `ValidateRequest:384`). Because the sampled events happen to match, `Verify` returns `Mql5SemanticParityState.Proven` and sets `SemanticParityProven = true`. Downstream governance treats the strategy as formally proven equivalent, allowing diverge-prone trading logic into live execution.
    Suggested fix: Rename the proven state and reason code to represent trace match rather than formal semantic equivalence (e.g., `Mql5SemanticParityState.TraceParityMatched`), and require minimum sample size and trace diversity thresholds before certification.

[3] [P2] Inconsistent Evidence Check Conflates Non-Numeric Divergence with Evidence Corruption
    Where:   src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5SemanticEquivalenceVerifier.cs:307-315
    Failure: If an event has differing output payloads (`!eventOutputsExact`) caused by string, enum, or formatting differences rather than numeric drift, `item.NumericDivergenceCount == 0` is true, which inadvertently trips `item.NumericDivergenceCount == 0 && !eventOutputsExact` to true. If structural mismatch counters are not incremented by the runner, the verifier fails with `SEMANTIC_TRACE_DIVERGENCE_EVIDENCE_INVALID` (reporting runner evidence corruption) rather than reporting a payload mismatch.
    Suggested fix: Restrict the `!eventOutputsExact` branch of `evidenceInconsistent` to only trigger when all non-numeric and structural mismatch counters are zero (`item.NonNumericMismatchCount == 0 && item.MissingReferenceFieldCount == 0 && item.MissingLoweredFieldCount == 0 && !eventOutputsExact`).

HOW TO WORK:

1. Verify each finding against the actual code BEFORE changing anything. Line numbers may
   have shifted. If a finding is WRONG, or was already fixed, or the suggested fix would
   itself introduce a bug - do NOT apply it. Say so in your summary and move on. A refused
   bad fix is a good outcome; applying a wrong fix to a trading system is not.

2. Make the SMALLEST change that actually fixes the defect. Do not refactor, rename,
   reorder, reformat, restyle, or "improve" anything you were not asked about. Do not
   reflow existing lines. The diff must contain only the fix.

3. Match the surrounding code exactly - its naming, its comment density and voice, its
   error-handling idiom, its use of existing helpers. Read enough of the file to know what
   that is. Where the file already has a helper for what you need, use it rather than
   writing a new one.

4. Preserve public API and behaviour that was not identified as defective. If a correct
   fix would require changing a public signature, a database schema, a serialised contract,
   or shared behaviour outside this file, DO NOT do it - report it as needing a wider
   change instead.

5. This code decides real trades. For anything touching money, volume, price, margin, order
   state or time: be conservative, prefer failing closed over guessing, and preserve
   existing rounding/normalisation conventions unless the finding is specifically that the
   convention is wrong.

6. The project builds clean with zero warnings. Keep it that way - no unused variables, no
   unreachable code, no nullable warnings.

AFTER EDITING, output a short plain-text summary (no code fences), one line per finding:
  [n] APPLIED  - <what you changed, in a few words>
  [n] SKIPPED  - <why the finding was wrong or the fix unsafe>
Then a final line: FILES CHANGED: <the one path you edited, or NONE>

