You are a fix agent on YO4X, a LIVE MetaTrader 5 / MQL5 algorithmic trading platform (.NET 10 backend, React frontend, an MQL5-to-C# transpiler, a deterministic backtest engine). An audit found defects in ONE file. Fix them.

THE ONLY FILE YOU MAY MODIFY:
  src/Runtime/YO4X.Mql5.CodeGen/Mql5GeneratorRun.Declarations.cs

Read that file completely first. You may read any other file for context, but you must not edit any other file, create files, delete files, or run commands.

FINDINGS TO FIX (7):

[1] [P1] Generic base class lookup in EmitTypeDeclaration shadows runtime and owner fields
    Where:   src/Runtime/YO4X.Mql5.CodeGen/Mql5GeneratorRun.Declarations.cs:440-442
    Failure: When an MQL5 class inherits from a generic template base (e.g. `class MyClass : BaseTemplate<double>`), `declaration.BaseTypeName` is `"BaseTemplate<double>"`. `_typeNames` contains bare type names (`"BaseTemplate"`), so `_typeNames.ContainsKey(declaration.BaseTypeName)` evaluates to `false` (unlike line 410, which uses `BareTypeName(declaration.BaseTypeName)`). `inheritsRuntime` evaluates to `false`, causing `EmitTypeDeclaration` to re-declare `internal IMql5Runtime Rt;` and `internal Strategy __owner;` on the derived class. Object initializers populate the derived fields while leaving `base.Rt` as `null`, so any inherited base class method accessing `Rt` to invoke MQL5 built-ins throws a `NullReferenceException`.
    Suggested fix: Change line 441 to check `_typeNames.ContainsKey(BareTypeName(declaration.BaseTypeName))`.

[2] [P1] OutOfLineBody matches overloads by parameter count only, dropping distinct signatures
    Where:   src/Runtime/YO4X.Mql5.CodeGen/Mql5GeneratorRun.Declarations.cs:365-373
    Failure: When an MQL5 class declares overloaded methods with the same parameter count but different parameter types (e.g. `void SetValue(int x);` and `void SetValue(string x);`), `OutOfLineBody` returns the first candidate where `candidate.Parameters.Count == method.Parameters.Count`. Both `SetValue(int)` and `SetValue(string)` receive the body of whichever definition appeared first at file scope. The second overload's body is dropped, and the wrong method body is emitted under the mismatched signature, resulting in C# compilation errors or invalid runtime execution.
    Suggested fix: Match candidate parameters against method parameter types in addition to matching parameter count.

[3] [P1] Prefix matching in EmitMethodCore corrupts static local symbol rewriting across functions
    Where:   src/Runtime/YO4X.Mql5.CodeGen/Mql5GeneratorRun.Declarations.cs:975-983
    Failure: When function `Calc` contains a static local `A_count` (field `__static_Calc_A_count`), and another function `Calc_A` declares static local `count` (field `__static_Calc_A_count`), or if function `Calc` references a variable named `count`, `local.FieldName.StartsWith("__static_Calc_")` matches `__static_Calc_A_count` belonging to `Calc_A`. In `Calc`, `_staticLocalNames["count"]` is set to `__static_Calc_A_count`. Expressions inside `Calc` that read or write `count` are redirected to mutate `Calc_A`'s static local state.
    Suggested fix: Associate hoisted static locals directly with their declaring function identity or exact qualified symbol instead of loose string prefix matching on flattened field names.

[4] [P1] Static local structures and class instances are not instantiated in constructor
    Where:   src/Runtime/YO4X.Mql5.CodeGen/Mql5GeneratorRun.Declarations.cs:821-845
    Failure: In MQL5, declaring an uninitialised static local structure or class (such as `static CTrade trade;` or `static MqlTradeRequest req;`) inside a function provides zeroed and default-constructed storage. Unlike `EmitFileScopeInitializer` (lines 801–813), `EmitStaticLocalInitializer` lacks a check for `resolved.Kind is Mql5ResolvedTypeKind.Structure or Mql5ResolvedTypeKind.Class`. As a result, the generated constructor emits no assignment for `__static_function_trade`, leaving the hoisted field as `null`. When the strategy executes and calls `trade.Buy(...)`, a `NullReferenceException` is thrown on the first trade invocation.
    Suggested fix: Add a structure/class resolution branch in `EmitStaticLocalInitializer` matching `EmitFileScopeInitializer` (lines 800–813) to instantiate non-pointer structure and class static locals using `new T(ConstructionArguments) { ... }`.

[5] [P1] Struct and class member fields omit default initialization for object, structure, and string types
    Where:   src/Runtime/YO4X.Mql5.CodeGen/Mql5GeneratorRun.Declarations.cs:548-575
    Failure: When an MQL5 struct or class defines a non-pointer member field of class, runtime structure, or string type without an explicit inline initializer (e.g. `CTrade m_trade;`, `MqlTradeRequest m_req;`, or `string m_comment;` inside `class CTrader`), `EmitField` sets `initializer` to `null`. In emitted C#, `internal Mql5Trade m_trade;` and `internal string m_comment;` are emitted with no initialization (defaulting to `null`). In MQL5, member objects are constructed and strings default to `""`. Calling `m_trade.Buy(...)` or evaluating string properties on the instance throws a `NullReferenceException` at runtime.
    Suggested fix: When `field.Initializer is null` and the field is not an array, check if `field.Type` is a non-pointer structure/class or string scalar, and assign default construction (`new T(...)`) or `string.Empty` to `initializer`.

[6] [P1] Transpiled module types leave `_runtime` and `__owner` references as `null!` in arrays and struct locals
    Where:   src/Runtime/YO4X.Mql5.CodeGen/Mql5GeneratorRun.Declarations.cs:455-459
    Failure: Emitted MQL5 module types (structs and classes) declare `internal IMql5Runtime _runtime = null!;` and `internal StrategyType __owner = null!;` to route built-in function calls (`MathAbs`, `SymbolInfoDouble`) and global variable access back to the strategy. The transpiler emits object initializers (`{ _runtime = _runtime, __owner = this }`) only for single scalar constructions. When a strategy allocates an array of module types (e.g. `CTrade trades[5];` or `MyStruct matrix[3][3];` via `ArrayCreation` / `NewArray2<T>`), or declares an unassigned struct local or field, every element/instance is left with `_runtime == null` and `__owner == null`. When the strategy subsequently invokes any method on an array element (e.g. `trades[0].PositionOpen(...)`), dereferencing `_runtime` or `__owner` throws a runtime `NullReferenceException`.
    Suggested fix: In array allocation helpers (`ArrayCreation`, `NewArray2`, `NewArray3`) and struct initialization routines, populate each element's `_runtime` and `__owner` fields upon instantiation.

[7] [P2] Non-constant parameter default values emitted via ConvertTo fail C# compile-time constant rule
    Where:   src/Runtime/YO4X.Mql5.CodeGen/Mql5GeneratorRun.Declarations.cs:1034-1038
    Failure: When an optional string parameter in MQL5 has a numeric `0` or `NULL` default (e.g. `void Log(string msg = NULL)`), `TryConstantText` returns `true`, and `ConvertTo` converts `0` to `"Mql5Ops.ToText(0)"`. The emitted signature becomes `public void Log(string msg = Mql5Ops.ToText(0))`. In C#, default parameter values must be compile-time constants; emitting method invocations in parameter defaults causes compilation failure with `CS1736`.
    Suggested fix: For string parameter defaults receiving `NULL` or `0`, emit constant literals `""` or `null` directly instead of calling conversion helper methods.

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

