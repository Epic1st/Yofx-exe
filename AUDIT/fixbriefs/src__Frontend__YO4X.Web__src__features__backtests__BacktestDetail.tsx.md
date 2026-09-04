You are a fix agent on YO4X, a LIVE MetaTrader 5 / MQL5 algorithmic trading platform (.NET 10 backend, React frontend, an MQL5-to-C# transpiler, a deterministic backtest engine). An audit found defects in ONE file. Fix them.

THE ONLY FILE YOU MAY MODIFY:
  src/Frontend/YO4X.Web/src/features/backtests/BacktestDetail.tsx

Read that file completely first. You may read any other file for context, but you must not edit any other file, create files, delete files, or run commands.

FINDINGS TO FIX (2):

[1] [P2] Equity curve horizontal projection ignores `sourceOrdinal`, distorting drawdown duration on decimated series
    Where:   src/Frontend/YO4X.Web/src/features/backtests/BacktestDetail.tsx:74-77
    Failure: Decimated equity curves include regular strided samples alongside a retained final sample (`sourceOrdinal = sampleCount - 1`), creating an irregular final sample delta (e.g. interval = 100 on 1050 samples leaves the last interval spanning only 49 samples). By computing `x = (index / lastIndex) * curveWidth` from array index rather than sample ordinal, `buildCurve` allocates the final 49-sample segment the exact same horizontal width as preceding 100-sample segments. This horizontally stretches the final segment by over 2x, misrepresenting the temporal progression and duration of drawdowns and recoveries.
    Suggested fix: Project the X coordinate proportionally to `point.sourceOrdinal` over `curve.sampleCount - 1` (or `lastPoint.sourceOrdinal - firstPoint.sourceOrdinal`) rather than the decimated array index.

[2] [P2] Missing polling lifecycle for running and queued backtests leaves detail and list views stale
    Where:   src/Frontend/YO4X.Web/src/features/backtests/BacktestDetail.tsx:136-139
    Failure: When viewing a backtest in `QUEUED` or `RUNNING` status, `BacktestDetail` performs only a single fetch on mount via `useResource`. There is no polling timer, interval cleanup, backoff, or terminal state stop condition (`COMPLETE`/`FAILED`). When an execution runner picks up the queued backtest and completes or fails the run, the page remains permanently stuck in the initial state ("Recorded, not started" or "Executing") without ever displaying final results or the equity curve until the user manually triggers a full browser reload.
    Suggested fix: Introduce a polling lifecycle hook in `BacktestDetail` that periodically refreshes the backtest state with backoff while in `QUEUED` or `RUNNING` status and automatically cancels when transitioning to `COMPLETE` or `FAILED` or on component unmount.

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

