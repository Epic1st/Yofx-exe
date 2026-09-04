---
agent_id: F10
lane: rt-math-conversion
scope:
  - src/Runtime/YO4X.Mql5.Runtime/Mql5Runtime.Math.cs
  - src/Runtime/YO4X.Mql5.Runtime/Mql5Runtime.Conversion.cs
  - src/Runtime/YO4X.Mql5.Runtime/Mql5Format.cs
status: COMPLETE
generated: 2026-08-29T08:34:00Z
counts: { P0: 0, P1: 3, P2: 3, P3: 1 }
---

# F10 — rt-math-conversion

## Scope audited
- `src/Runtime/YO4X.Mql5.Runtime/Mql5Runtime.Math.cs` (463 lines)
- `src/Runtime/YO4X.Mql5.Runtime/Mql5Runtime.Conversion.cs` (584 lines)
- `src/Runtime/YO4X.Mql5.Runtime/Mql5Format.cs` (642 lines)

## Verdict
The math, conversion, and C-style format runtime layer provides high fidelity for standard mathematical operations, invariant culture parsing, and deterministic pseudo-random sequence replay. However, several critical semantic divergence bugs exist: `StringToInteger` completely lacks hexadecimal literal parsing (`0x`/`0X`), returning `0` for hex values; `ToUInt64` in `Mql5Format` fails to handle signed integer types, causing negative 32-bit integers formatted with `%u`, `%x`, `%X`, or `%o` to 64-bit sign-extend to `ulong.MaxValue`; `StringToTime` binds time-only inputs to the host operating system's wall-clock calendar date, violating backtest determinism; and `DoubleToString` / `Mql5Format.Fixed` rely on .NET's banker's rounding rather than half-away-from-zero rounding, creating subtle price-string mismatches against `NormalizeDouble`.

## Findings

### [P1] StringToInteger fails to parse hexadecimal strings and returns 0
- **Where:** `src/Runtime/YO4X.Mql5.Runtime/Mql5Runtime.Conversion.cs:263`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
  int digitStart = length;
  while (length < span.Length && char.IsAsciiDigit(span[length]))
  {
      length++;
  }

  if (length == digitStart)
  ```
- **Failure:** MQL5 documents `StringToInteger` as accepting decimal or hexadecimal notation with a `0x` or `0X` prefix (e.g. `StringToInteger("0x1A")` yields `26`, `StringToInteger("0xFF")` yields `255`). In `Mql5Runtime.Conversion.cs`, the parsing loop strictly scans for `char.IsAsciiDigit`. For any string starting with `0x` or `0X`, the loop reads the leading `'0'`, halts at `'x'`, and executes `long.TryParse(span[..1])`, returning `0`. Strategies that parse hex magic numbers, bitmasks, color values, or protocol payloads via `StringToInteger` silently receive `0` instead of the parsed integer.
- **Fix:** Check for `0x` / `0X` prefix after optional sign and parse using `long.TryParse(..., NumberStyles.HexNumber, CultureInfo.InvariantCulture, out parsed)` with appropriate hex character scanning.

### [P1] ToUInt64 in Mql5Format omits signed integer types causing 64-bit sign extension under %u, %x, %X, %o
- **Where:** `src/Runtime/YO4X.Mql5.Runtime/Mql5Format.cs:305`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
  public static ulong ToUInt64(object? value) => value switch
  {
      ulong number => number,
      uint number => number,
      byte number => number,
      ushort number => number,
      _ => unchecked((ulong)ToInt64(value))
  };
  ```
- **Failure:** In MQL5 / C `printf`, formatting a 32-bit negative integer with `%u` or `%x` treats the argument as a 32-bit unsigned integer (e.g. `(int)-1` formatted with `%u` produces `4294967295` and `%x` produces `ffffffff`). Because `ToUInt64` lacks pattern match arms for `int`, `short`, and `sbyte`, a boxed 32-bit `int` falls into `_ => unchecked((ulong)ToInt64(value))`. This sign-extends `-1` to `0xFFFFFFFFFFFFFFFFUL` (64 bits). As a result, `StringFormat("%u", -1)` outputs `18446744073709551615` instead of `4294967295`, and `StringFormat("%x", -1)` outputs `ffffffffffffffff` (16 characters) instead of `ffffffff` (8 characters).
- **Fix:** Add switch arms to `ToUInt64` for signed primitive types: `int number => unchecked((uint)number)`, `short number => unchecked((ushort)number)`, and `sbyte number => unchecked((byte)number)`.

