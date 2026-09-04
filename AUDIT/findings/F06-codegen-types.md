---
agent_id: F06
lane: codegen-types
scope:
  - src/Runtime/YO4X.Mql5.CodeGen/Mql5ClrTypes.cs
status: COMPLETE
generated: 2026-08-29T08:28:00Z
counts: { P0: 0, P1: 4, P2: 1, P3: 1 }
---

# F06 — codegen-types

## Scope audited
- `src/Runtime/YO4X.Mql5.CodeGen/Mql5ClrTypes.cs` (1399 lines) — primary audit scope.
- `src/Runtime/YO4X.Mql5.Runtime/IMql5Runtime.cs` (133 lines) — reviewed for runtime built-in method signatures, predefined variable surfaces, and error codes.
- `src/Runtime/YO4X.Mql5.Runtime/Mql5TradeTypes.cs` (189 lines) — reviewed for trade request/result struct property types and mutability.
- `src/Runtime/YO4X.Mql5.Runtime/Mql5Structs.cs` (200 lines) — reviewed for tick, rates, book info, datetime, and parameter struct layouts.
- `src/Runtime/YO4X.Mql5.Runtime/Mql5Colors.cs` (108 lines) — reviewed for BGR byte packing order and color conversions.
- `src/Runtime/YO4X.Mql5.Runtime/StandardLibrary/Mql5Trade.cs` (514 lines) — reviewed for CTrade methods, parameter shapes, and return types.
- `src/Runtime/YO4X.Mql5.Runtime/StandardLibrary/Mql5SymbolInfo.cs` (246 lines) — reviewed for CSymbolInfo method shapes.
- `src/Runtime/YO4X.Mql5.Runtime/StandardLibrary/Mql5AccountInfo.cs` (228 lines) — reviewed for CAccountInfo method shapes.
- `src/Runtime/YO4X.Mql5.Runtime/StandardLibrary/Mql5DealInfo.cs` (129 lines) — reviewed for CDealInfo method shapes.
- `src/Runtime/YO4X.Mql5.Runtime/StandardLibrary/Mql5OrderInfo.cs` (125 lines) — reviewed for COrderInfo method shapes.
- `src/Runtime/YO4X.Mql5.Runtime/StandardLibrary/Mql5HistoryOrderInfo.cs` (109 lines) — reviewed for CHistoryOrderInfo method shapes.
- `src/Runtime/YO4X.Mql5.Runtime/StandardLibrary/Mql5TradeTransaction.cs` (63 lines) — reviewed for MqlTradeTransaction field mappings.
- `src/Runtime/YO4X.Mql5.CodeGen/Mql5GeneratorRun.Expressions.cs` (1817 lines) — reviewed for type conversion and member access emission.
- `src/Runtime/YO4X.Mql5.CodeGen/Mql5GeneratorRun.Declarations.cs` (1170 lines) — reviewed for struct vs class declarations and array initialization.
- `tests/YO4X.Mql5.Runtime.Tests/ParameterTypeShapeTests.cs` (162 lines) — reviewed for runtime parameter reflection tests.
- `tests/YO4X.Mql5.Runtime.Tests/ByReferenceShapeTests.cs` (134 lines) — reviewed for ref/out reflection tests.
- `tests/YO4X.Mql5.Runtime.Tests/BuiltinNameResolutionTests.cs` (71 lines) — reviewed for builtin alias resolution tests.

## Verdict
The scalar type mapping (`Mql5ClrTypes.Spell`, `DefaultFor`, `WidthOf`, and `ScalarKeywords`) is mathematically sound across all 15 MQL5 scalar types (`char`, `uchar`, `short`, `ushort`, `int`, `uint`, `long`, `ulong`, `float`, `double`, `bool`, `string`, `color` as packed BGR `int`, and `datetime` as epoch `long`). However, four high-severity type contract mismatches break transpilation and emitted C# compilation on reachable paths: predefined variable `_UninitReason` maps to a nonexistent property instead of `UninitializeReason()`; `_RandomSeed`, `_IsX64`, and `_AppliedTo` map to nonexistent runtime members; `retcode_external` is typed as `uint` in `RuntimeMemberClrTypes` instead of signed `int`, forcing an invalid cast; and `StructToTime` passes `ref` for a parameter declared with `in`.

## Findings

### [P1] Predefined variable `_UninitReason` maps to nonexistent property `UninitReason`
- **Where:** `src/Runtime/YO4X.Mql5.CodeGen/Mql5ClrTypes.cs:86`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
  ["_UninitReason"] = "UninitReason",
  ```
- **Failure:** When an MQL5 strategy reads the predefined variable `_UninitReason`, `Mql5GeneratorRun.Expressions.cs:378` evaluates `PredefinedVariables["_UninitReason"]` and emits `Rt.UninitReason`. `IMql5Runtime` defines `int UninitializeReason();` as a method and declares no property named `UninitReason`. Emitted C# fails Roslyn compilation with CS1061 (`'IMql5Runtime' does not contain a definition for 'UninitReason'`).
- **Fix:** Change the entry in `PredefinedVariables` from `["_UninitReason"] = "UninitReason"` to `["_UninitReason"] = "UninitializeReason()"`.

### [P1] Predefined variables `_RandomSeed`, `_IsX64`, and `_AppliedTo` map to nonexistent `IMql5Runtime` members
- **Where:** `src/Runtime/YO4X.Mql5.CodeGen/Mql5ClrTypes.cs:84`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
  ["_RandomSeed"] = "RandomSeed",
  ["_StopFlag"] = "IsStopped()",
  ["_UninitReason"] = "UninitReason",
  ["_IsX64"] = "IsX64",
  ["_AppliedTo"] = "AppliedTo",
  ```
