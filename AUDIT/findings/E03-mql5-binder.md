---
agent_id: E03
lane: Name Resolution and Typing
scope:
  - src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5Binder.cs
status: COMPLETE
generated: 2026-08-29T11:27:00Z
counts: { P0: 0, P1: 3, P2: 4, P3: 0 }
---

# E03 — Name Resolution and Typing

## Scope audited
- `src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5Binder.cs` (2695 lines) — reviewed fully.

## Verdict
The semantic binder provides a clean recursive AST traversal and robust handling of basic lexical scoping, but suffers from severe fidelity defects in overload resolution, built-in constant typing, and qualified name lookup. Overload selection completely ignores parameter types and accepts ambiguous calls by arbitrarily selecting the first declared overload. Floating-point built-in constants (including `EMPTY_VALUE`, `DBL_MAX`, and `M_PI`) and `NULL` are incorrectly typed as 32-bit integers (`Whole32`), directly compromising indicator buffer checks and null pointer semantics.

## Findings

### [P1] Real-valued built-in constants (EMPTY_VALUE, DBL_MAX, M_PI) silently resolve as 32-bit integers with value 0
- **Where:** `src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5Binder.cs:2167`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
            Type? constants = assembly.GetType(
                "YO4X.StrategyGovernance.Mql5BuiltinConstants", throwOnError: false);
            MethodInfo? tryGetValue = constants?.GetMethod(
                "TryGetValue", BindingFlags.Public | BindingFlags.Static);
            if (tryGetValue is not null
                && tryGetValue.ReturnType == typeof(bool)
                && tryGetValue.GetParameters().Length == 2)
            {
                TryGetConstantMethod = tryGetValue;
            }
  ```
- **Failure:** `Mql5BinderRuntime` only inspects `Mql5BuiltinConstants` (which only contains integer constants) and never binds to `Mql5BuiltinRealConstants`. When a trading strategy references `EMPTY_VALUE` (the indicator buffer sentinel equal to `1.7976931348623157e+308`), `DBL_MAX`, or `M_PI`, `TryGetConstantMethod` fails and execution falls back to `Mql5BinderFallback.IsConstant()`, which returns `true` with default `value = 0L`. In `ResolveBuiltinName` (line 1408), the constant is resolved as `Mql5IrScalarKind.Whole32` (`int`) with value 0 instead of `Real64` (`double`). Downstream code comparing indicator outputs `if (val == EMPTY_VALUE)` checks against integer 0 rather than `DBL_MAX`.
- **Fix:** Reflectively load `Mql5BuiltinRealConstants.TryGetValue` alongside `Mql5BuiltinConstants`, and update `ResolveBuiltinName` to return `Mql5ResolvedType.ForScalar(Mql5IrScalarKind.Real64)` for real constants.

### [P1] Overload resolution ignores parameter types and picks first declaration on ambiguous arity matches
- **Where:** `src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5Binder.cs:1877`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
            if (arguments >= required && arguments <= allowed)
            {
                matches++;
                match ??= candidate;
            }
  ```
- **Failure:** `CheckOverloads` evaluates candidates solely on argument count (`arguments >= required && arguments <= allowed`). When two overloads share the same parameter count with different parameter types (e.g. `double Process(double price)` and `int Process(int count)`), calling `Process(1.23)` matches both overloads. Instead of performing type-based ranking or emitting an ambiguity error, `CheckOverloads` silently increments `_ambiguousCalls` and arbitrarily returns the return type of the first declared candidate (`match ??= candidate`), causing downstream expressions to be typed incorrectly based on declaration order.
- **Fix:** Match argument expression types against parameter types with MQL5 standard implicit conversion ranking, and emit `Mql5BindDiagnosticCodes.UnresolvedCall` or an ambiguity diagnostic when no unique best match exists.

### [P1] NULL constant resolves as Whole32 integer instead of NullLiteral
- **Where:** `src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5Binder.cs:1408`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
        if (!asCallee && Mql5BinderRuntime.TryGetBuiltinConstant(name.Name, out long value))
        {
            _builtinConstantReferences++;
            return new Mql5ResolvedSymbol(
                Mql5SymbolKind.BuiltinConstant,
                name.Name,
                value is >= int.MinValue and <= int.MaxValue
                    ? Mql5ResolvedType.ForScalar(Mql5IrScalarKind.Whole32)
                    : Mql5ResolvedType.ForScalar(Mql5IrScalarKind.Whole64),
                name.Line,
                name.Column,
                true);
        }
  ```
- **Failure:** When `NULL` is referenced in source as an identifier name expression, `Mql5BinderRuntime.TryGetBuiltinConstant("NULL", out long value)` returns `0`. `ResolveBuiltinName` classifies `NULL` as `Whole32` (`int`). When compared with or assigned to a pointer (`CObject* obj = NULL; if (obj == NULL)`), `CommonType(Class*, Whole32)` evaluates to `Mql5ResolvedType.Unknown`, breaking null pointer checks.
- **Fix:** Check for `"NULL"` in `ResolveBuiltinName` and return `Mql5ResolvedType.Null` (`NullLiteral`) rather than `Whole32`.

### [P2] Class-qualified access to nested enum members fails resolution
- **Where:** `src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5Binder.cs:1461`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
        if (TryGetDeclaredType(qualifier, out Mql5BinderTypeInfo? info))
        {
            Mql5IrField? field = FindField(info, name.Name);
            if (field is not null)
            {
                return new Mql5ResolvedSymbol(
                    Mql5SymbolKind.Field,
                    name.Name,
                    ResolveTypeReference(field.Type, report: false, extraRank: field.ArrayRanks.Count),
                    field.Line,
                    field.Column,
                    true);
            }

            List<Mql5IrFunction>? methods = FindMethods(info, name.Name);
  ```
