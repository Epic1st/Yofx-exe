---
agent_id: F01
lane: Generator Structure & Run Lifecycle
scope:
  - src/Runtime/YO4X.Mql5.CodeGen/Mql5CodeGenerator.cs
  - src/Runtime/YO4X.Mql5.CodeGen/Mql5GeneratorRun.cs
status: COMPLETE
generated: 2026-08-29T11:26:30Z
counts: { P0: 0, P1: 5, P2: 1, P3: 0 }
---

# F01 — Generator Structure & Run Lifecycle

## Scope audited
- `src/Runtime/YO4X.Mql5.CodeGen/Mql5CodeGenerator.cs` (69 lines)
- `src/Runtime/YO4X.Mql5.CodeGen/Mql5GeneratorRun.cs` (534 lines)

## Verdict
The lifecycle design of `Mql5CodeGenerator` is clean of cross-compilation state leakage: each invocation instantiates a fresh `Mql5GeneratorRun` with isolated collections, invariant path/line writing, and sorted constant emission. However, within a single generation run, the lookup construction in `Mql5GeneratorRun.cs` suffers from symbol map overwriting on nested types, improper splitting of nested qualified method names, syntax corruption and collisions on static local hoisting, and total omission of static locals declared inside inline class methods.

## Findings

### [P1] Nested type and enum registration overwrites lookup entries keyed by unqualified names
- **Where:** `src/Runtime/YO4X.Mql5.CodeGen/Mql5GeneratorRun.cs:406-414`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
    private void RegisterType(Mql5IrTypeDeclaration declaration, string? owner)
    {
        string emitted = owner is null
            ? Mql5ClrTypes.Identifier(declaration.Name)
            : owner + "." + Mql5ClrTypes.Identifier(declaration.Name);
        _typeNames[declaration.Name] = emitted;
        _typeDeclarations[declaration.Name] = declaration;
        _moduleTypeClrNames.Add(emitted);
  ```
- **Failure:** When a module defines a top-level type or enum `Config` and a class `TradeManager` containing a nested struct or enum also named `Config`, `RegisterType` registers the top-level type under key `"Config"` and then unconditionally overwrites `_typeNames["Config"]` with `"TradeManager.Config"` (and overwrites `_typeDeclarations["Config"]` with the nested declaration). Any top-level variable or parameter declared as `Config` is then emitted with C# type `TradeManager.Config` instead of `Config`, and type resolution uses the wrong AST node.
- **Fix:** Key `_typeNames` and `_typeDeclarations` by fully qualified or scoped names, or maintain lexical scope hierarchy rather than overwriting unqualified keys.

### [P1] Static local variable field generator produces invalid C# identifier names for out-of-line functions
- **Where:** `src/Runtime/YO4X.Mql5.CodeGen/Mql5GeneratorRun.cs:481-482`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
    private static string StaticFieldName(string functionName, string variableName) =>
        "__static_" + functionName + "_" + variableName;
  ```
- **Failure:** When an out-of-line member function definition such as `void CTrade::Execute() { static int count = 0; }` contains a static local variable, `functionName` is `"CTrade::Execute"`. `StaticFieldName` produces `"__static_CTrade::Execute_count"`. When emitted on the strategy class, `private int __static_CTrade::Execute_count;` contains `::`, which is invalid C# identifier syntax and causes Roslyn compilation failure (`CS1003` / `CS1001`).
- **Fix:** Sanitize `functionName` in `StaticFieldName` by replacing `::` and invalid identifier characters with underscores (e.g. `functionName.Replace("::", "_", StringComparison.Ordinal)`).

### [P1] Static local variable hoisting collides and produces duplicate field declarations for overloads and inner blocks
- **Where:** `src/Runtime/YO4X.Mql5.CodeGen/Mql5GeneratorRun.cs:430-435`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
            case Mql5IrLocalDeclarationStatement declaration when declaration.IsStatic:
                foreach (Mql5IrVariable variable in declaration.Variables)
                {
                    _staticLocals.Add(
                        new StaticLocal(StaticFieldName(functionName, variable.Name), declaration.Type, variable));
                }

                break;
  ```
- **Failure:** If two overloaded functions (e.g. `void Log(string msg)` and `void Log(int code)`) both declare `static int counter;`, or if a single function contains static locals with the same variable name in two distinct nested blocks, both entries produce the identical field name `"__static_Log_counter"`. `_staticLocals` appends both entries, and the strategy class emits duplicate field definitions `private int __static_Log_counter;`, causing C# compilation error `CS0102`.
- **Fix:** Include unique source position offsets (line/column) or a monotonic sequence counter in hoisted static local field names.

### [P1] Nested class out-of-line method definitions fail to resolve and are dropped
- **Where:** `src/Runtime/YO4X.Mql5.CodeGen/Mql5GeneratorRun.cs:75-81`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
    private static (string Owner, string Member)? SplitQualified(string name)
    {
        int marker = name.IndexOf("::", StringComparison.Ordinal);
        return marker <= 0 || marker + 2 >= name.Length
            ? null
            : (name[..marker], name[(marker + 2)..]);
    }
  ```
- **Failure:** When an out-of-line method is defined for a nested type (e.g. `void COuter::CInner::Process()`), `SplitQualified` splits on the first `::`, assigning owner `"COuter"` and member `"CInner::Process"`. The method is stored in `_outOfLineDefinitions["COuter"]`. When `CInner` is emitted, its lookup for `_outOfLineDefinitions.TryGetValue("CInner", ...)` fails, and `OutOfLineBody` returns `null`. The body of `Process` is never emitted.
- **Fix:** Split on the last `::` via `LastIndexOf("::")` so that owner is `"COuter::CInner"` and member is `"Process"`, matching the nested type's qualified name.

### [P1] Static local variables declared inside inline class methods are never collected
- **Where:** `src/Runtime/YO4X.Mql5.CodeGen/Mql5GeneratorRun.cs:373-379`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
        foreach (Mql5IrFunction function in _module.Functions)
        {
            if (function.Body is not null)
            {
                CollectStaticLocals(function.Name, function.Body);
            }
        }
  ```
- **Failure:** In `BuildLookups`, static locals are only collected by traversing `_module.Functions` (file-scope and out-of-line functions). Methods defined inline within `_module.Types` (`Mql5IrTypeDeclaration.Methods`) are never inspected. Any `static` local variable inside an inline class method is omitted from `_staticLocals`, no hoisted field is created on the strategy, and the static variable loses persistence between invocations.
- **Fix:** Recursively traverse `_module.Types` and their `Methods` during `BuildLookups` to collect static local declarations.

### [P2] Fallback binding diagnostics are discarded when semantic model is rebound
- **Where:** `src/Runtime/YO4X.Mql5.CodeGen/Mql5CodeGenerator.cs:37-41`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
        if (model is null || !ReferenceEquals(model.Module, module))
        {
            Mql5BindResult bound = Mql5Binder.Bind(module);
            model = bound.Model;
        }
  ```
- **Failure:** If `Generate` is called with a null or mismatched model, `Mql5Binder.Bind(module)` executes to create a model. If semantic binding encounters errors (`bound.Succeeded == false`), `bound.Diagnostics` are discarded. The generator proceeds with an incomplete model and emits only downstream codegen errors without the root semantic diagnostics.
- **Fix:** Accumulate `bound.Diagnostics` and merge them into the returned `Mql5CodeGenResult.Diagnostics`.

## Referrals
- `src/Runtime/YO4X.Mql5.CodeGen/Mql5AssemblyBuilder.cs:87-89` — `Build` calls `Mql5Binder.Bind(module)` but discards `bound.Diagnostics`, never adding binding diagnostics to compilation output.
- `src/Runtime/YO4X.Mql5.CodeGen/Mql5GeneratorRun.Declarations.cs:500-503` — Type parameters in `_typeParametersInScope` are removed before nested types are emitted, causing nested types within generic classes to fail resolution of enclosing type parameters.
- `src/Runtime/YO4X.Mql5.CodeGen/Mql5GeneratorRun.Declarations.cs:973-983` — Static local rewriting is guarded by `if (owner is null)`, causing static locals in class methods to not be rewritten to their hoisted fields.

## Coverage gaps
- `src/Runtime/YO4X.Mql5.CodeGen/Mql5CodeGenerator.cs:49-63` — Exception catch block returning `Mql5CodeGenDiagnosticCodes.InternalFailure` is unverified for non-standard runtime exceptions.
- `src/Runtime/YO4X.Mql5.CodeGen/Mql5GeneratorRun.cs:520-530` — Nesting budget exhaustion branch in `Budget` when statement depth exceeds `MaxDepth = 200`.
- `src/Runtime/YO4X.Mql5.CodeGen/Mql5GeneratorRun.cs:393-398` — Enum member collision handling mapping `_enumMemberOwner[member.Name] = null` across conflicting enumerations.
- `src/Runtime/YO4X.Mql5.CodeGen/Mql5GeneratorRun.cs:298-304` — Fallback diagnostic branch in `EmitReferencedConstants` when an MQL5 constant has no published integer or real value.


--- agy: gemini-3.7-flash-high | effort=high | mode=plan (read-only) | 135.0s | 262396 tok | id=8a73fb67-49bb-4488-bcbd-7635659d7748
