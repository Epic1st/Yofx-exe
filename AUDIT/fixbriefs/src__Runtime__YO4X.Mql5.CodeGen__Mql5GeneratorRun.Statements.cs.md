You are a fix agent on YO4X, a LIVE MetaTrader 5 / MQL5 algorithmic trading platform (.NET 10 backend, React frontend, an MQL5-to-C# transpiler, a deterministic backtest engine). An audit found defects in ONE file. Fix them.

THE ONLY FILE YOU MAY MODIFY:
  src/Runtime/YO4X.Mql5.CodeGen/Mql5GeneratorRun.Statements.cs

Read that file completely first. You may read any other file for context, but you must not edit any other file, create files, delete files, or run commands.

FINDINGS TO FIX (2):

[1] [P1] Trailing empty switch section emits orphan case label before switch closing brace causing CS8070
    Where:   src/Runtime/YO4X.Mql5.CodeGen/Mql5GeneratorRun.Statements.cs:352-355
    Failure: When an MQL5 switch statement ends with an empty section (such as `switch(x) { case 1: break; case 2: }` or `switch(x) { case 1: break; default: }`), `EmitSwitch` outputs the label (`case 2:` or `default:`) and hits `continue;`. The loop completes and closes the switch with `}`, emitting `case 2:\n}` without any statement in the final section. In C#, a switch label cannot immediately precede the closing brace without a statement list, causing Roslyn compilation failure `CS8070: Control cannot fall out of switch from final case label`.
    Suggested fix: When `section.Statements.Count == 0` and `position == selection.Sections.Count - 1`, emit a braced block containing `break;` (`{ break; }`) so the terminal switch label has a valid terminating statement before the switch block closes.

[2] [P1] Void function return statement with expression silently drops expression side effects
    Where:   src/Runtime/YO4X.Mql5.CodeGen/Mql5GeneratorRun.Statements.cs:245-249
    Failure: In MQL5 (like C++), a `void` function may return a `void` expression (for example, `void LogAndExit() { return Print("Exiting"); }` or `void Cleanup() { return ReleaseHandles(); }`). When `_currentReturnType.Scalar == Mql5IrScalarKind.Void` and `statement.Value` is non-null, `EmitReturn` emits only `return;` without evaluating or emitting `statement.Value`. Any function calls, side effects, or resource cleanups in `statement.Value` are silently dropped.
    Suggested fix: When `_currentReturnType.Scalar == Mql5IrScalarKind.Void` and `statement.Value is not null`, emit `statement.Value` as an expression statement before emitting `return;`.

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

