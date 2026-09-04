---
agent_id: F22
lane: engine-ind-a-f
scope:
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
status: COMPLETE
generated: 2026-08-29T08:28:00Z
counts: { P0: 0, P1: 1, P2: 0, P3: 0 }
---

# F22 — engine-ind-a-f

## Scope audited
- `src/Runtime/YO4X.Mql5.Engine/Indicators/Mql5AdxIndicator.cs` (107 lines) — Wilder SMMA smoothing for TR/DM/DX, directional movement positive/larger rule, buffer layout (MAIN, +DI, -DI), zero-division guards.
- `src/Runtime/YO4X.Mql5.Engine/Indicators/Mql5AlligatorIndicator.cs` (71 lines) — Jaw (13/8), Teeth (8/5), Lips (5/3) periods and forward shift displacements on median price, buffer layout (JAW, TEETH, LIPS).
- `src/Runtime/YO4X.Mql5.Engine/Indicators/Mql5AtrIndicator.cs` (53 lines) — True Range 3-way max calculation, first-bar fallback, Wilder SMMA recursive smoothing.
- `src/Runtime/YO4X.Mql5.Engine/Indicators/Mql5AwesomeOscillatorIndicator.cs` (35 lines) — SMA(5, Median) - SMA(34, Median), 34-bar warm-up delay.
- `src/Runtime/YO4X.Mql5.Engine/Indicators/Mql5BandsIndicator.cs` (58 lines) — Population standard deviation divisor, forward shift displacement, buffer layout (BASE, UPPER, LOWER).
- `src/Runtime/YO4X.Mql5.Engine/Indicators/Mql5CciIndicator.cs` (45 lines) — Mean absolute deviation, 0.015 Lambert constant multiplier, zero-deviation guard.
- `src/Runtime/YO4X.Mql5.Engine/Indicators/Mql5DeMarkerIndicator.cs` (68 lines) — DeMax and DeMin extrema, SMA smoothing over period, zero-sum guard.
- `src/Runtime/YO4X.Mql5.Engine/Indicators/Mql5EnvelopesIndicator.cs` (61 lines) — Percentage deviation bands, forward shift displacement, buffer layout (UPPER, LOWER).
- `src/Runtime/YO4X.Mql5.Engine/Indicators/Mql5ForceIndexIndicator.cs` (72 lines) — Volume-price product smoothing vs price-only smoothing.
- `src/Runtime/YO4X.Mql5.Engine/Indicators/Mql5FractalsIndicator.cs` (87 lines) — 5-bar peak/trough pattern, 2-bar revision delay to avoid look-ahead bias, buffer layout (UPPER, LOWER).

## Verdict
The indicator suite is overwhelmingly mathematically faithful to MetaTrader 5 specifications. ADX and ATR correctly implement Wilder recursive smoothing (SMMA) seeded with simple window sums; Bollinger Bands properly use population standard deviation; CCI correctly applies Lambert's 0.015 multiplier over mean absolute deviation; Alligator enforces all three forward shifts on median price; and Fractals confirm only after 2 subsequent bars without look-ahead bias. However, `Mql5ForceIndexIndicator` exhibits a significant formula deviation for periods > 1 by smoothing price and scaling by current bar volume instead of smoothing the volume-price product.

## Findings

### [P1] Force Index smooths price instead of volume-price product, discarding historical volume weighting
- **Where:** `src/Runtime/YO4X.Mql5.Engine/Indicators/Mql5ForceIndexIndicator.cs:67`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
        double force = bar.TickVolume * (current - previousAverage);
        previousAverage = current;
        Push(0, force);
  ```
- **Failure:** In MetaTrader 5 and Alexander Elder's canonical definition, Force Index is calculated by first taking the raw 1-bar volume-price product `RawForce = Volume * (Close - Close[1])` and then smoothing that product series with a Moving Average of period $N$ (`MA(RawForce, N)`). `Mql5ForceIndexIndicator` instead feeds raw `Close` into `MovingAverageCalculator(Period, method)` and multiplies the moving average step by the current bar's tick volume `bar.TickVolume * (MA(Close) - MA(Close)[1])`. When $N > 1$, this completely discards the volume weighting of earlier bars in the period. For example, with $N=3$ SMA and bars (Close=10, Vol=100), (Close=12, Vol=1000), (Close=12, Vol=50), (Close=12, Vol=50), canonical MT5 computes `(1000*2 + 50*0 + 50*0)/3 = +666.67`, whereas YO4X computes `50 * (12.0 - 11.333) = +33.33` (a 20x error). If the current bar has zero volume during an ongoing trend, YO4X outputs 0 regardless of previous high-volume moves.
- **Fix:** Prime the previous bar's close, compute `double rawForce = bar.TickVolume * (bar.Close - previousClose)` for each subsequent bar, and pass `rawForce` to `average.Add(rawForce)` to smooth the volume-price product directly.

## Referrals
- `src/Runtime/YO4X.Mql5.Engine/Indicators/RollingWindow.cs` — `RollingWindow.this[int index]` uses `items[(head + index) % items.Length]`, which accesses uninitialized zero slots when `Count < items.Length` (before the ring buffer is full), corrupting `Highest()`, `Lowest()`, and indexed lookups on partially-filled windows (Lane F24).

## Coverage gaps
- `src/Runtime/YO4X.Mql5.Engine/Indicators/Mql5ForceIndexIndicator.cs:53-70` — Unit test `ForceIndexIsVolumeTimesTheMovingAverageStep` in `IndicatorExpansionAccuracyTests.cs:239` only exercises `period = 1`, failing to test `period > 1` (e.g. 13-period EMA) where the formula divergence occurs.
- `src/Runtime/YO4X.Mql5.Engine/Indicators/Mql5AdxIndicator.cs:91-95` — Zero-division guard branches for `smoothedRange == 0.0` and `total == 0.0` (e.g. during extended flat zero-volatility price series) are not covered by unit tests.
- `src/Runtime/YO4X.Mql5.Engine/Indicators/Mql5BandsIndicator.cs:52-55` — Forward shift `shift > 0` path in Bollinger Bands is not exercised in `IndicatorAccuracyTests.cs:186` (which tests `shift = 0`).


--- agy: gemini-3.7-flash-high | effort=high | mode=plan (read-only) | 168.1s | 275698 tok | id=d6aa6444-b480-4986-bde1-bac54a47d381
