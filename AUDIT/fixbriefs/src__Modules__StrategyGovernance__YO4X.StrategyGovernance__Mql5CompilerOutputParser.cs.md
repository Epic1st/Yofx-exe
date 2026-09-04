You are a fix agent on YO4X, a LIVE MetaTrader 5 / MQL5 algorithmic trading platform (.NET 10 backend, React frontend, an MQL5-to-C# transpiler, a deterministic backtest engine). An audit found defects in ONE file. Fix them.

THE ONLY FILE YOU MAY MODIFY:
  src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5CompilerOutputParser.cs

Read that file completely first. You may read any other file for context, but you must not edit any other file, create files, delete files, or run commands.

FINDINGS TO FIX (1):

[1] [P3] Compiler output preflight byte limit permits oversized string allocations before character count check
    Where:   src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5CompilerOutputParser.cs:133-134
    Failure: `MaximumRecordUtf8Bytes` is set to `262,144` bytes (4x `MaximumRecordCharacters` of `65,536`). An ASCII record of 150,000 bytes passes `PreflightRecords` and forces `Parse` to materialize a 150,000-character string via `StrictUtf8.GetString` before rejecting it at `line.Length > MaximumRecordCharacters` (line 90), bypassing early memory-bounding guarantees during preflight.
    Suggested fix: Tighten preflight record byte bounds or check decoded buffer lengths before full string instantiation.

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

