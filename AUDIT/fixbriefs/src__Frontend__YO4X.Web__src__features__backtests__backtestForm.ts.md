You are a fix agent on YO4X, a LIVE MetaTrader 5 / MQL5 algorithmic trading platform (.NET 10 backend, React frontend, an MQL5-to-C# transpiler, a deterministic backtest engine). An audit found defects in ONE file. Fix them.

THE ONLY FILE YOU MAY MODIFY:
  src/Frontend/YO4X.Web/src/features/backtests/backtestForm.ts

Read that file completely first. You may read any other file for context, but you must not edit any other file, create files, delete files, or run commands.

FINDINGS TO FIX (5):

[1] [P1] `formatColourValue` emits CSS hex colors rejected by backend `IsColour` validator
    Where:   src/Frontend/YO4X.Web/src/features/backtests/backtestForm.ts:106-119
    Failure: When a strategy declares a color input with a hex default (e.g. `#00FF7F`), `parseColourDefault` accepts it and `editorKindFor` assigns it a color picker (`COLOUR`). When the user selects a new color (e.g. `#0080ff`), `formatColourValue` evaluates `!colourLiteralPattern.test(defaultValue.trim())` as true and returns `#0080ff`. Client validation (`validateInputValue:295-298`) passes it. However, backend `PostgresFrontendProjections.ValidateInputValue` validates colors via `IsColour` (`PostgresFrontendProjections.cs:2716-2752`), which only accepts `C'r,g,b'`, MQL5 identifiers (`clrRed`), `0x...` hex integers, and unsigned decimals. `IsColour("#0080ff")` returns `false`, causing the server to reject the backtest creation request with HTTP 422 `VALUE_NOT_A_COLOUR`.
    Suggested fix: In `formatColourValue`, always serialize picked colors to MQL5 `C'r,g,b'` literals (e.g. `C'0,128,255'`) or numeric literals regardless of whether the source default was written as `C'r,g,b'`.

[2] [P2] `validateInputValue` for `COLOUR` inputs edited as text does not validate color syntax
    Where:   src/Frontend/YO4X.Web/src/features/backtests/backtestForm.ts:295-298
    Failure: When a color input default cannot be shown in a color picker (e.g. `clrTomato` or `0x00FF00`), `editorKindFor` falls back to a text box (`TEXT`). If the user enters an invalid color string (e.g. `clrInvalid!`, `not_a_color`, or a string with illegal symbols), `validateInputValue` falls through to `TEXT` and checks only `submitted.length > 2_000`, returning `null`. The backend `IsColour` check rejects the value upon submission with HTTP 422 `VALUE_NOT_A_COLOUR`.
    Suggested fix: Implement client-side validation for text-edited `COLOUR` inputs matching server `IsColour` rules (validating `C'r,g,b'`, valid MQL5 identifiers `/^[A-Za-z_][A-Za-z0-9_]{0,63}$/u`, `0x` hex integers, or unsigned decimals).

[3] [P2] `validateInputValue` permits hexadecimal and non-decimal literals for `REAL` inputs that backend rejects
    Where:   src/Frontend/YO4X.Web/src/features/backtests/backtestForm.ts:272-275
    Failure: In JavaScript, `Number("0x10")` returns `16` (finite), `Number("0b101")` returns `5`, and `Number("0o77")` returns `63`. If a user enters `"0x10"` for a `REAL` (`double` or `float`) input, `validateInputValue` returns `null` (valid). On the server, `PostgresFrontendProjections.ValidateInputValue` (`PostgresFrontendProjections.cs:2607-2616`) parses the input using `double.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out _)`. `NumberStyles.Float` rejects hexadecimal, binary, and octal notations, causing the server to reject the request with HTTP 422 `VALUE_NOT_A_REAL_NUMBER`.
    Suggested fix: Validate `REAL` inputs against a decimal floating-point regex (`/^[+-]?(?:[0-9]+(?:\.[0-9]*)?|\.[0-9]+)(?:[eE][+-]?[0-9]+)?$/u`) before checking `Number.isFinite(Number(trimmed))`.

[4] [P3] Frontend input length limit (2,000 characters) diverges from backend limit (4,000 characters)
    Where:   src/Frontend/YO4X.Web/src/features/backtests/backtestForm.ts:298
    Failure: The backend `MaximumInputValueLength` in `PostgresFrontendProjections.cs:64` is 4,000 characters, matching `simulation.backtest_inputs.value` table constraint `check (length(value) <= 4000)`. However, `backtestForm.ts` caps inputs at 2,000 characters. If a strategy requires long textual inputs (such as serialized configurations or parameters between 2,001 and 4,000 characters), the client blocks the user with `"That value is too long to record."` even though the service and database allow up to 4,000 characters.
    Suggested fix: Update the character limit check from `2_000` to `4_000` to match `MaximumInputValueLength`.

[5] [P3] `validateFormValues` does not trim `strategyId` before empty check
    Where:   src/Frontend/YO4X.Web/src/features/backtests/backtestForm.ts:326-328
    Failure: While `symbol` and `timeframe` validation check `values.symbol.trim() === ''`, `strategyId` checks `values.strategyId === ''` without trimming. If `strategyId` contains whitespace (e.g. `'   '`), `validateFormValues` passes validation on the client instead of flagging `errors.strategyId`.
    Suggested fix: Check `values.strategyId.trim() === ''` or validate against a standard UUID format regex.

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