### [P1] StringToTime synthesizes missing date components using host machine's wall-clock date
- **Where:** `src/Runtime/YO4X.Mql5.Runtime/Mql5Runtime.Conversion.cs:313`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
  if (DateTime.TryParseExact(
          trimmed,
          DateTimeFormats,
          CultureInfo.InvariantCulture,
          DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
          out DateTime exact))
  {
      return Mql5Time.FromDateTime(exact);
  }
  ```
- **Failure:** When given a time-only string such as `"12:30:00"`, `DateTime.TryParseExact` populates the date portion with the host system's current wall-clock calendar date (`DateTime.Today`). In a backtest replaying historic market data from 2020, calling `StringToTime("12:30:00")` evaluates to today's date (e.g. 2026-08-29 12:30:00) rather than the simulated engine date (`TimeCurrent()`) or an epoch date. This breaks deterministic backtesting and historical strategy replays across different execution dates.
- **Fix:** If the matched format is a time-only format (`HH:mm:ss` or `HH:mm`), combine the parsed `TimeSpan` with `context.TimeCurrent.Date` rather than relying on .NET's default `DateTime.Today` instantiation.

### [P2] Fixed and Scientific formatters use banker's rounding, diverging from NormalizeDouble on exact decimal ties
- **Where:** `src/Runtime/YO4X.Mql5.Runtime/Mql5Format.cs:236`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
  int clamped = digits < 0 ? 0 : (digits > MaxPrecision ? MaxPrecision : digits);
  return value.ToString("F" + clamped.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
  ```
- **Failure:** While `NormalizeDouble` explicitly uses `MidpointRounding.AwayFromZero`, `Mql5Format.Fixed` and `Mql5Format.Scientific` invoke `.NET`'s standard `.ToString("F...")` and `.ToString("E...")`, which perform round-to-nearest-even (banker's rounding). For exact ties at odd vs even numbers (e.g. `2.5` with 0 digits or `0.025` with 2 digits), `NormalizeDouble(2.5, 0)` produces `3.0`, whereas `DoubleToString(2.5, 0)` produces `"2"`, and `StringFormat("%.2f", 0.025)` produces `"0.02"`. Order comments, ticket labels, and logs generated via `DoubleToString` diverge from the actual order price produced by `NormalizeDouble`.
- **Fix:** Format fixed floating-point numbers by calling `Math.Round(value, clamped, MidpointRounding.AwayFromZero)` prior to string formatting, or use a custom tie-breaking formatter.

### [P2] Exp1M and Log1PCore return NaN on infinity and large finite inputs due to intermediate indeterminate form
- **Where:** `src/Runtime/YO4X.Mql5.Runtime/Mql5Runtime.Math.cs:447`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
  return (raised - 1.0) * value / Math.Log(raised);
  ```
- **Failure:** In `Exp1M` and `Log1PCore`, when `value` is `double.PositiveInfinity` or `value >= 709.782712893384` (where `Math.Exp(value)` overflows to `PositiveInfinity`), the expression evaluates `(Infinity * value) / Math.Log(Infinity)` = `Infinity / Infinity`, which produces `double.NaN`. In MQL5 / IEEE-754 mathematics, `MathExpm1(+Inf)` and `MathLog1p(+Inf)` evaluate to `+Inf`.
- **Fix:** Guard against non-finite values at the start of `Exp1M` and `Log1PCore` (returning `value` if `double.IsInfinity(value) || double.IsNaN(value)`), or delegate directly to .NET's built-in `double.ExpM1(value)` and `double.LogP1(value)`.

### [P2] StringFormat drops NUL character for %c specifier
- **Where:** `src/Runtime/YO4X.Mql5.Runtime/Mql5Format.cs:493`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
  long value = ToInt64(Next(arguments, ref argumentIndex));
  string body = value is > 0 and <= 0xFFFF ? ((char)value).ToString() : string.Empty;
  return Pad(string.Empty, string.Empty, body, width, leftAlign, zeroPad: false);
  ```
