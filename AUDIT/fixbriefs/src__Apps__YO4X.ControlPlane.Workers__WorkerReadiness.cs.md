You are a fix agent on YO4X, a LIVE MetaTrader 5 / MQL5 algorithmic trading platform (.NET 10 backend, React frontend, an MQL5-to-C# transpiler, a deterministic backtest engine). An audit found defects in ONE file. Fix them.

THE ONLY FILE YOU MAY MODIFY:
  src/Apps/YO4X.ControlPlane.Workers/WorkerReadiness.cs

Read that file completely first. You may read any other file for context, but you must not edit any other file, create files, delete files, or run commands.

FINDINGS TO FIX (1):

[1] [P1] Liveness probe unconditionally reports healthy when worker workstreams are terminally stopped
    Where:   src/Apps/YO4X.ControlPlane.Workers/WorkerReadiness.cs:62
    Failure: When hosted background workstreams (`OutboxDispatch` and `ControlWork`) fail or encounter a fatal unconfirmed termination condition (`WorkerOperationTerminationUnconfirmedException`), their work loops exit and transition `WorkerReadiness` into `RequiredWorkstreamState.Stopped`. While `/health/startup` and `/health/ready` fail closed, the liveness endpoint `/health/live` invokes `GetLive()`, which unconditionally returns HTTP 200 (`Healthy: true`, `process_live`) as long as the ASP.NET Core web host is reachable. Container orchestrators (e.g. Kubernetes) evaluating liveness probes will never restart or replace the dead worker process, stranding the worker in a permanent zombie state where dispatching and control loops are inactive.
    Suggested fix: Synchronize on `_sync` in `WorkerReadiness.GetLive()` and check `AnyState(RequiredWorkstreamState.Stopped)`. If any required workstream is stopped, return an unhealthy snapshot (`Healthy = false`, code `"worker_stopped"`) so container liveness probes fail and trigger an orchestrator restart.

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

