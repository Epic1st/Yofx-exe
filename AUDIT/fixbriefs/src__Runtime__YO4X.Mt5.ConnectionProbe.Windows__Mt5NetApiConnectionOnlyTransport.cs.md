You are a fix agent on YO4X, a LIVE MetaTrader 5 / MQL5 algorithmic trading platform (.NET 10 backend, React frontend, an MQL5-to-C# transpiler, a deterministic backtest engine). An audit found defects in ONE file. Fix them.

THE ONLY FILE YOU MAY MODIFY:
  src/Runtime/YO4X.Mt5.ConnectionProbe.Windows/Mt5NetApiConnectionOnlyTransport.cs

Read that file completely first. You may read any other file for context, but you must not edit any other file, create files, delete files, or run commands.

FINDINGS TO FIX (2):

[1] [P1] Connection probe transport fabricates Demo environment without verifying broker account group
    Where:   src/Runtime/YO4X.Mt5.ConnectionProbe.Windows/Mt5NetApiConnectionOnlyTransport.cs:286-294
    Failure: `VaultBackedBrokerConnectionProbeExecutor` checks `connected.Environment != BrokerEnvironment.Demo` (line 147) to ensure real/funded accounts are not admitted by a demo connection probe. However, `Mt5NetApiConnectionOnlyTransport` hardcodes `BrokerEnvironment.Demo` without reading `Account.Type` from the live session (unlike `Mt5NetApiDemoTradeClient` and `Mt5NetApiAccountReader`). When an operator configures credentials for a live/real account, the probe connects to the live broker server, fabricates a demo environment observation, and succeeds, bypassing downstream environment protection.
    Suggested fix: Read the session's account group string from `apiType.GetProperty("Account")`, determine if the server reports a demo account, and populate `Mt5ConnectionOnlyObservation.Environment` with the actual verified environment.

[2] [P2] Probe transport performs blocking synchronous connect without timeout configuration or in-flight cancellation
    Where:   src/Runtime/YO4X.Mt5.ConnectionProbe.Windows/Mt5NetApiConnectionOnlyTransport.cs:252-270
    Failure: `ConnectAndDisconnectAsync` wraps synchronous execution inside `Task.FromResult`. It does not configure the vendor `ConnectTimeout` field on `MT5API` and does not check `cancellationToken` once `Connect()` begins. If the broker host is unresponsive or blackholed, the thread blocks indefinitely in vendor socket connection routines holding the plaintext `password` string in its stack frame and keeping the DPAPI `LocalMt5Credential` open until the external supervisor forcefully terminates the process.
    Suggested fix: Expose and configure `SetConnectTimeout` on `IMt5NetApiConnectionClient`, and execute connection attempts with cancellation support that aborts and disposes the client when the request deadline expires.

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