- **Failure:** In `ResolveQualifiedName`, when qualifier is a declared class or struct (`qualifier = "CStrategy"`), the binder only searches `info.Fields` and `info.Methods`. It does not search `info.Declaration.NestedEnums`. A valid MQL5 expression referencing a nested enum member via its class qualifier (`CStrategy::STATE_ACTIVE`) fails lookup and emits a false `MQL5_BIND_UNRESOLVED_NAME` error.
- **Fix:** In `ResolveQualifiedName`, search `info.Declaration.NestedEnums` for matching enum members when field and method lookups miss on a type qualifier.

### [P2] 64-bit integer #define macros are unconditionally typed as Whole32
- **Where:** `src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5Binder.cs:486`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
        if (Mql5IrLiteral.TryFoldWhole(text) is not null)
        {
            return Mql5ResolvedType.ForScalar(Mql5IrScalarKind.Whole32);
        }
  ```
- **Failure:** `ClassifyDefine` classifies every integer `#define` replacement as `Mql5IrScalarKind.Whole32`. For definitions with values outside 32-bit signed range (e.g. `#define BITMASK 0xFFFFFFFF` or `#define BIG_VAL 1000000000000LL`), the symbol is typed as 32-bit signed `int` (`Whole32`), whereas `TypeOfLiteral` correctly checks `int.MinValue` to `int.MaxValue` and uses `Whole64`. Downstream arithmetic and shift expressions on large `#define` constants lose precision or overflow unexpectedly.
- **Fix:** Check `Mql5IrLiteral.TryFoldWhole(text)` against `int.MinValue` and `int.MaxValue`, returning `Whole64` when the value exceeds 32 bits.

### [P2] Built-in enumeration constants are typed as raw Whole32 scalars rather than Enumeration types
- **Where:** `src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5Binder.cs:1408`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
                value is >= int.MinValue and <= int.MaxValue
                    ? Mql5ResolvedType.ForScalar(Mql5IrScalarKind.Whole32)
                    : Mql5ResolvedType.ForScalar(Mql5IrScalarKind.Whole64),
  ```
- **Failure:** All built-in enum members (such as `POSITION_TYPE_BUY`, `ORDER_TYPE_BUY`, `TRADE_ACTION_DEAL`, and `PERIOD_CURRENT`) are resolved as scalar `Whole32` rather than `Mql5ResolvedTypeKind.Enumeration` carrying their declaring enum name. Type-checking mechanisms cannot verify that argument constants match formal parameter enum types.
- **Fix:** Query `Mql5BuiltinConstants.TryGetDeclaringEnum(name, out string enumName)` in `ResolveBuiltinName` and return `EnumerationType(enumName)` when present.

### [P2] Binary subtraction of datetime from integer is typed as datetime
- **Where:** `src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5Binder.cs:1995`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
        if (preserveMoment
            && (left.Scalar == Mql5IrScalarKind.Moment || right.Scalar == Mql5IrScalarKind.Moment))
        {
            bool bothMoments = left.Scalar == Mql5IrScalarKind.Moment && right.Scalar == Mql5IrScalarKind.Moment;
            return bothMoments
                ? Mql5ResolvedType.ForScalar(Mql5IrScalarKind.Whole64)
                : Mql5ResolvedType.ForScalar(Mql5IrScalarKind.Moment);
        }
  ```
- **Failure:** `BindBinary` routes `case "-":` to `UsualArithmetic(left, right, preserveMoment: true)`. When evaluating `seconds - time` where `left` is `int` (`Whole32`) and `right` is `datetime` (`Moment`), `bothMoments` is false and the result is typed as `Moment` (`datetime`). Subtracting a timestamp from an integer is nonsensical as a calendar moment and should be typed as integer or flagged.
- **Fix:** For binary subtraction (`-`), only preserve `Moment` when `left` is `Moment` and `right` is an integer scalar; when `left` is integer and `right` is `Moment`, return `Whole64`.

## Referrals
None.

## Coverage gaps
- `Mql5Binder.cs:1892` — Branch `Mql5BinderRuntime.TryCheckBuiltinArity` in `CheckOverloads` when a user function overload shares a built-in name with conflicting parameter types: no tests verify resolution when a user overload has different arity and return type from the built-in.
- `Mql5Binder.cs:1460` — Multi-level qualifier resolution (`Class::NestedClass::Method` where `name.Scope.Count > 1`): `ResolveQualifiedName` only checks `name.Scope[^1]`, untested for deep namespace or nested class structures.


--- agy: gemini-3.7-flash-high | effort=high | mode=plan (read-only) | 123.8s | 272995 tok | id=c1b2ea62-0bae-4bf4-b4f0-36b5c12ad0e2
