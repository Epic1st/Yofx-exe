You are a fix agent on YO4X, a LIVE MetaTrader 5 / MQL5 algorithmic trading platform (.NET 10 backend, React frontend, an MQL5-to-C# transpiler, a deterministic backtest engine). An audit found defects in ONE file. Fix them.

THE ONLY FILE YOU MAY MODIFY:
  src/Tools/YO4X.Mt5.DemoExecutionTest/Program.cs

Read that file completely first. You may read any other file for context, but you must not edit any other file, create files, delete files, or run commands.

FINDINGS TO FIX (2):

[1] [P0] DemoExecutionTest accepts `--environment live` and executes real trades on live funded accounts
    Where:   src/Tools/YO4X.Mt5.DemoExecutionTest/Program.cs:41-43
    Failure: An operator or automation script runs `YO4X.Mt5.DemoExecutionTest --environment live --credential-key <live_key> --symbol EURUSD ...`. Because `Mt5NetApiDemoTradeClient.RequireDeclaredEnvironment` verifies that the declared environment matches what the broker reports, declaring `live` for a live account satisfies the environment check. `DemoExecutionTest` then places real market BUY orders (`0.01` lots) and pending stop orders (`0.01` lots) or benchmark cycles against a live funded account, risking real capital from a diagnostic tool.
    Suggested fix: Remove the `--environment live` option from `YO4X.Mt5.DemoExecutionTest/Program.cs` and hardcode `Mt5TradingEnvironment.Demo` so the tool is strictly prohibited from executing against live accounts.

[2] [P1] Unhandled failure during position lifecycle leaves test positions open and pending orders active on broker
    Where:   src/Tools/YO4X.Mt5.DemoExecutionTest/Program.cs:83-91
    Failure: In `DemoExecutionTest`, step [1] opens a market position ticket. If step [2] `ModifyAsync` fails (e.g. broker rejects stops due to minimum stop-distance rules) or step [3] `CloseAsync` fails, an exception is thrown to `Main` and the process exits. The open position is never closed. Similarly, if step [4] places a pending stop order and step [5] `CancelAsync` throws, the pending order remains active on the broker and can trigger into an unmonitored market position.
    Suggested fix: Wrap the test sequence in a `try/finally` block that attempts an emergency close on `opened` and cancel on `placed` if any intermediate step fails.

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

