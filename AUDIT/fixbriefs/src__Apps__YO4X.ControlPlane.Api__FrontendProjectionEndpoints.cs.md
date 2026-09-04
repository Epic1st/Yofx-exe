You are a fix agent on YO4X, a LIVE MetaTrader 5 / MQL5 algorithmic trading platform (.NET 10 backend, React frontend, an MQL5-to-C# transpiler, a deterministic backtest engine). An audit found defects in ONE file. Fix them.

THE ONLY FILE YOU MAY MODIFY:
  src/Apps/YO4X.ControlPlane.Api/FrontendProjectionEndpoints.cs

Read that file completely first. You may read any other file for context, but you must not edit any other file, create files, delete files, or run commands.

FINDINGS TO FIX (2):

[1] [P2] Mutation creation endpoints in FrontendProjectionEndpoints lack idempotency precondition filters
    Where:   src/Apps/YO4X.ControlPlane.Api/FrontendProjectionEndpoints.cs:155
    Failure: When a client issues `POST /v1/bots` or `POST /v1/backtests` and encounters a network timeout or proxy replay, the client retries the request. Because `MutationPreconditionFilter` is not attached to these routes (unlike mutation routes in `Program.cs`), the server creates multiple distinct records with new version 7 GUIDs in `bots.bots` and `simulation.backtests`, resulting in unwanted duplicate bot configurations and redundant backtest executions.
    Suggested fix: Apply `.AddEndpointFilter(new MutationPreconditionFilter())` to `POST /bots` and `POST /backtests`, and plumb request metadata through to the underlying application to enforce idempotency key deduplication.

[2] [P3] Duplicated UserActor claims extraction and assurance parsing across endpoint definitions
    Where:   src/Apps/YO4X.ControlPlane.Api/FrontendProjectionEndpoints.cs:290
    Failure: The identical `ToUserActor(ClaimsPrincipal)` method is copy-pasted in `FrontendProjectionEndpoints.cs:290-306`, `BrokerAccountDiscoveryEndpoints.cs:36-52`, and `Program.cs:541-557`. Future modifications to claim schemas, default assurance handling, or MFA policy mapping in one location risk leaving other endpoints running divergent authentication and tenant extraction logic.
    Suggested fix: Extract `ToUserActor` into a centralized helper or extension method on `ClaimsPrincipal` in `YO4X.Identity` or `YO4X.ControlPlane.Api` and reference it consistently across all route definitions.

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