- **Failure:** When MQL5 code uses `_RandomSeed`, `_IsX64`, or `_AppliedTo`, codegen emits `Rt.RandomSeed`, `Rt.IsX64`, or `Rt.AppliedTo`. None of these three members exist on `IMql5Runtime` (`RandomSeed` only exists on `Mql5RuntimeOptions`), causing downstream compilation failure with CS1061.
- **Fix:** Remove `_RandomSeed`, `_IsX64`, and `_AppliedTo` from `PredefinedVariables` until corresponding members are exposed on `IMql5Runtime`.

### [P1] `retcode_external` in `RuntimeMemberClrTypes` emits invalid `(uint)` cast on signed `int` property
- **Where:** `src/Runtime/YO4X.Mql5.CodeGen/Mql5ClrTypes.cs:259`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
  ["request_id"] = "uint",
  ["retcode_external"] = "uint",
  ["time"] = "long",
  ```
- **Failure:** `Mql5TradeResult.RetcodeExternal` and `Mql5Trade.ResultRetcodeExternal()` are declared as signed `int` (matching external broker return code conventions). When MQL5 code assigns to `result.retcode_external = -1;`, `Mql5GeneratorRun.Expressions.cs:1245-1246` wraps the RHS in `(uint)(...)`, generating `result.RetcodeExternal = (uint)(-1);`. In C#, assigning `uint` to an `int` property causes compilation failure CS0266 (cannot implicitly convert `uint` to `int`), and in unchecked execution would corrupt negative return code signs.
- **Fix:** Remove `["retcode_external"] = "uint"` from `RuntimeMemberClrTypes` because `int` properties need no conversion per `RuntimeMemberClrTypes`' contract.

### [P1] `StructToTime` marked with `r` in `RuntimeByReferenceParameters` emits illegal `ref` for `in` parameter
- **Where:** `src/Runtime/YO4X.Mql5.CodeGen/Mql5ClrTypes.cs:1180`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
  ["StructToCharArray"] = "1:0r;2:0r",
  ["StructToTime"] = "1:0r",
  ["SymbolInfoDouble"] = "3:2o",
  ```
- **Failure:** `IMql5Runtime.DateTime.cs:61` declares `long StructToTime(in Mql5DateTime moment);`. Because `RuntimeByReferenceParameters` records `1:0r`, `RuntimeParameterKeyword` returns `"ref "`, causing codegen to emit `Rt.StructToTime(ref moment)`. In C#, passing `ref` to a parameter declared with `in` causes compile error CS1615 (`Argument 1 may not be passed with the 'ref' keyword`).
- **Fix:** Remove `["StructToTime"] = "1:0r"` from `RuntimeByReferenceParameters` so argument 0 is emitted using standard by-value / `in` syntax.

### [P2] Missing snake_case member aliases for `MqlParam` fields in `RuntimeMemberAliases`
- **Where:** `src/Runtime/YO4X.Mql5.CodeGen/Mql5ClrTypes.cs:173-236`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
  public static FrozenDictionary<string, string> RuntimeMemberAliases { get; } =
      new Dictionary<string, string>(StringComparer.Ordinal)
      {
          // MqlTradeRequest
          ["action"] = "Action",
  ```
- **Failure:** MQL5 `MqlParam` structures declare fields in lower_snake_case: `type`, `integer_value`, `double_value`, `string_value`. The runtime model `Mql5Param` declares `Type`, `IntegerValue`, `DoubleValue`, and `StringValue`. `RuntimeMemberAliases` translates aliases for `MqlTradeRequest`, `MqlTradeResult`, `MqlRates`, `MqlTick`, `MqlTradeTransaction`, and `MqlDateTime`, but omits `integer_value`, `double_value`, and `string_value`. As a result, code accessing `param.integer_value` emits `param.integer_value` instead of `param.IntegerValue`, failing C# compilation with CS1061.
- **Fix:** Add `["integer_value"] = "IntegerValue"`, `["double_value"] = "DoubleValue"`, and `["string_value"] = "StringValue"` to `RuntimeMemberAliases`.

### [P3] Duplicate constant `INVALID_HANDLE` in `PredefinedConstants`
- **Where:** `src/Runtime/YO4X.Mql5.CodeGen/Mql5ClrTypes.cs:295`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
  "EMPTY", "EMPTY_VALUE", "WHOLE_ARRAY", "INVALID_HANDLE", "WRONG_VALUE",
  ```
- **Failure:** `"INVALID_HANDLE"` is declared twice in `PredefinedConstants` (line 295 and line 305). While `ToFrozenSet()` deduplicates items, the duplicate array entry is redundant table baggage.
- **Fix:** Remove the duplicate `"INVALID_HANDLE"` from line 305.

## Referrals
- `src/Runtime/YO4X.Mql5.Runtime/Mql5TradeTypes.cs:18` — `Mql5TradeRequest`, `Mql5TradeResult`, `Mql5TradeCheckResult`, and `Mql5TradeTransaction` are declared as `sealed class` (reference types) instead of `struct` (value types), causing struct assignment `MqlTradeRequest req2 = req1;` in C# to silently alias the original instance where MQL5 copies values.

## Coverage gaps
- `Mql5ClrTypes.PredefinedVariables`: `_UninitReason`, `_RandomSeed`, `_IsX64`, and `_AppliedTo` are completely untested against `IMql5Runtime` in `tests/YO4X.Mql5.Runtime.Tests`, allowing nonexistent and malformed member names to remain undetected.
- `Mql5ClrTypes.RuntimeMemberClrTypes`: No test asserts that types listed in `RuntimeMemberClrTypes` match the actual property types of runtime classes (`Mql5TradeResult.RetcodeExternal`), allowing invalid cast insertions to escape.
