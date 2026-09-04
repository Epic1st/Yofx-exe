You are a fix agent on YO4X, a LIVE MetaTrader 5 / MQL5 algorithmic trading platform (.NET 10 backend, React frontend, an MQL5-to-C# transpiler, a deterministic backtest engine). An audit found defects in ONE file. Fix them.

THE ONLY FILE YOU MAY MODIFY:
  src/Runtime/YO4X.Mql5.CodeGen/Mql5GeneratorRun.Calls.cs

Read that file completely first. You may read any other file for context, but you must not edit any other file, create files, delete files, or run commands.

FINDINGS TO FIX (4):

[1] [P1] Member, sibling, and qualified calls emit plain arguments without `ref` for user methods with by-reference parameters
    Where:   src/Runtime/YO4X.Mql5.CodeGen/Mql5GeneratorRun.Calls.cs:57-60
    Failure: When a user class declares a method with a mutable reference parameter (e.g. `void Compute(double price, double &result)`), `Mql5GeneratorRun.Declarations.cs:1020-1021` emits the C# method signature as `public void Compute(double price, ref double result)`. However, `EmitMemberCall` (lines 57-60 and 62-72), sibling method calls in `EmitNamedCall:135`, and qualified calls in `EmitCall:25` route arguments through `PlainArguments` or `RuntimeArgument`, emitting `target.Compute(price, result)` without `ref`. In C#, passing an argument to a `ref` parameter without the `ref` keyword produces compilation error `CS1620: Argument 2 must be passed with the 'ref' keyword`.
    Suggested fix: For user-defined member, sibling, and qualified method calls, look up the target method declaration, test each parameter with `ParameterIsByRef`, and prepend `"ref "` with `ReferenceArgument(...)` for by-reference arguments instead of delegating to `PlainArguments`.

[2] [P1] Module-declared enumeration functional casts are omitted in `EmitNamedCall` and rejected as uncallable
    Where:   src/Runtime/YO4X.Mql5.CodeGen/Mql5GeneratorRun.Calls.cs:156-174
    Failure: In MQL5, functional conversion syntax `MyEnum(value)` is standard for casting expressions to user-defined enumeration types. In `EmitNamedCall`, lines 156-174 only handle built-in enum names starting with `ENUM_` or catalogued in `Mql5BuiltinConstants.EnumNames`. User-declared enums registered in `_enumTypeNames` are never matched by any branch before falling through to line 189, where `EmitNamedCall` emits diagnostic `Mql5CodeGenDiagnosticCodes.UnsupportedCall` ("The call to 'MyEnum' resolved to nothing callable.") and replaces the expression with poison token `__mql5_unsupported`.
    Suggested fix: In `EmitNamedCall`, check `_enumTypeNames.TryGetValue(name.Name, out string? enumType)`, ensure `call.Arguments.Count == 1`, and emit an explicit cast `unchecked((` + enumType + `)(` + Expr(call.Arguments[0], depth + 1) + `))`.

[3] [P1] `EmitModuleCall` selects overloads by argument count alone, converting arguments to wrong parameter types
    Where:   src/Runtime/YO4X.Mql5.CodeGen/Mql5GeneratorRun.Calls.cs:207-216
    Failure: When a module declares multiple overloads of the same function with identical arity but different parameter types (e.g. `void Trace(int code)` and `void Trace(string msg)`), `EmitModuleCall` unconditionally selects the first candidate in declaration order. For `Trace("tick")`, candidate 0 (`Trace(int)`) is selected, and argument `"tick"` is coerced to `int` via `ConvertTo(int, string, ...)` -> `unchecked((int)(Mql5Ops.ToLong("tick")))` (`0`). This silently compiles into a call to `Trace(0)` instead of `Trace("tick")`, discarding the string argument and invoking the wrong overload.
    Suggested fix: Select the overload candidate by matching actual argument types against candidate parameter types, or retrieve the resolved function symbol from `_model`.

[4] [P3] Dead method `TryAgreeOnReferences` left uncalled in `Mql5GeneratorRun.Calls.cs`
    Where:   src/Runtime/YO4X.Mql5.CodeGen/Mql5GeneratorRun.Calls.cs:453-483
    Failure: `TryAgreeOnReferences` is a 30-line private static helper that was superseded by `Mql5ClrTypes.RuntimeParameterKeyword` and is never called anywhere in the codebase.
    Suggested fix: Delete the unused private method `TryAgreeOnReferences`.

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

