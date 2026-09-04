You are a fix agent on YO4X, a LIVE MetaTrader 5 / MQL5 algorithmic trading platform (.NET 10 backend, React frontend, an MQL5-to-C# transpiler, a deterministic backtest engine). An audit found defects in ONE file. Fix them.

THE ONLY FILE YOU MAY MODIFY:
  src/Runtime/YO4X.Mql5.CodeGen/Mql5ClrTypes.cs

Read that file completely first. You may read any other file for context, but you must not edit any other file, create files, delete files, or run commands.

FINDINGS TO FIX (6):

[1] [P1] Predefined variable `_UninitReason` maps to nonexistent property `UninitReason`
    Where:   src/Runtime/YO4X.Mql5.CodeGen/Mql5ClrTypes.cs:86
    Failure: When an MQL5 strategy reads the predefined variable `_UninitReason`, `Mql5GeneratorRun.Expressions.cs:378` evaluates `PredefinedVariables["_UninitReason"]` and emits `Rt.UninitReason`. `IMql5Runtime` defines `int UninitializeReason();` as a method and declares no property named `UninitReason`. Emitted C# fails Roslyn compilation with CS1061 (`'IMql5Runtime' does not contain a definition for 'UninitReason'`).
    Suggested fix: Change the entry in `PredefinedVariables` from `["_UninitReason"] = "UninitReason"` to `["_UninitReason"] = "UninitializeReason()"`.

[2] [P1] Predefined variables `_RandomSeed`, `_IsX64`, and `_AppliedTo` map to nonexistent `IMql5Runtime` members
    Where:   src/Runtime/YO4X.Mql5.CodeGen/Mql5ClrTypes.cs:84
    Failure: When MQL5 code uses `_RandomSeed`, `_IsX64`, or `_AppliedTo`, codegen emits `Rt.RandomSeed`, `Rt.IsX64`, or `Rt.AppliedTo`. None of these three members exist on `IMql5Runtime` (`RandomSeed` only exists on `Mql5RuntimeOptions`), causing downstream compilation failure with CS1061.
    Suggested fix: Remove `_RandomSeed`, `_IsX64`, and `_AppliedTo` from `PredefinedVariables` until corresponding members are exposed on `IMql5Runtime`.

[3] [P1] `StructToTime` marked with `r` in `RuntimeByReferenceParameters` emits illegal `ref` for `in` parameter
    Where:   src/Runtime/YO4X.Mql5.CodeGen/Mql5ClrTypes.cs:1180
    Failure: `IMql5Runtime.DateTime.cs:61` declares `long StructToTime(in Mql5DateTime moment);`. Because `RuntimeByReferenceParameters` records `1:0r`, `RuntimeParameterKeyword` returns `"ref "`, causing codegen to emit `Rt.StructToTime(ref moment)`. In C#, passing `ref` to a parameter declared with `in` causes compile error CS1615 (`Argument 1 may not be passed with the 'ref' keyword`).
    Suggested fix: Remove `["StructToTime"] = "1:0r"` from `RuntimeByReferenceParameters` so argument 0 is emitted using standard by-value / `in` syntax.

[4] [P1] `retcode_external` in `RuntimeMemberClrTypes` emits invalid `(uint)` cast on signed `int` property
    Where:   src/Runtime/YO4X.Mql5.CodeGen/Mql5ClrTypes.cs:259
    Failure: `Mql5TradeResult.RetcodeExternal` and `Mql5Trade.ResultRetcodeExternal()` are declared as signed `int` (matching external broker return code conventions). When MQL5 code assigns to `result.retcode_external = -1;`, `Mql5GeneratorRun.Expressions.cs:1245-1246` wraps the RHS in `(uint)(...)`, generating `result.RetcodeExternal = (uint)(-1);`. In C#, assigning `uint` to an `int` property causes compilation failure CS0266 (cannot implicitly convert `uint` to `int`), and in unchecked execution would corrupt negative return code signs.
    Suggested fix: Remove `["retcode_external"] = "uint"` from `RuntimeMemberClrTypes` because `int` properties need no conversion per `RuntimeMemberClrTypes`' contract.

[5] [P2] Missing snake_case member aliases for `MqlParam` fields in `RuntimeMemberAliases`
    Where:   src/Runtime/YO4X.Mql5.CodeGen/Mql5ClrTypes.cs:173-236
    Failure: MQL5 `MqlParam` structures declare fields in lower_snake_case: `type`, `integer_value`, `double_value`, `string_value`. The runtime model `Mql5Param` declares `Type`, `IntegerValue`, `DoubleValue`, and `StringValue`. `RuntimeMemberAliases` translates aliases for `MqlTradeRequest`, `MqlTradeResult`, `MqlRates`, `MqlTick`, `MqlTradeTransaction`, and `MqlDateTime`, but omits `integer_value`, `double_value`, and `string_value`. As a result, code accessing `param.integer_value` emits `param.integer_value` instead of `param.IntegerValue`, failing C# compilation with CS1061.
    Suggested fix: Add `["integer_value"] = "IntegerValue"`, `["double_value"] = "DoubleValue"`, and `["string_value"] = "StringValue"` to `RuntimeMemberAliases`.

[6] [P3] Duplicate constant `INVALID_HANDLE` in `PredefinedConstants`
    Where:   src/Runtime/YO4X.Mql5.CodeGen/Mql5ClrTypes.cs:295
    Failure: `"INVALID_HANDLE"` is declared twice in `PredefinedConstants` (line 295 and line 305). While `ToFrozenSet()` deduplicates items, the duplicate array entry is redundant table baggage.
    Suggested fix: Remove the duplicate `"INVALID_HANDLE"` from line 305.

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

