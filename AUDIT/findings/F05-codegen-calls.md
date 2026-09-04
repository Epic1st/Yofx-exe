---
agent_id: F05
lane: codegen-calls
scope:
  - src/Runtime/YO4X.Mql5.CodeGen/Mql5GeneratorRun.Calls.cs
status: COMPLETE
generated: 2026-08-29T11:30:00Z
counts: { P0: 0, P1: 3, P2: 0, P3: 1 }
---

# F05 — codegen-calls

## Scope audited
- `src/Runtime/YO4X.Mql5.CodeGen/Mql5GeneratorRun.Calls.cs` (524 lines) — MQL5 call emission (built-ins, module functions, constructor calls, member calls, unqualified/qualified calls, conversions, parameter type coercion, and reference argument checking).

## Verdict
The call emission logic handles runtime built-ins and single module function calls cleanly, but contains serious semantic flaws in user-defined method calls, user enum conversions, and module function overload selection. In member calls, sibling calls, and qualified calls on user classes/structs, by-reference parameter qualifiers (`ref`) are completely dropped, emitting invalid C# that fails Roslyn compilation or loses writebacks. Furthermore, module function overload selection matches purely on argument count rather than types, causing arguments to be coerced into incorrect types and routed to the wrong overload.

## Findings

### [P1] Member, sibling, and qualified calls emit plain arguments without `ref` for user methods with by-reference parameters
- **Where:** `src/Runtime/YO4X.Mql5.CodeGen/Mql5GeneratorRun.Calls.cs:57-60`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
        if (libraryType is null)
        {
            return target + "." + method + "(" + PlainArguments(call.Arguments, depth) + ")";
        }
  ```
- **Failure:** When a user class declares a method with a mutable reference parameter (e.g. `void Compute(double price, double &result)`), `Mql5GeneratorRun.Declarations.cs:1020-1021` emits the C# method signature as `public void Compute(double price, ref double result)`. However, `EmitMemberCall` (lines 57-60 and 62-72), sibling method calls in `EmitNamedCall:135`, and qualified calls in `EmitCall:25` route arguments through `PlainArguments` or `RuntimeArgument`, emitting `target.Compute(price, result)` without `ref`. In C#, passing an argument to a `ref` parameter without the `ref` keyword produces compilation error `CS1620: Argument 2 must be passed with the 'ref' keyword`.
- **Fix:** For user-defined member, sibling, and qualified method calls, look up the target method declaration, test each parameter with `ParameterIsByRef`, and prepend `"ref "` with `ReferenceArgument(...)` for by-reference arguments instead of delegating to `PlainArguments`.

### [P1] Module-declared enumeration functional casts are omitted in `EmitNamedCall` and rejected as uncallable
- **Where:** `src/Runtime/YO4X.Mql5.CodeGen/Mql5GeneratorRun.Calls.cs:156-174`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
        if (!Mql5BuiltinCatalog.IsKnown(name.Name)
            && (name.Name.StartsWith("ENUM_", StringComparison.Ordinal)
                || Mql5BuiltinConstants.EnumNames.Contains(name.Name)))
        {
            if (call.Arguments.Count != 1)
            {
                return Fail(
                    Mql5CodeGenDiagnosticCodes.UnsupportedCall,
                    "A conversion to '" + name.Name + "' takes exactly one argument.",
                    call.Line,
                    call.Column);
            }

            return Coerce(
                Mql5ResolvedType.ForScalar(Mql5IrScalarKind.Whole32),
                TypeOf(call.Arguments[0]),
                Expr(call.Arguments[0], depth + 1),
                explicitCast: true);
        }
  ```
- **Failure:** In MQL5, functional conversion syntax `MyEnum(value)` is standard for casting expressions to user-defined enumeration types. In `EmitNamedCall`, lines 156-174 only handle built-in enum names starting with `ENUM_` or catalogued in `Mql5BuiltinConstants.EnumNames`. User-declared enums registered in `_enumTypeNames` are never matched by any branch before falling through to line 189, where `EmitNamedCall` emits diagnostic `Mql5CodeGenDiagnosticCodes.UnsupportedCall` ("The call to 'MyEnum' resolved to nothing callable.") and replaces the expression with poison token `__mql5_unsupported`.
- **Fix:** In `EmitNamedCall`, check `_enumTypeNames.TryGetValue(name.Name, out string? enumType)`, ensure `call.Arguments.Count == 1`, and emit an explicit cast `unchecked((` + enumType + `)(` + Expr(call.Arguments[0], depth + 1) + `))`.

### [P1] `EmitModuleCall` selects overloads by argument count alone, converting arguments to wrong parameter types
- **Where:** `src/Runtime/YO4X.Mql5.CodeGen/Mql5GeneratorRun.Calls.cs:207-216`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
        Mql5IrFunction? selected = null;
        foreach (Mql5IrFunction candidate in overloads)
        {
            if (call.Arguments.Count >= RequiredParameterCount(candidate)
                && call.Arguments.Count <= candidate.Parameters.Count)
            {
                selected = candidate;
                break;
            }
        }
  ```
- **Failure:** When a module declares multiple overloads of the same function with identical arity but different parameter types (e.g. `void Trace(int code)` and `void Trace(string msg)`), `EmitModuleCall` unconditionally selects the first candidate in declaration order. For `Trace("tick")`, candidate 0 (`Trace(int)`) is selected, and argument `"tick"` is coerced to `int` via `ConvertTo(int, string, ...)` -> `unchecked((int)(Mql5Ops.ToLong("tick")))` (`0`). This silently compiles into a call to `Trace(0)` instead of `Trace("tick")`, discarding the string argument and invoking the wrong overload.
- **Fix:** Select the overload candidate by matching actual argument types against candidate parameter types, or retrieve the resolved function symbol from `_model`.

### [P3] Dead method `TryAgreeOnReferences` left uncalled in `Mql5GeneratorRun.Calls.cs`
- **Where:** `src/Runtime/YO4X.Mql5.CodeGen/Mql5GeneratorRun.Calls.cs:453-483`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
    private static bool TryAgreeOnReferences(
        List<Mql5BuiltinSignature> matching,
        int argumentCount,
        out bool[] byReference)
    {
        byReference = new bool[argumentCount];
  ```
- **Failure:** `TryAgreeOnReferences` is a 30-line private static helper that was superseded by `Mql5ClrTypes.RuntimeParameterKeyword` and is never called anywhere in the codebase.
- **Fix:** Delete the unused private method `TryAgreeOnReferences`.

## Referrals
None.

## Coverage gaps
- `src/Runtime/YO4X.Mql5.CodeGen/Mql5GeneratorRun.Calls.cs:114-121` — Untested diagnostic branch when a scalar conversion is invoked with more or fewer than 1 argument (e.g. `int(a, b)`).
- `src/Runtime/YO4X.Mql5.CodeGen/Mql5GeneratorRun.Calls.cs:363-373` — Untested emission branch for built-in calls classified as `Mql5BuiltinSupport.Unsupported`, verifying that `Mql5CodeGenDiagnosticCodes.RuntimeGatedBuiltin` informational diagnostic is properly recorded while still emitting the runtime invocation.
- `src/Runtime/YO4X.Mql5.CodeGen/Mql5GeneratorRun.Calls.cs:500-508` — Untested diagnostic branch in `ReferenceArgument` when a non-addressable expression (such as binary expression `a + b` or literal `42`) is passed to a `ref` parameter.


--- agy: gemini-3.7-flash-high | effort=high | mode=plan (read-only) | 154.5s | 326955 tok | id=f1310402-ff22-4ce6-b0c5-0f1623fdf090