- **Failure:** In C `printf` and MQL5 `StringFormat`, `%c` with an argument of `0` emits a 1-character string containing the null character `\0`. In `Mql5Format.cs`, the check `value is > 0 and <= 0xFFFF` explicitly excludes `0`, setting `body` to `string.Empty`. Thus, `StringFormat("%c", 0)` returns an empty string `""` (length 0) instead of `"\0"` (length 1).
- **Fix:** Change the range check to `value is >= 0 and <= 0xFFFF`.

### [P3] IMql5Runtime and Mql5Runtime omit signed integer overloads for MathSwap
- **Where:** `src/Runtime/YO4X.Mql5.Runtime/Mql5Runtime.Math.cs:126`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
  ushort MathSwap(ushort value);

  /// <summary>MQL5 <c>MathSwap</c>: reverses the byte order of a 32-bit value. Native.</summary>
  uint MathSwap(uint value);

  /// <summary>MQL5 <c>MathSwap</c>: reverses the byte order of a 64-bit value. Native.</summary>
  ulong MathSwap(ulong value);
  ```
- **Failure:** MQL5 specifies `MathSwap` overloads for both signed and unsigned integer types: `short`, `int`, `long`, `ushort`, `uint`, `ulong`. `IMql5Runtime` only exposes `ushort`, `uint`, and `ulong`. When transpiled MQL5 code passes signed integers (e.g. `int magic = MathSwap(order_magic)`), C# overload resolution fails without an explicit cast.
- **Fix:** Add `short MathSwap(short value)`, `int MathSwap(int value)`, and `long MathSwap(long value)` to `IMql5Runtime` and `Mql5Runtime`.

## Referrals
- `src/Runtime/YO4X.Mql5.Runtime/Mql5Colors.cs:66` — `Mql5Colors.TryParse` fails to parse numeric string representations (e.g. `"16711680"`, `"0x0000FF"`), causing `StringToColor` to return `ColorNone` (-1) instead of parsing numeric colors as documented in MQL5.
- `src/Runtime/YO4X.Mql5.Runtime/Mql5Structs.cs:146` — `Mql5Time.FromDateTime` converts `DateTimeKind.Local` to UTC using `ToUniversalTime()`, dropping local timezone context.
- `src/Runtime/YO4X.Mql5.Runtime/Mql5Structs.cs:177` — `Mql5Time.ToStruct` populates `DayOfYear` using .NET's 1-based `DateTime.DayOfYear` rather than MQL5's 0-based specification (0–365).

## Coverage gaps
- `Mql5Runtime.StringToInteger` (`src/Runtime/YO4X.Mql5.Runtime/Mql5Runtime.Conversion.cs:249`): Missing test coverage for hexadecimal string inputs with `"0x"` and `"0X"` prefixes (e.g. `"0x1A"`, `"0xFF"`, `"-0x10"`).
- `Mql5Format.Format` (`src/Runtime/YO4X.Mql5.Runtime/Mql5Format.cs:428`): Missing test coverage for formatting negative 32-bit signed integers with `%u`, `%x`, `%X`, and `%o` format specifiers.
- `Mql5Runtime.StringToTime` (`src/Runtime/YO4X.Mql5.Runtime/Mql5Runtime.Conversion.cs:305`): Missing test coverage verifying that time-only strings do not bind to the host system's wall-clock calendar date during backtests.
- `Mql5Format.Format` (`src/Runtime/YO4X.Mql5.Runtime/Mql5Format.cs:490`): Missing test coverage for `%c` with character code 0 (`\0`).
- `Mql5Runtime.MathExpm1` / `MathLog1p` (`src/Runtime/YO4X.Mql5.Runtime/Mql5Runtime.Math.cs:434`): Missing test coverage for `double.PositiveInfinity` and large finite values (`>= 710.0`).


--- agy: gemini-3.7-flash-high | effort=high | mode=plan (read-only) | 179.9s | 296670 tok | id=73132885-c496-4dc0-9df4-709187271f04
