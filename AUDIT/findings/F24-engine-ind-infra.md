---
agent_id: F24
lane: Shared indicator infrastructure
scope:
  - src/Runtime/YO4X.Mql5.Engine/Indicators/Mql5IndicatorBase.cs
  - src/Runtime/YO4X.Mql5.Engine/Indicators/Mql5IndicatorFactory.cs
  - src/Runtime/YO4X.Mql5.Engine/Indicators/RollingWindow.cs
  - src/Runtime/YO4X.Mql5.Engine/Indicators/MovingAverageCalculator.cs
  - src/Runtime/YO4X.Mql5.Engine/Indicators/IMql5Indicator.cs
status: COMPLETE
generated: 2026-08-29T11:28:45Z
counts: { P0: 0, P1: 2, P2: 0, P3: 0 }
---

# F24 — Shared indicator infrastructure

## Scope audited

- `src/Runtime/YO4X.Mql5.Engine/Indicators/Mql5IndicatorBase.cs` (110 lines)
- `src/Runtime/YO4X.Mql5.Engine/Indicators/Mql5IndicatorFactory.cs` (373 lines)
- `src/Runtime/YO4X.Mql5.Engine/Indicators/RollingWindow.cs` (119 lines)
- `src/Runtime/YO4X.Mql5.Engine/Indicators/MovingAverageCalculator.cs` (96 lines)
- `src/Runtime/YO4X.Mql5.Engine/Indicators/IMql5Indicator.cs` (29 lines)

## Verdict

The core moving average mathematics in `MovingAverageCalculator` (SMA, EMA, SMMA, LWMA) and the base buffer alignment mechanics in `Mql5IndicatorBase` are mathematically sound. However, the ring-buffer indexing in `RollingWindow` is fundamentally broken whenever the buffer is partially filled before reaching full capacity, corrupting index queries, sums, averages, and extreme calculations during warm-up. Additionally, `Mql5IndicatorFactory.CreateAtr` expects an arity of 2 instead of the standard MQL5 arity of 1, causing the timeframe constant (e.g. `PERIOD_H1 = 16385`) to be assigned as the ATR period on canonical MQL5 indicator creation calls.

## Findings

### [P1] RollingWindow ring-buffer index arithmetic reads uninitialized slots before window is full
- **Where:** `src/Runtime/YO4X.Mql5.Engine/Indicators/RollingWindow.cs:23`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
    /// <summary>Gets a value by age, where zero is the oldest retained value.</summary>
    internal double this[int index] => items[(head + index) % items.Length];
  ```
- **Failure:** When `RollingWindow` has not yet reached capacity (`!IsFull`), elements are appended sequentially into `items[0 .. Count - 1]` and `head` equals `Count`. Evaluating `this[0]` accesses `items[head]`, which is an uninitialized default slot (`0.0`) rather than the oldest added value at `items[0]`. Furthermore, `RollingWindow.Add` computes `Sum` by looping `0 .. Count - 1` with `this[index]`. For example, with `capacity = 5`, adding `10.0`, `20.0`, `30.0` leaves `items = [10, 20, 30, 0, 0]` and `head = 3`. `this[0]` reads `items[3] = 0.0`, `this[1]` reads `items[4] = 0.0`, `this[2]` reads `items[0] = 10.0`, resulting in `Sum = 10.0` instead of `60.0`, `Average() = 3.333` instead of `20.0`, `Highest() = 10.0` instead of `30.0`, and `Lowest() = 0.0` instead of `10.0`.
- **Fix:** Offset the ring index starting from `0` when `!IsFull` and from `head` when `IsFull`: `internal double this[int index] => items[((IsFull ? head : 0) + index) % items.Length];`.

### [P1] Mql5IndicatorFactory.CreateAtr uses arity 2 instead of 1, corrupting ATR period with timeframe constant
- **Where:** `src/Runtime/YO4X.Mql5.Engine/Indicators/Mql5IndicatorFactory.cs:125`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
    private static Mql5AtrIndicator CreateAtr(IReadOnlyList<object?> parameters)
    {
        List<double> values = Numeric(parameters, 2);
        int period = Int(values, 0, 14);
        int smoothing = Int(values, 1, (int)Mql5MaMethod.Smma);
        return new Mql5AtrIndicator(period, (Mql5MaMethod)smoothing);
    }
  ```
- **Failure:** In MQL5, `iATR` takes 3 parameters: `(string symbol, ENUM_TIMEFRAMES period, int ma_period)`. There is only 1 indicator calculation parameter (`ma_period`). When an EA calls `iATR("EURUSD", PERIOD_H1, 14)`, `parameters` contains `["EURUSD", 16385, 14]`. `Numeric(parameters, 2)` strips the string symbol but retains both numbers because `expected` is set to `2`. As a result, `values[0]` (`16385`) is passed as `period`, and `values[1]` (`14`) is passed as `smoothing`. An ATR indicator with a 16,385-bar period is instantiated instead of a 14-bar ATR, returning `EmptyValue` (0.0) across backtests and disabling indicator-dependent trade logic.
- **Fix:** Change `Numeric(parameters, 2)` to `Numeric(parameters, 1)` in `CreateAtr` so that leading timeframe constants are stripped when resolving the ATR period.

## Referrals

None.

## Coverage gaps

- `src/Runtime/YO4X.Mql5.Engine/Indicators/RollingWindow.cs:23`: No unit test inspects `this[index]`, `Sum`, `Average()`, `Highest()`, or `Lowest()` on a `RollingWindow` instance before `Count == Capacity`, allowing the pre-capacity index offset bug to remain undetected.
- `src/Runtime/YO4X.Mql5.Engine/Indicators/Mql5IndicatorFactory.cs:125`: `IndicatorExpansionAccuracyTests.FactoryDropsTheLeadingSymbolAndTimeframeForTheNewIndicators` covers `iADX`, `iWPR`, and `iSAR` full 3-arg / 4-arg calls, but omits `iATR`, leaving the parameter count mismatch untested.


--- agy: gemini-3.7-flash-high | effort=high | mode=plan (read-only) | 118.7s | 254446 tok | id=436422fd-192d-487c-b7eb-a5c1bfcad6c2
