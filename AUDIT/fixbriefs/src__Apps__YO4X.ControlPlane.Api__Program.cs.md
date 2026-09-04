You are a fix agent on YO4X, a LIVE MetaTrader 5 / MQL5 algorithmic trading platform (.NET 10 backend, React frontend, an MQL5-to-C# transpiler, a deterministic backtest engine). An audit found defects in ONE file. Fix them.

THE ONLY FILE YOU MAY MODIFY:
  src/Apps/YO4X.ControlPlane.Api/Program.cs

Read that file completely first. You may read any other file for context, but you must not edit any other file, create files, delete files, or run commands.

FINDINGS TO FIX (2):

[1] [P1] `IPAddress.IsLoopback` check rejects IPv4 loopback connections on dual-stack listener during broker account linking
    Where:   src/Apps/YO4X.ControlPlane.Api/Program.cs:178
    Failure: When Kestrel listens on dual-stack sockets (`[::]`), local clients connecting via IPv4 (`127.0.0.1`) produce an IPv4-mapped IPv6 `RemoteIpAddress` (`::ffff:127.0.0.1`). In .NET, `IPAddress.IsLoopback` returns `false` for IPv4-mapped IPv6 addresses, causing legitimate local requests to `POST /v1/broker-accounts` to be rejected with HTTP 403 `LOCAL_CREDENTIAL_BOUNDARY_REQUIRES_LOOPBACK`.
    Suggested fix: Unmap IPv4-mapped IPv6 addresses before checking loopback: `IPAddress? ip = context.Connection.RemoteIpAddress; if (ip is null || !IPAddress.IsLoopback(ip.IsIPv4MappedToIPv6 ? ip.MapToIPv4() : ip))`.

[2] [P2] `ClassifySourceNetwork` misclassifies IPv4-mapped loopback connections as private in request metadata
    Where:   src/Apps/YO4X.ControlPlane.Api/Program.cs:577
    Failure: An IPv4 loopback connection arriving on a dual-stack socket (`::ffff:127.0.0.1`) fails the initial `IPAddress.IsLoopback` check. After being unmapped to IPv4 (`127.0.0.1`), execution falls through without re-checking loopback, hitting `bytes[0] == 127` which classifies the network as `"private"` instead of `"loopback"` in audit and request metadata.
    Suggested fix: Check `IPAddress.IsLoopback` again immediately after unmapping `address.MapToIPv4()`, or map to IPv4 before the initial `IPAddress.IsLoopback` check.

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

