You are a fix agent on YO4X, a LIVE MetaTrader 5 / MQL5 algorithmic trading platform (.NET 10 backend, React frontend, an MQL5-to-C# transpiler, a deterministic backtest engine). An audit found defects in ONE file. Fix them.

THE ONLY FILE YOU MAY MODIFY:
  src/Runtime/YO4X.Mql5.CodeGen/Mql5GeneratorRun.cs

Read that file completely first. You may read any other file for context, but you must not edit any other file, create files, delete files, or run commands.

FINDINGS TO FIX (5):

[1] [P1] Nested class out-of-line method definitions fail to resolve and are dropped
    Where:   src/Runtime/YO4X.Mql5.CodeGen/Mql5GeneratorRun.cs:75-81
    Failure: When an out-of-line method is defined for a nested type (e.g. `void COuter::CInner::Process()`), `SplitQualified` splits on the first `::`, assigning owner `"COuter"` and member `"CInner::Process"`. The method is stored in `_outOfLineDefinitions["COuter"]`. When `CInner` is emitted, its lookup for `_outOfLineDefinitions.TryGetValue("CInner", ...)` fails, and `OutOfLineBody` returns `null`. The body of `Process` is never emitted.
    Suggested fix: Split on the last `::` via `LastIndexOf("::")` so that owner is `"COuter::CInner"` and member is `"Process"`, matching the nested type's qualified name.

[2] [P1] Nested type and enum registration overwrites lookup entries keyed by unqualified names
    Where:   src/Runtime/YO4X.Mql5.CodeGen/Mql5GeneratorRun.cs:406-414
    Failure: When a module defines a top-level type or enum `Config` and a class `TradeManager` containing a nested struct or enum also named `Config`, `RegisterType` registers the top-level type under key `"Config"` and then unconditionally overwrites `_typeNames["Config"]` with `"TradeManager.Config"` (and overwrites `_typeDeclarations["Config"]` with the nested declaration). Any top-level variable or parameter declared as `Config` is then emitted with C# type `TradeManager.Config` instead of `Config`, and type resolution uses the wrong AST node.
    Suggested fix: Key `_typeNames` and `_typeDeclarations` by fully qualified or scoped names, or maintain lexical scope hierarchy rather than overwriting unqualified keys.

[3] [P1] Static local variable field generator produces invalid C# identifier names for out-of-line functions
    Where:   src/Runtime/YO4X.Mql5.CodeGen/Mql5GeneratorRun.cs:481-482
    Failure: When an out-of-line member function definition such as `void CTrade::Execute() { static int count = 0; }` contains a static local variable, `functionName` is `"CTrade::Execute"`. `StaticFieldName` produces `"__static_CTrade::Execute_count"`. When emitted on the strategy class, `private int __static_CTrade::Execute_count;` contains `::`, which is invalid C# identifier syntax and causes Roslyn compilation failure (`CS1003` / `CS1001`).
    Suggested fix: Sanitize `functionName` in `StaticFieldName` by replacing `::` and invalid identifier characters with underscores (e.g. `functionName.Replace("::", "_", StringComparison.Ordinal)`).

[4] [P1] Static local variable hoisting collides and produces duplicate field declarations for overloads and inner blocks
    Where:   src/Runtime/YO4X.Mql5.CodeGen/Mql5GeneratorRun.cs:430-435
    Failure: If two overloaded functions (e.g. `void Log(string msg)` and `void Log(int code)`) both declare `static int counter;`, or if a single function contains static locals with the same variable name in two distinct nested blocks, both entries produce the identical field name `"__static_Log_counter"`. `_staticLocals` appends both entries, and the strategy class emits duplicate field definitions `private int __static_Log_counter;`, causing C# compilation error `CS0102`.
    Suggested fix: Include unique source position offsets (line/column) or a monotonic sequence counter in hoisted static local field names.

[5] [P1] Static local variables declared inside inline class methods are never collected
    Where:   src/Runtime/YO4X.Mql5.CodeGen/Mql5GeneratorRun.cs:373-379
    Failure: In `BuildLookups`, static locals are only collected by traversing `_module.Functions` (file-scope and out-of-line functions). Methods defined inline within `_module.Types` (`Mql5IrTypeDeclaration.Methods`) are never inspected. Any `static` local variable inside an inline class method is omitted from `_staticLocals`, no hoisted field is created on the strategy, and the static variable loses persistence between invocations.
    Suggested fix: Recursively traverse `_module.Types` and their `Methods` during `BuildLookups` to collect static local declarations.

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

