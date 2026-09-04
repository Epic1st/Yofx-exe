---
agent_id: F03
lane: codegen-declarations
scope:
  - src/Runtime/YO4X.Mql5.CodeGen/Mql5GeneratorRun.Declarations.cs
status: COMPLETE
generated: 2026-08-29T11:26:31Z
counts: { P0: 0, P1: 5, P2: 1, P3: 0 }
---

# F03 — codegen-declarations

## Scope audited
- `src/Runtime/YO4X.Mql5.CodeGen/Mql5GeneratorRun.Declarations.cs` (1170 lines)

## Verdict
The declaration emission logic handles basic scalar types, standard enums, and entry-point dispatch shims cleanly, but exhibits critical flaws in default initialisation and symbol resolution for composite types. Static local structures and class instances fail to be instantiated in the constructor, struct and class member fields omit default initialization for object and string types causing runtime null dereferences, and out-of-line method definitions as well as base class inheritance checks fail to account for overload parameter types and generic type arguments.

## Findings

### [P1] Static local structures and class instances are not instantiated in constructor
- **Where:** `src/Runtime/YO4X.Mql5.CodeGen/Mql5GeneratorRun.Declarations.cs:821-845`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
    private void EmitStaticLocalInitializer(StaticLocal local)
    {
        _writer.LineDirective(local.Variable.Line);
        if (local.Variable.Initializer is not null)
        {
            string value = ValueText(local.Type, local.Variable.ArrayRanks, local.Variable.Initializer, 1);
            _writer.Line(local.FieldName + " = " + value + ";");
            return;
        }

        if (local.Type.ArrayRanks.Count + local.Variable.ArrayRanks.Count > 0)
        {
            string? core = CoreTypeName(local.Type);
            _writer.Line(
                local.FieldName + " = "
                + (core is null ? PoisonToken : ArrayCreation(core, local.Type, local.Variable.ArrayRanks, 1))
                + ";");
            return;
        }

        if (local.Type.Scalar == Mql5IrScalarKind.Text || local.Type.Scalar == Mql5IrScalarKind.Moment)
        {
            _writer.Line(local.FieldName + " = " + Mql5ClrTypes.DefaultFor(local.Type.Scalar) + ";");
        }
    }
  ```
- **Failure:** In MQL5, declaring an uninitialised static local structure or class (such as `static CTrade trade;` or `static MqlTradeRequest req;`) inside a function provides zeroed and default-constructed storage. Unlike `EmitFileScopeInitializer` (lines 801–813), `EmitStaticLocalInitializer` lacks a check for `resolved.Kind is Mql5ResolvedTypeKind.Structure or Mql5ResolvedTypeKind.Class`. As a result, the generated constructor emits no assignment for `__static_function_trade`, leaving the hoisted field as `null`. When the strategy executes and calls `trade.Buy(...)`, a `NullReferenceException` is thrown on the first trade invocation.
- **Fix:** Add a structure/class resolution branch in `EmitStaticLocalInitializer` matching `EmitFileScopeInitializer` (lines 800–813) to instantiate non-pointer structure and class static locals using `new T(ConstructionArguments) { ... }`.

### [P1] Struct and class member fields omit default initialization for object, structure, and string types
- **Where:** `src/Runtime/YO4X.Mql5.CodeGen/Mql5GeneratorRun.Declarations.cs:548-575`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
        if (field.Initializer is not null)
        {
            if (ReferencesInstanceState(field.Initializer, 0))
            {
                Fail(
                    Mql5CodeGenDiagnosticCodes.UnsupportedInitializer,
                    "The initialiser of '" + field.Name + "' reads instance state, which a C# field initialiser cannot.",
                    field.Line,
                    field.Column);
            }
            else
            {
                initializer = ValueText(field.Type, field.ArrayRanks, field.Initializer, depth + 1);
            }
        }
        else if (field.Type.ArrayRanks.Count + field.ArrayRanks.Count > 0)
        {
            string? core = CoreTypeName(field.Type);
            if (core is not null)
            {
                initializer = ArrayCreation(core, field.Type, field.ArrayRanks, depth);
            }
        }

        _writer.LineDirective(field.Line);
        _writer.Line(
            modifiers + " " + typeText + " " + Mql5ClrTypes.Identifier(field.Name)
            + (initializer is null ? string.Empty : " = " + initializer) + ";");
  ```
- **Failure:** When an MQL5 struct or class defines a non-pointer member field of class, runtime structure, or string type without an explicit inline initializer (e.g. `CTrade m_trade;`, `MqlTradeRequest m_req;`, or `string m_comment;` inside `class CTrader`), `EmitField` sets `initializer` to `null`. In emitted C#, `internal Mql5Trade m_trade;` and `internal string m_comment;` are emitted with no initialization (defaulting to `null`). In MQL5, member objects are constructed and strings default to `""`. Calling `m_trade.Buy(...)` or evaluating string properties on the instance throws a `NullReferenceException` at runtime.
- **Fix:** When `field.Initializer is null` and the field is not an array, check if `field.Type` is a non-pointer structure/class or string scalar, and assign default construction (`new T(...)`) or `string.Empty` to `initializer`.

