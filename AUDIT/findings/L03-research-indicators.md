---
agent_id: L03
lane: Canonical MetaTrader Indicator Formulas vs YO4X Implementation
scope:
  - src/Runtime/YO4X.Mql5.Engine/Indicators/IMql5Indicator.cs
  - src/Runtime/YO4X.Mql5.Engine/Indicators/MovingAverageCalculator.cs
  - src/Runtime/YO4X.Mql5.Engine/Indicators/Mql5AdxIndicator.cs
  - src/Runtime/YO4X.Mql5.Engine/Indicators/Mql5AlligatorIndicator.cs
  - src/Runtime/YO4X.Mql5.Engine/Indicators/Mql5AtrIndicator.cs
  - src/Runtime/YO4X.Mql5.Engine/Indicators/Mql5AwesomeOscillatorIndicator.cs
  - src/Runtime/YO4X.Mql5.Engine/Indicators/Mql5BandsIndicator.cs
  - src/Runtime/YO4X.Mql5.Engine/Indicators/Mql5CciIndicator.cs
  - src/Runtime/YO4X.Mql5.Engine/Indicators/Mql5DeMarkerIndicator.cs
  - src/Runtime/YO4X.Mql5.Engine/Indicators/Mql5EnvelopesIndicator.cs
  - src/Runtime/YO4X.Mql5.Engine/Indicators/Mql5ForceIndexIndicator.cs
  - src/Runtime/YO4X.Mql5.Engine/Indicators/Mql5FractalsIndicator.cs
  - src/Runtime/YO4X.Mql5.Engine/Indicators/Mql5IndicatorBase.cs
  - src/Runtime/YO4X.Mql5.Engine/Indicators/Mql5IndicatorFactory.cs
  - src/Runtime/YO4X.Mql5.Engine/Indicators/Mql5MacdIndicator.cs
  - src/Runtime/YO4X.Mql5.Engine/Indicators/Mql5MomentumIndicator.cs
  - src/Runtime/YO4X.Mql5.Engine/Indicators/Mql5MovingAverageIndicator.cs
  - src/Runtime/YO4X.Mql5.Engine/Indicators/Mql5OsMaIndicator.cs
  - src/Runtime/YO4X.Mql5.Engine/Indicators/Mql5ParabolicSarIndicator.cs
  - src/Runtime/YO4X.Mql5.Engine/Indicators/Mql5RelativeVigorIndexIndicator.cs
  - src/Runtime/YO4X.Mql5.Engine/Indicators/Mql5RsiIndicator.cs
  - src/Runtime/YO4X.Mql5.Engine/Indicators/Mql5StdDevIndicator.cs
  - src/Runtime/YO4X.Mql5.Engine/Indicators/Mql5StochasticIndicator.cs
  - src/Runtime/YO4X.Mql5.Engine/Indicators/Mql5WilliamsPercentRangeIndicator.cs
  - src/Runtime/YO4X.Mql5.Engine/Indicators/RollingWindow.cs
status: COMPLETE
generated: 2026-08-29T11:42:00Z
counts: { P0: 0, P1: 2, P2: 1, P3: 0 }
---

# L03 — Canonical MetaTrader Indicator Formulas vs YO4X Implementation

