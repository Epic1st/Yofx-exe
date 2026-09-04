You are a fix agent on YO4X, a LIVE MetaTrader 5 / MQL5 algorithmic trading platform (.NET 10 backend, React frontend, an MQL5-to-C# transpiler, a deterministic backtest engine). An audit found defects in ONE file. Fix them.

THE ONLY FILE YOU MAY MODIFY:
  src/BuildingBlocks/YO4X.Api/ApiFoundation.cs

Read that file completely first. You may read any other file for context, but you must not edit any other file, create files, delete files, or run commands.

FINDINGS TO FIX (2):

[1] [P2] Middleware ordering in `UseYo4xApiFoundation` strips security headers and `X-Correlation-Id` on exception responses
    Where:   src/BuildingBlocks/YO4X.Api/ApiFoundation.cs:67
    Failure: When an endpoint throws an unhandled exception or a mapped domain/not-found/conflict exception, execution unwinds to `app.UseExceptionHandler()`. In ASP.NET Core, `ExceptionHandlerMiddleware` invokes `HttpResponse.Clear()`, which resets the response and wipes all headers set prior to the exception (including `X-Correlation-Id` added by `CorrelationIdMiddleware`). Because the security headers middleware is registered *after* `app.UseExceptionHandler()`, it is bypassed on the error path. As a result, problem responses (HTTP 404/409/422/500/503) are returned to clients without `Cache-Control: no-store`, `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`, `Content-Security-Policy`, and the `X-Correlation-Id` HTTP header.
    Suggested fix: Register security headers and correlation ID using `HttpResponse.OnStarting` or place the security headers middleware before `app.UseExceptionHandler()` and re-apply the correlation header inside `Yo4xExceptionHandler`.

[2] [P2] `Yo4xExceptionHandler` classifies client-aborted requests as 500 `INTERNAL_ERROR` and logs unhandled exceptions
    Where:   src/BuildingBlocks/YO4X.Api/ApiFoundation.cs:134
    Failure: When a client aborts a connection during request execution, an `OperationCanceledException` is thrown. Because `Yo4xExceptionHandler` does not check `httpContext.RequestAborted.IsCancellationRequested` or handle `OperationCanceledException`, it falls through to the wildcard `_ => StatusCodes.Status500InternalServerError`. This logs an unhandled error log on normal client cancellations and attempts to write a 500 problem response to an aborted HTTP connection.
    Suggested fix: Check `if (exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested) return false;` or return early without logging when the request is aborted.

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