### [P1] OutOfLineBody matches overloads by parameter count only, dropping distinct signatures
- **Where:** `src/Runtime/YO4X.Mql5.CodeGen/Mql5GeneratorRun.Declarations.cs:365-373`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
        foreach (Mql5IrFunction candidate in members)
        {
            if (SplitQualified(candidate.Name) is (_, string member)
                && string.Equals(member, method.Name, StringComparison.Ordinal)
                && candidate.Parameters.Count == method.Parameters.Count)
            {
                return candidate;
            }
        }
  ```
- **Failure:** When an MQL5 class declares overloaded methods with the same parameter count but different parameter types (e.g. `void SetValue(int x);` and `void SetValue(string x);`), `OutOfLineBody` returns the first candidate where `candidate.Parameters.Count == method.Parameters.Count`. Both `SetValue(int)` and `SetValue(string)` receive the body of whichever definition appeared first at file scope. The second overload's body is dropped, and the wrong method body is emitted under the mismatched signature, resulting in C# compilation errors or invalid runtime execution.
- **Fix:** Match candidate parameters against method parameter types in addition to matching parameter count.

### [P1] Generic base class lookup in EmitTypeDeclaration shadows runtime and owner fields
- **Where:** `src/Runtime/YO4X.Mql5.CodeGen/Mql5GeneratorRun.Declarations.cs:440-442`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
        bool inheritsRuntime = declaration.BaseTypeName is not null
            && _typeNames.ContainsKey(declaration.BaseTypeName);
        if (!isInterface && !inheritsRuntime)
        {
  ```
- **Failure:** When an MQL5 class inherits from a generic template base (e.g. `class MyClass : BaseTemplate<double>`), `declaration.BaseTypeName` is `"BaseTemplate<double>"`. `_typeNames` contains bare type names (`"BaseTemplate"`), so `_typeNames.ContainsKey(declaration.BaseTypeName)` evaluates to `false` (unlike line 410, which uses `BareTypeName(declaration.BaseTypeName)`). `inheritsRuntime` evaluates to `false`, causing `EmitTypeDeclaration` to re-declare `internal IMql5Runtime Rt;` and `internal Strategy __owner;` on the derived class. Object initializers populate the derived fields while leaving `base.Rt` as `null`, so any inherited base class method accessing `Rt` to invoke MQL5 built-ins throws a `NullReferenceException`.
- **Fix:** Change line 441 to check `_typeNames.ContainsKey(BareTypeName(declaration.BaseTypeName))`.

### [P1] Prefix matching in EmitMethodCore corrupts static local symbol rewriting across functions
- **Where:** `src/Runtime/YO4X.Mql5.CodeGen/Mql5GeneratorRun.Declarations.cs:975-983`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
        _staticLocalNames.Clear();
        if (owner is null)
        {
            foreach (StaticLocal local in _staticLocals)
            {
                if (local.FieldName.StartsWith(
                    "__static_" + function.Name + "_", StringComparison.Ordinal))
                {
                    _staticLocalNames[local.Variable.Name] = local.FieldName;
                }
            }
        }
  ```
- **Failure:** When function `Calc` contains a static local `A_count` (field `__static_Calc_A_count`), and another function `Calc_A` declares static local `count` (field `__static_Calc_A_count`), or if function `Calc` references a variable named `count`, `local.FieldName.StartsWith("__static_Calc_")` matches `__static_Calc_A_count` belonging to `Calc_A`. In `Calc`, `_staticLocalNames["count"]` is set to `__static_Calc_A_count`. Expressions inside `Calc` that read or write `count` are redirected to mutate `Calc_A`'s static local state.
- **Fix:** Associate hoisted static locals directly with their declaring function identity or exact qualified symbol instead of loose string prefix matching on flattened field names.

### [P2] Non-constant parameter default values emitted via ConvertTo fail C# compile-time constant rule
- **Where:** `src/Runtime/YO4X.Mql5.CodeGen/Mql5GeneratorRun.Declarations.cs:1034-1038`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
                else if (TryConstantText(parameter.DefaultValue, depth, out string? constant))
                {
                    Mql5ResolvedType target = ResolveWrittenType(parameter.Type, []);
                    text += " = " + ConvertTo(target, TypeOf(parameter.DefaultValue), constant);
                }
  ```
- **Failure:** When an optional string parameter in MQL5 has a numeric `0` or `NULL` default (e.g. `void Log(string msg = NULL)`), `TryConstantText` returns `true`, and `ConvertTo` converts `0` to `"Mql5Ops.ToText(0)"`. The emitted signature becomes `public void Log(string msg = Mql5Ops.ToText(0))`. In C#, default parameter values must be compile-time constants; emitting method invocations in parameter defaults causes compilation failure with `CS1736`.
- **Fix:** For string parameter defaults receiving `NULL` or `0`, emit constant literals `""` or `null` directly instead of calling conversion helper methods.

## Referrals
- `src/Runtime/YO4X.Mql5.CodeGen/Mql5GeneratorRun.cs:373` — `CollectStaticLocals` only processes file-scope functions in `_module.Functions`, skipping static local variables declared inside methods of `_module.Types`.
- `src/Runtime/YO4X.Mql5.CodeGen/Mql5GeneratorRun.Expressions.cs:1816` — `ArrayLiteralBody` hardcodes rank-2 jagged syntax (`core + "[][]"`), causing 3D array literals to emit invalid C# array creation expressions.

## Coverage gaps
- `src/Runtime/YO4X.Mql5.CodeGen/Mql5GeneratorRun.Declarations.cs:83-88` — `UnsupportedArrayRank` diagnostic branch for array ranks >= 4 is untested if the parser/lowering layer rejects high-rank arrays before codegen.
- `src/Runtime/YO4X.Mql5.CodeGen/Mql5GeneratorRun.Declarations.cs:402-409` — Diagnostic branch for struct declaring a base type (`UnsupportedTypeDeclaration`) is untested for generic or templated struct hierarchies.


--- agy: gemini-3.7-flash-high | effort=high | mode=plan (read-only) | 128.5s | 268583 tok | id=4ecd90e1-0db6-4853-b27c-d55735616403
