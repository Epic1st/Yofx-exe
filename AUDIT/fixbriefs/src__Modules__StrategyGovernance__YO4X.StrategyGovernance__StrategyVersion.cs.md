You are a fix agent on YO4X, a LIVE MetaTrader 5 / MQL5 algorithmic trading platform (.NET 10 backend, React frontend, an MQL5-to-C# transpiler, a deterministic backtest engine). An audit found defects in ONE file. Fix them.

THE ONLY FILE YOU MAY MODIFY:
  src/Modules/StrategyGovernance/YO4X.StrategyGovernance/StrategyVersion.cs

Read that file completely first. You may read any other file for context, but you must not edit any other file, create files, delete files, or run commands.

FINDINGS TO FIX (3):

[1] [P2] ApproveForDemo skips digest lowercasing and leaves RuntimeVersion unvalidated
    Where:   src/Modules/StrategyGovernance/YO4X.StrategyGovernance/StrategyVersion.cs:121
    Failure: `CreateManualU0Candidate` and `RecordManualReview` normalize all digests to lowercase via `.ToLowerInvariant()`. In contrast, `ApproveForDemo` validates `evidence.EvidenceDigest` and `evidence.DatasetDigest` against `^[A-Fa-f0-9]{64}$` (which accepts uppercase hex) but assigns `evidence` directly without normalizing its digest fields to lowercase, and without validating `evidence.RuntimeVersion` for null or whitespace. If evidence generated with standard uppercase hex format (e.g. .NET `Convert.ToHexString`) is supplied, `ValidationEvidence.EvidenceDigest` retains uppercase characters, causing downstream case-sensitive comparisons and database check constraints (`check (package_sha256 ~ '^[0-9a-f]{64}$')`) to fail.
    Suggested fix: Normalize `EvidenceDigest` and `DatasetDigest` to lowercase, validate that `evidence.RuntimeVersion` is non-empty, and store the normalized record in `ValidationEvidence`.

[2] [P2] RecordManualReview preserves stale ValidationEvidence on re-review of demo-eligible versions
    Where:   src/Modules/StrategyGovernance/YO4X.StrategyGovernance/StrategyVersion.cs:97
    Failure: When an active `DemoEligible` strategy version undergoes a manual re-review, `RecordManualReview` updates `ReviewEvidenceDigest` and sets `State` back to `ManuallyReviewed`. However, it does not clear `ValidationEvidence`. The aggregate remains in the `ManuallyReviewed` state with stale validation evidence from the previous review cycle still attached, presenting inconsistent evidence state to consumers and audit logs before new validation evidence is approved.
    Suggested fix: Reset `ValidationEvidence = null;` inside `RecordManualReview` whenever a new manual review is recorded.

[3] [P3] ValidateDigest in ApproveForDemo reports parameter name as 'evidence' rather than specific digest property
    Where:   src/Modules/StrategyGovernance/YO4X.StrategyGovernance/StrategyVersion.cs:114
    Failure: When `evidence.EvidenceDigest` or `evidence.DatasetDigest` is invalid, `ValidateDigest` throws `ArgumentException` with `paramName` set to `"evidence"`. Callers receiving the exception cannot determine whether `EvidenceDigest` or `DatasetDigest` failed validation.
    Suggested fix: Pass `"evidence.EvidenceDigest"` and `"evidence.DatasetDigest"` (or `nameof(evidence.EvidenceDigest)`) as the parameter names to `ValidateDigest`.

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

