---
agent_id: A11
lane: backtest-form
scope:
  - src/Frontend/YO4X.Web/src/features/backtests/backtestForm.ts
status: COMPLETE
generated: 2026-08-29T08:51:00Z
counts: { P0: 0, P1: 1, P2: 2, P3: 2 }
---

# A11 — backtest-form

## Scope audited
- `src/Frontend/YO4X.Web/src/features/backtests/backtestForm.ts` (611 lines)

## Verdict
The pure validation logic in `backtestForm.ts` is largely solid with deterministic UTC date parsing, careful enum member resolution, and non-coercing input state handling. However, there is a notable contract mismatch in color serialization where the client emits CSS `#rrggbb` strings for non-`C'r,g,b'` defaults that the backend `IsColour` validator rejects. Additionally, client-side numeric validation allows non-decimal formats (`0x...`) for real numbers, and text-edited color inputs bypass color syntax checks.

## Findings

### [P1] `formatColourValue` emits CSS hex colors rejected by backend `IsColour` validator
- **Where:** `src/Frontend/YO4X.Web/src/features/backtests/backtestForm.ts:106-119`
- **Confidence:** CONFIRMED
- **Code:**
  ```typescript
  export function formatColourValue(defaultValue: string, hex: string): string {
    const parsed = hexColourPattern.exec(hex.trim());
    if (parsed === null) {
      return hex;
    }
    const digits = parsed[1] ?? '';
    if (!colourLiteralPattern.test(defaultValue.trim())) {
      return `#${digits.toLowerCase()}`;
    }

    const channels = [digits.slice(0, 2), digits.slice(2, 4), digits.slice(4, 6)]
      .map((part) => Number.parseInt(part, 16));
    return `C'${channels.join(',')}'`;
  }
  ```
- **Failure:** When a strategy declares a color input with a hex default (e.g. `#00FF7F`), `parseColourDefault` accepts it and `editorKindFor` assigns it a color picker (`COLOUR`). When the user selects a new color (e.g. `#0080ff`), `formatColourValue` evaluates `!colourLiteralPattern.test(defaultValue.trim())` as true and returns `#0080ff`. Client validation (`validateInputValue:295-298`) passes it. However, backend `PostgresFrontendProjections.ValidateInputValue` validates colors via `IsColour` (`PostgresFrontendProjections.cs:2716-2752`), which only accepts `C'r,g,b'`, MQL5 identifiers (`clrRed`), `0x...` hex integers, and unsigned decimals. `IsColour("#0080ff")` returns `false`, causing the server to reject the backtest creation request with HTTP 422 `VALUE_NOT_A_COLOUR`.
- **Fix:** In `formatColourValue`, always serialize picked colors to MQL5 `C'r,g,b'` literals (e.g. `C'0,128,255'`) or numeric literals regardless of whether the source default was written as `C'r,g,b'`.

### [P2] `validateInputValue` permits hexadecimal and non-decimal literals for `REAL` inputs that backend rejects
- **Where:** `src/Frontend/YO4X.Web/src/features/backtests/backtestForm.ts:272-275`
- **Confidence:** CONFIRMED
- **Code:**
  ```typescript
      case 'REAL':
        return Number.isFinite(Number(trimmed)) && trimmed !== ''
          ? null
          : `${input.declaredType} takes a decimal number.`;
  ```
- **Failure:** In JavaScript, `Number("0x10")` returns `16` (finite), `Number("0b101")` returns `5`, and `Number("0o77")` returns `63`. If a user enters `"0x10"` for a `REAL` (`double` or `float`) input, `validateInputValue` returns `null` (valid). On the server, `PostgresFrontendProjections.ValidateInputValue` (`PostgresFrontendProjections.cs:2607-2616`) parses the input using `double.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out _)`. `NumberStyles.Float` rejects hexadecimal, binary, and octal notations, causing the server to reject the request with HTTP 422 `VALUE_NOT_A_REAL_NUMBER`.
- **Fix:** Validate `REAL` inputs against a decimal floating-point regex (`/^[+-]?(?:[0-9]+(?:\.[0-9]*)?|\.[0-9]+)(?:[eE][+-]?[0-9]+)?$/u`) before checking `Number.isFinite(Number(trimmed))`.

