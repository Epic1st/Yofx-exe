---
agent_id: F17
lane: Core Constants, Error Codes, Colors, Logging & Market Context Interfaces
scope:
  - src/Runtime/YO4X.Mql5.Runtime/Mql5Constants.cs
  - src/Runtime/YO4X.Mql5.Runtime/Mql5ErrorCodes.cs
  - src/Runtime/YO4X.Mql5.Runtime/Mql5Colors.cs
  - src/Runtime/YO4X.Mql5.Runtime/Mql5Log.cs
  - src/Runtime/YO4X.Mql5.Runtime/Mql5ProgramInfo.cs
  - src/Runtime/YO4X.Mql5.Runtime/Mql5UnsupportedOperationException.cs
  - src/Runtime/YO4X.Mql5.Runtime/IMql5MarketContext.cs
  - src/Runtime/YO4X.Mql5.Runtime/IMql5Strategy.cs
status: COMPLETE
generated: 2026-08-29T11:28:00Z
counts: { P0: 0, P1: 2, P2: 1, P3: 1 }
---

# F17 — Core Constants, Error Codes, Colors, Logging & Market Context Interfaces

## Scope audited
- `src/Runtime/YO4X.Mql5.Runtime/Mql5Constants.cs` (238 lines)
- `src/Runtime/YO4X.Mql5.Runtime/Mql5ErrorCodes.cs` (92 lines)
- `src/Runtime/YO4X.Mql5.Runtime/Mql5Colors.cs` (108 lines)
- `src/Runtime/YO4X.Mql5.Runtime/Mql5Log.cs` (107 lines)
- `src/Runtime/YO4X.Mql5.Runtime/Mql5ProgramInfo.cs` (170 lines)
- `src/Runtime/YO4X.Mql5.Runtime/Mql5UnsupportedOperationException.cs` (52 lines)
- `src/Runtime/YO4X.Mql5.Runtime/IMql5MarketContext.cs` (484 lines)
- `src/Runtime/YO4X.Mql5.Runtime/IMql5Strategy.cs` (28 lines)

## Verdict
The core constants, error code values, program info defaults, and market context interfaces are structurally solid and faithfully replicate MetaQuotes MQL5 specifications (including non-sequential timeframe numbers, BGR color packing, and tester-specific runtime permissions). However, `Mql5Colors` contains a semantic flaw where `clrNONE` (-1) masks to `0x00FFFFFF` and is misidentified as `"White"`, as well as failing to parse standard `"clrNone"` string representations. Additionally, `IMql5MarketContext` has an unhandled null dereference in its default `CopyBufferRange` implementation, and `Mql5LogRecorder` contains redundant concurrency logic.

## Findings

### [P1] `Mql5Colors.Name` aliases `ColorNone` (-1) to `"White"`, corrupting `clrNONE` string conversion
- **Where:** `src/Runtime/YO4X.Mql5.Runtime/Mql5Colors.cs:59`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
  public static string? Name(int color) => ByValue.TryGetValue(color & 0xFFFFFF, out string? found) ? found : null;
  ```
- **Failure:** When `Mql5Colors.Name` is called with `Mql5Constants.ColorNone` (`-1` / `0xFFFFFFFF`), `color & 0xFFFFFF` masks the value to `0x00FFFFFF`. Because `Table` maps `White` to `0xFFFFFF`, `ByValue` returns `"White"`. Consequently, converting `clrNONE` with `ColorToString(clrNONE, true)` emits `"clrWhite"` instead of `"clrNONE"` / empty, turning transparent indicator plots or invisible chart objects into opaque solid white.
- **Fix:** Explicitly guard against `color == Mql5Constants.ColorNone` (or `color < 0`) before querying `ByValue`, returning `null` or handling `clrNONE` explicitly.

### [P1] `Mql5Colors.TryParse` fails to recognize `"clrNone"`, `"clrNONE"`, and `"None"`
- **Where:** `src/Runtime/YO4X.Mql5.Runtime/Mql5Colors.cs:74-83`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
  string trimmed = text.Trim();
  if (trimmed.StartsWith("clr", StringComparison.OrdinalIgnoreCase) && trimmed.Length > 3)
  {
      string bare = trimmed[3..];
      if (ByName.TryGetValue(bare, out int named))
      {
          color = named;
          return true;
      }
  }
  ```
- **Failure:** `ByName` is built strictly from `Table`, which does not include `None`. When parsing valid MQL5 color literals `"clrNone"`, `"clrNONE"`, or `"None"`, `TryParse` fails and returns `false`. When called through `StringToColor("clrNone")`, this failure sets `ERR_INVALID_PARAMETER` (4003) on `GetLastError()`, corrupting strategy error handling for standard MQL5 code.
- **Fix:** Add `"None"` / `Mql5Constants.ColorNone` to the color name dictionary or explicitly check for `None` in `TryParse` and return `Mql5Constants.ColorNone`.

### [P2] `IMql5MarketContext.CopyBufferRange` throws `NullReferenceException` when `target` is null
- **Where:** `src/Runtime/YO4X.Mql5.Runtime/IMql5MarketContext.cs:412-415`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
  if (target.Length < count)
  {
      Array.Resize(ref target, count);
  }
  ```
- **Failure:** If an external engine or test calls default interface method `IMql5MarketContext.CopyBufferRange` with an unallocated/null `target` array and `range.Count > 0`, `target.Length` immediately throws `NullReferenceException` instead of allocating the required buffer.
- **Fix:** Update the guard to `if (target is null || target.Length < count)` before resizing or allocating `target = new double[count]`.

### [P3] `Mql5LogRecorder.Log` contains redundant branches and decrements `count` on failed dequeue
- **Where:** `src/Runtime/YO4X.Mql5.Runtime/Mql5Log.cs:97-104`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
  if (entries.TryDequeue(out _))
  {
      Interlocked.Decrement(ref count);
  }
  else
  {
      Interlocked.Decrement(ref count);
  }
  ```
- **Failure:** The `if` and `else` branches are byte-for-byte identical. If `TryDequeue` returns `false` under high multi-threaded contention, `count` is still decremented despite no entry being removed, causing the recorded count to drift below the actual queue size.
- **Fix:** Remove the redundant `else` branch so `count` is only decremented when `TryDequeue` successfully removes an item.

## Referrals
- `src/Runtime/YO4X.Mql5.Runtime/Mql5Runtime.Conversion.cs:385` — `ColorToString` delegates directly to `Mql5Colors.Name` without checking for `ColorNone` first, inheriting the `"clrWhite"` string corruption.

## Coverage gaps
- `src/Runtime/YO4X.Mql5.Runtime/Mql5ProgramInfo.cs:114-140` — Unexercised exception-throwing branches in `InfoInteger` for host-specific properties (`ProgramType`, `LicenseType`, `MemoryUsed`, `MemoryLimit`, `HandlesUsed`, `Codepage`, `GlobalCounter`, and unknown property IDs).
- `src/Runtime/YO4X.Mql5.Runtime/Mql5ProgramInfo.cs:151-164` — Unexercised exception-throwing branches in `InfoString` for `ProgramName`, `ProgramPath`, and unknown property IDs.
- `src/Runtime/YO4X.Mql5.Runtime/Mql5Constants.cs:148` — Default branch (`_ => 0`) in `Mql5Constants.Timeframes.Seconds` for invalid/unrecognized timeframe identifiers.


--- agy: gemini-3.7-flash-high | effort=high | mode=plan (read-only) | 83.5s | 202717 tok | id=6d880c70-ae0d-4cc1-a63b-67730529075b
