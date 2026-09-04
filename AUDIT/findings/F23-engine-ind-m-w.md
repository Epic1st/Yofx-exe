---
agent_id: F23
lane: engine-ind-m-w
scope:
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
status: COMPLETE
generated: 2026-08-29T08:28:00Z
counts: { P0: 0, P1: 0, P2: 0, P3: 0 }
---

# F23 — engine-ind-m-w

## Scope audited
- `src/Runtime/YO4X.Mql5.Engine/Indicators/Mql5MacdIndicator.cs` (61 lines)
- `src/Runtime/YO4X.Mql5.Engine/Indicators/Mql5MomentumIndicator.cs` (42 lines)
- `src/Runtime/YO4X.Mql5.Engine/Indicators/Mql5MovingAverageIndicator.cs` (42 lines)
- `src/Runtime/YO4X.Mql5.Engine/Indicators/Mql5OsMaIndicator.cs` (51 lines)
- `src/Runtime/YO4X.Mql5.Engine/Indicators/Mql5ParabolicSarIndicator.cs` (129 lines)
- `src/Runtime/YO4X.Mql5.Engine/Indicators/Mql5RelativeVigorIndexIndicator.cs` (78 lines)
- `src/Runtime/YO4X.Mql5.Engine/Indicators/Mql5RsiIndicator.cs` (83 lines)
- `src/Runtime/YO4X.Mql5.Engine/Indicators/Mql5StdDevIndicator.cs` (64 lines)
- `src/Runtime/YO4X.Mql5.Engine/Indicators/Mql5StochasticIndicator.cs` (85 lines)
- `src/Runtime/YO4X.Mql5.Engine/Indicators/Mql5WilliamsPercentRangeIndicator.cs` (47 lines)

## Verdict
The audited indicator set is mathematically sound, cleanly structured, and faithful to canonical MetaTrader 5 definitions. All trouble spots called out in the charter—Wilder alpha smoothing on RSI gain/loss with zero-loss saturations, MACD oscillator SMA signal smoothing, OsMA differential, Stochastic dual-line slowing and price field switching, Parabolic SAR penetration clamping and reversal rules, negative Williams %R scaling, and population standard deviation—are implemented correctly with proper warm-up NaN management and divide-by-zero guards. No defects were found across the 10 files.

## Findings
None. The implementations strictly match MetaTrader 5 mathematical definitions and Wilder smoothing conventions. Warm-up periods, seeding logic, ring-buffer indexing directions, buffer synchronisation, shift offsets, and divide-by-zero guards are correctly handled.

## Referrals
None.

## Coverage gaps
- `src/Runtime/YO4X.Mql5.Engine/Indicators/Mql5RsiIndicator.cs:75`: The flat market branch (`averageLoss <= 0.0 && averageGain <= 0.0` returning `50.0`) is not covered by unit tests in `IndicatorAccuracyTests.cs`.
- `src/Runtime/YO4X.Mql5.Engine/Indicators/Mql5StochasticIndicator.cs:54`: The `Mql5StochasticPriceField.CloseClose` branch is not exercised in `IndicatorAccuracyTests.cs`.
- `src/Runtime/YO4X.Mql5.Engine/Indicators/Mql5StochasticIndicator.cs:80`: The degenerate zero-range flat market branch (`smoothedDenominator <= 0.0` returning `50.0`) is not covered by unit tests.
- `src/Runtime/YO4X.Mql5.Engine/Indicators/Mql5WilliamsPercentRangeIndicator.cs:44`: The flat market zero-range branch (`range <= 0.0` returning `0.0`) is not exercised in `IndicatorExpansionAccuracyTests.cs`.
- `src/Runtime/YO4X.Mql5.Engine/Indicators/Mql5RelativeVigorIndexIndicator.cs:64`: The zero-range period branch (`denominator == 0.0` falling back to `numerator`) is not covered in `IndicatorExpansionAccuracyTests.cs`.
- `src/Runtime/YO4X.Mql5.Engine/Indicators/Mql5MomentumIndicator.cs:39`: The zero reference price guard (`reference == 0.0` returning `double.NaN`) is not tested.


--- agy: gemini-3.7-flash-high | effort=high | mode=plan (read-only) | 130.8s | 252807 tok | id=a45b5396-0122-4bbf-8692-c47393c500bb