### [P2] `validateInputValue` for `COLOUR` inputs edited as text does not validate color syntax
- **Where:** `src/Frontend/YO4X.Web/src/features/backtests/backtestForm.ts:295-298`
- **Confidence:** CONFIRMED
- **Code:**
  ```typescript
      case 'COLOUR':
      case 'TEXT':
      default:
        return submitted.length > 2_000 ? 'That value is too long to record.' : null;
  ```
- **Failure:** When a color input default cannot be shown in a color picker (e.g. `clrTomato` or `0x00FF00`), `editorKindFor` falls back to a text box (`TEXT`). If the user enters an invalid color string (e.g. `clrInvalid!`, `not_a_color`, or a string with illegal symbols), `validateInputValue` falls through to `TEXT` and checks only `submitted.length > 2_000`, returning `null`. The backend `IsColour` check rejects the value upon submission with HTTP 422 `VALUE_NOT_A_COLOUR`.
- **Fix:** Implement client-side validation for text-edited `COLOUR` inputs matching server `IsColour` rules (validating `C'r,g,b'`, valid MQL5 identifiers `/^[A-Za-z_][A-Za-z0-9_]{0,63}$/u`, `0x` hex integers, or unsigned decimals).

### [P3] Frontend input length limit (2,000 characters) diverges from backend limit (4,000 characters)
- **Where:** `src/Frontend/YO4X.Web/src/features/backtests/backtestForm.ts:298`
- **Confidence:** CONFIRMED
- **Code:**
  ```typescript
      case 'COLOUR':
      case 'TEXT':
      default:
        return submitted.length > 2_000 ? 'That value is too long to record.' : null;
  ```
- **Failure:** The backend `MaximumInputValueLength` in `PostgresFrontendProjections.cs:64` is 4,000 characters, matching `simulation.backtest_inputs.value` table constraint `check (length(value) <= 4000)`. However, `backtestForm.ts` caps inputs at 2,000 characters. If a strategy requires long textual inputs (such as serialized configurations or parameters between 2,001 and 4,000 characters), the client blocks the user with `"That value is too long to record."` even though the service and database allow up to 4,000 characters.
- **Fix:** Update the character limit check from `2_000` to `4_000` to match `MaximumInputValueLength`.

### [P3] `validateFormValues` does not trim `strategyId` before empty check
- **Where:** `src/Frontend/YO4X.Web/src/features/backtests/backtestForm.ts:326-328`
- **Confidence:** CONFIRMED
- **Code:**
  ```typescript
    if (values.strategyId === '') {
      errors.strategyId = 'Choose the strategy to test.';
    }
  ```
- **Failure:** While `symbol` and `timeframe` validation check `values.symbol.trim() === ''`, `strategyId` checks `values.strategyId === ''` without trimming. If `strategyId` contains whitespace (e.g. `'   '`), `validateFormValues` passes validation on the client instead of flagging `errors.strategyId`.
- **Fix:** Check `values.strategyId.trim() === ''` or validate against a standard UUID format regex.

## Referrals
- `src/Infrastructure/YO4X.ControlPlane.Postgres/PostgresFrontendProjections.cs:2675-2709` — `IsMoment` accepts second timestamps via `long.TryParse(value, NumberStyles.None, ...)` without range checking against unix epoch boundaries.
- `src/Infrastructure/YO4X.ControlPlane.Postgres/PostgresFrontendProjections.cs:1377-1382` — `CreateBacktestAsync` checks `request.PeriodEnd < request.PeriodStart` but does not enforce maximum data window boundaries or check for dates outside broker market data availability.

## Coverage gaps
- `src/Frontend/YO4X.Web/src/features/backtests/backtestForm.ts:107-110` — `formatColourValue` branch where `hex` argument fails `hexColourPattern` and is returned as-is lacks test coverage.
- `src/Frontend/YO4X.Web/src/features/backtests/backtestForm.ts:437-446` — `serverFieldErrors` branch where `pathTokens` contains an out-of-bounds numeric index for `inputs[index]` (falling through to `unmatched`) is untested.


--- agy: gemini-3.7-flash-high | effort=high | mode=plan (read-only) | 139.4s | 305686 tok | id=616c385d-1bbc-413b-a872-a0b8a6492395