## Scope audited
Reviewed all 25 files in `src/Runtime/YO4X.Mql5.Engine/Indicators/`:
- [IMql5Indicator.cs](file:///C:/Users/Dev23/Desktop/yo4x/src/Runtime/YO4X.Mql5.Engine/Indicators/IMql5Indicator.cs) (29 lines)
- [MovingAverageCalculator.cs](file:///C:/Users/Dev23/Desktop/yo4x/src/Runtime/YO4X.Mql5.Engine/Indicators/MovingAverageCalculator.cs) (96 lines)
- [Mql5AdxIndicator.cs](file:///C:/Users/Dev23/Desktop/yo4x/src/Runtime/YO4X.Mql5.Engine/Indicators/Mql5AdxIndicator.cs) (107 lines)
- [Mql5AlligatorIndicator.cs](file:///C:/Users/Dev23/Desktop/yo4x/src/Runtime/YO4X.Mql5.Engine/Indicators/Mql5AlligatorIndicator.cs) (71 lines)
- [Mql5AtrIndicator.cs](file:///C:/Users/Dev23/Desktop/yo4x/src/Runtime/YO4X.Mql5.Engine/Indicators/Mql5AtrIndicator.cs) (53 lines)
- [Mql5AwesomeOscillatorIndicator.cs](file:///C:/Users/Dev23/Desktop/yo4x/src/Runtime/YO4X.Mql5.Engine/Indicators/Mql5AwesomeOscillatorIndicator.cs) (35 lines)
- [Mql5BandsIndicator.cs](file:///C:/Users/Dev23/Desktop/yo4x/src/Runtime/YO4X.Mql5.Engine/Indicators/Mql5BandsIndicator.cs) (58 lines)
- [Mql5CciIndicator.cs](file:///C:/Users/Dev23/Desktop/yo4x/src/Runtime/YO4X.Mql5.Engine/Indicators/Mql5CciIndicator.cs) (45 lines)
- [Mql5DeMarkerIndicator.cs](file:///C:/Users/Dev23/Desktop/yo4x/src/Runtime/YO4X.Mql5.Engine/Indicators/Mql5DeMarkerIndicator.cs) (68 lines)
- [Mql5EnvelopesIndicator.cs](file:///C:/Users/Dev23/Desktop/yo4x/src/Runtime/YO4X.Mql5.Engine/Indicators/Mql5EnvelopesIndicator.cs) (61 lines)
- [Mql5ForceIndexIndicator.cs](file:///C:/Users/Dev23/Desktop/yo4x/src/Runtime/YO4X.Mql5.Engine/Indicators/Mql5ForceIndexIndicator.cs) (72 lines)
- [Mql5FractalsIndicator.cs](file:///C:/Users/Dev23/Desktop/yo4x/src/Runtime/YO4X.Mql5.Engine/Indicators/Mql5FractalsIndicator.cs) (87 lines)
- [Mql5IndicatorBase.cs](file:///C:/Users/Dev23/Desktop/yo4x/src/Runtime/YO4X.Mql5.Engine/Indicators/Mql5IndicatorBase.cs) (110 lines)
- [Mql5IndicatorFactory.cs](file:///C:/Users/Dev23/Desktop/yo4x/src/Runtime/YO4X.Mql5.Engine/Indicators/Mql5IndicatorFactory.cs) (373 lines)
- [Mql5MacdIndicator.cs](file:///C:/Users/Dev23/Desktop/yo4x/src/Runtime/YO4X.Mql5.Engine/Indicators/Mql5MacdIndicator.cs) (61 lines)
- [Mql5MomentumIndicator.cs](file:///C:/Users/Dev23/Desktop/yo4x/src/Runtime/YO4X.Mql5.Engine/Indicators/Mql5MomentumIndicator.cs) (42 lines)
- [Mql5MovingAverageIndicator.cs](file:///C:/Users/Dev23/Desktop/yo4x/src/Runtime/YO4X.Mql5.Engine/Indicators/Mql5MovingAverageIndicator.cs) (42 lines)
- [Mql5OsMaIndicator.cs](file:///C:/Users/Dev23/Desktop/yo4x/src/Runtime/YO4X.Mql5.Engine/Indicators/Mql5OsMaIndicator.cs) (51 lines)
- [Mql5ParabolicSarIndicator.cs](file:///C:/Users/Dev23/Desktop/yo4x/src/Runtime/YO4X.Mql5.Engine/Indicators/Mql5ParabolicSarIndicator.cs) (129 lines)
- [Mql5RelativeVigorIndexIndicator.cs](file:///C:/Users/Dev23/Desktop/yo4x/src/Runtime/YO4X.Mql5.Engine/Indicators/Mql5RelativeVigorIndexIndicator.cs) (78 lines)
- [Mql5RsiIndicator.cs](file:///C:/Users/Dev23/Desktop/yo4x/src/Runtime/YO4X.Mql5.Engine/Indicators/Mql5RsiIndicator.cs) (83 lines)
- [Mql5StdDevIndicator.cs](file:///C:/Users/Dev23/Desktop/yo4x/src/Runtime/YO4X.Mql5.Engine/Indicators/Mql5StdDevIndicator.cs) (64 lines)
- [Mql5StochasticIndicator.cs](file:///C:/Users/Dev23/Desktop/yo4x/src/Runtime/YO4X.Mql5.Engine/Indicators/Mql5StochasticIndicator.cs) (85 lines)
- [Mql5WilliamsPercentRangeIndicator.cs](file:///C:/Users/Dev23/Desktop/yo4x/src/Runtime/YO4X.Mql5.Engine/Indicators/Mql5WilliamsPercentRangeIndicator.cs) (47 lines)
- [RollingWindow.cs](file:///C:/Users/Dev23/Desktop/yo4x/src/Runtime/YO4X.Mql5.Engine/Indicators/RollingWindow.cs) (119 lines)

## Verdict
The core indicator mathematics in YO4X is exceptionally faithful to MetaTrader 5 canonical formulas: RSI and ATR correctly implement Wilder smoothing ($\alpha = 1/N$), Bollinger Bands and StdDev correctly use population standard deviation ($N$ divisor), CCI follows Lambert's 0.015 mean absolute deviation, Parabolic SAR enforces the two-prior-bars constraint and reversal reset rules, Stochastic separates %K slowing summation from %D smoothing, Alligator applies forward shifts on median SMMA, Williams %R produces negative values $[-100, 0]$, and Force Index implements the terminal formula ($\text{Volume} \times \Delta\text{MA}$). However, two significant defects exist: `Mql5IndicatorFactory` parameter coercion silently misaligns argument lists when MQL5 calls include leading timeframe parameters with omitted trailing defaults (e.g., treating timeframe 16385 as MA period), and `iADX` conflates standard ADX (EMA-based in MT5) with `iADXWilder` (SMMA-based). Additionally, `RollingWindow` contains a flaw where indexing on a partially filled window reads uninitialized buffer memory.

## Findings

### [P1] Parameter truncation in `Mql5IndicatorFactory.Numeric` misaligns arguments when timeframe is passed with omitted trailing parameters
- **Where:** `src/Runtime/YO4X.Mql5.Engine/Indicators/Mql5IndicatorFactory.cs:318-322`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
  // Anything beyond the indicator's own arity is a leading symbol or timeframe argument.
  while (values.Count > expected)
  {
      values.RemoveAt(0);
  }
  ```
- **Failure:** In MQL5, indicator functions take `(symbol, timeframe, ...params)` where trailing parameters have default values. When an EA invokes an indicator with symbol, timeframe, and a subset of parameters (for instance `iMA("EURUSD", PERIOD_H1, 20)` where `PERIOD_H1 = 16385`), `TryCoerce` produces `values = [16385.0, 20.0]`. Because `expected = 4` and `values.Count (2) <= expected (4)`, `Numeric` does not strip the leading timeframe. `CreateMovingAverage` then assigns `Period = 16385` and `Shift = 20` instead of `Period = 20` and `Shift = 0`. Similarly, `iMACD("EURUSD", PERIOD_M15, 12, 26, 9)` yields `fastPeriod = 15`, `slowPeriod = 12`, and `signalPeriod = 26`, corrupting the indicator completely.
- **Fix:** Detect leading timeframe/symbol arguments based on parameter position and argument types (e.g., checking if the first argument is a string or null symbol, and the second is a timeframe enum/integer) rather than relying on `values.Count > expected`.

### [P1] `iADX` incorrectly computes SMMA (Wilder) smoothing instead of canonical MetaTrader 5 EMA smoothing
- **Where:** `src/Runtime/YO4X.Mql5.Engine/Indicators/Mql5IndicatorFactory.cs:65-66` and `src/Runtime/YO4X.Mql5.Engine/Indicators/Mql5AdxIndicator.cs:46-49`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
  "iadx" => CreateAdx(parameters, "iADX"),
  "iadxwilder" => CreateAdx(parameters, "iADXWilder"),
  ```
  ```csharp
  trueRange = new MovingAverageCalculator(Period, Mql5MaMethod.Smma);
  plusMovement = new MovingAverageCalculator(Period, Mql5MaMethod.Smma);
  minusMovement = new MovingAverageCalculator(Period, Mql5MaMethod.Smma);
  directionalIndex = new MovingAverageCalculator(Period, Mql5MaMethod.Smma);
  ```
- **Failure:** In MetaTrader 5, `iADX` and `iADXWilder` are distinct indicators with different smoothing formulas. `iADXWilder` adheres strictly to Welles Wilder's SMMA smoothing ($\alpha = 1/N$), whereas the standard MetaTrader 5 `iADX` uses Exponential Moving Average (EMA, $\alpha = \frac{2}{N+1}$) smoothing for True Range, $+DM$, $-DM$, and $DX$. `Mql5IndicatorFactory` maps both `iADX` and `iADXWilder` to `Mql5AdxIndicator` which hardcodes `Mql5MaMethod.Smma`. For a standard 14-period ADX, `iADX` computes smoothing with $\alpha = 1/14 \approx 0.0714$ instead of canonical MT5 EMA $\alpha = 2/15 \approx 0.1333$, making `iADX` significantly more lagging than in MetaTrader 5.
- **Fix:** Allow `Mql5AdxIndicator` to accept a smoothing method (`Mql5MaMethod.Ema` for `iADX` and `Mql5MaMethod.Smma` for `iADXWilder`).

### [P2] `RollingWindow` indexer reads uninitialized slots and computes incorrect sum when partially filled
- **Where:** `src/Runtime/YO4X.Mql5.Engine/Indicators/RollingWindow.cs:23-45`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
  internal double this[int index] => items[(head + index) % items.Length];

  internal void Add(double value)
  {
      if (!IsFull)
      {
          Count++;
      }

      items[head] = value;
      head = (head + 1) % items.Length;

      // Summed afresh rather than carried incrementally. Adding the new value and
      // subtracting the evicted one drifts by ~1e-13 over a long backtest, which is
      // enough to stop a flat series producing an exactly zero deviation - CCI then
      // divides by that residue and reports a large value where it should report zero.
      double sum = 0.0;
      for (int index = 0; index < Count; index++)
      {
          sum += this[index];
      }

      Sum = sum;
  }
  ```
- **Failure:** When `Count < items.Length`, inserted items reside at indices `0 .. head - 1`. The indexer `this[index]` computes `(head + index) % items.Length`, which reads uninitialized slots (starting at `items[head] == 0.0`). On the first `Add(10.0)` into a window with capacity 3, `this[0]` accesses `items[1]` (which is `0.0`), setting `Sum = 0.0` instead of `10.0`. If any caller accesses `window[index]`, `window.Average()`, `window.Highest()`, `window.Lowest()`, `window.PopulationStandardDeviation()`, or `window.MeanAbsoluteDeviation()` before `IsFull` is true, the returned value reads uninitialized zero-slots rather than actual data.
- **Fix:** Update indexer calculation to `items[(head - Count + items.Length + index) % items.Length]` so index 0 points to the oldest stored item during both partial-fill and full phases.

## Referrals
None.

## Coverage gaps
- `src/Runtime/YO4X.Mql5.Engine/Indicators/Mql5IndicatorFactory.cs:318`: Calling `Mql5IndicatorFactory.Create` with MQL5 standard signatures containing symbol + timeframe and omitted trailing arguments (e.g. `iMA("EURUSD", PERIOD_H1, 20)` or `iMACD("EURUSD", PERIOD_M15, 12, 26, 9)`) is untested, leaving argument alignment defects undetected.
- `src/Runtime/YO4X.Mql5.Engine/Indicators/RollingWindow.cs:48-117`: Calling `RollingWindow.Average()`, `Highest()`, `Lowest()`, `PopulationStandardDeviation()`, or `MeanAbsoluteDeviation()` on a partially filled window (`Count < Capacity`) is untested across test suites because tests exclusively inspect full windows.
- `src/Runtime/YO4X.Mql5.Engine/Indicators/Mql5StochasticIndicator.cs:80`: The flat-market zero-denominator branch (`smoothedDenominator <= 0.0`), which assigns the 50.0 neutral midpoint, is untested.


--- agy: gemini-3.7-flash-high | effort=high | mode=plan (read-only) | 245.2s | 295068 tok | id=d6ae94f1-daca-460f-ac2e-d2a874921e2e
