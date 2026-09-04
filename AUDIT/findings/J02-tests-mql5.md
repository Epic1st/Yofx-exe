---
agent_id: J02
lane: Mql5 Engine and Runtime Tests Audit
scope:
  - tests/YO4X.Mql5.Engine.Tests/**
  - tests/YO4X.Mql5.Runtime.Tests/**
status: COMPLETE
generated: 2026-08-29T11:40:00Z
counts: { P0: 0, P1: 0, P2: 2, P3: 1 }
---

# J02 — Mql5 Engine and Runtime Tests Audit

## Scope audited
Opened and audited all 26 files within the lane scope:
- `tests/YO4X.Mql5.Engine.Tests/BrokerAccountingTests.cs` (297 lines)
- `tests/YO4X.Mql5.Engine.Tests/BrokerExecutionTests.cs` (281 lines)
- `tests/YO4X.Mql5.Engine.Tests/EngineTestSupport.cs` (155 lines)
- `tests/YO4X.Mql5.Engine.Tests/IndicatorAccuracyTests.cs` (306 lines)
- `tests/YO4X.Mql5.Engine.Tests/IndicatorExpansionAccuracyTests.cs` (589 lines)
- `tests/YO4X.Mql5.Engine.Tests/MarketContextTests.cs` (206 lines)
- `tests/YO4X.Mql5.Engine.Tests/MarketFeedTests.cs` (163 lines)
- `tests/YO4X.Mql5.Engine.Tests/PropertyIdentifierFidelityTests.cs` (99 lines)
- `tests/YO4X.Mql5.Engine.Tests/StrategyHostTests.cs` (409 lines)
- `tests/YO4X.Mql5.Engine.Tests/YO4X.Mql5.Engine.Tests.csproj` (29 lines)
- `tests/YO4X.Mql5.Runtime.Tests/BuiltinNameResolutionTests.cs` (71 lines)
- `tests/YO4X.Mql5.Runtime.Tests/ByReferenceShapeTests.cs` (134 lines)
- `tests/YO4X.Mql5.Runtime.Tests/FakeMarketContext.cs` (114 lines)
- `tests/YO4X.Mql5.Runtime.Tests/Mql5ArrayTests.cs` (283 lines)
- `tests/YO4X.Mql5.Runtime.Tests/Mql5CalendarTypesTests.cs` (58 lines)
- `tests/YO4X.Mql5.Runtime.Tests/Mql5ConversionTests.cs` (276 lines)
- `tests/YO4X.Mql5.Runtime.Tests/Mql5EngineSurfaceTests.cs` (399 lines)
- `tests/YO4X.Mql5.Runtime.Tests/Mql5EnvironmentTests.cs` (439 lines)
- `tests/YO4X.Mql5.Runtime.Tests/Mql5FormatTests.cs` (256 lines)
- `tests/YO4X.Mql5.Runtime.Tests/Mql5MathTests.cs` (221 lines)
- `tests/YO4X.Mql5.Runtime.Tests/Mql5StringTests.cs` (205 lines)
- `tests/YO4X.Mql5.Runtime.Tests/Mql5TypeInfoTests.cs` (115 lines)
- `tests/YO4X.Mql5.Runtime.Tests/ParameterTypeShapeTests.cs` (162 lines)
- `tests/YO4X.Mql5.Runtime.Tests/StandardLibraryContext.cs` (198 lines)
- `tests/YO4X.Mql5.Runtime.Tests/StandardLibraryTests.cs` (434 lines)
- `tests/YO4X.Mql5.Runtime.Tests/YO4X.Mql5.Runtime.Tests.csproj` (28 lines)

## Verdict
The test suites are exceptionally disciplined and well-structured, featuring hand-derived mathematical ground truths across almost all indicators (SMA, EMA, SMMA, LWMA, RSI, ATR, Bands, CCI, ADX, StdDev, Momentum, WPR, AO, DeMarker, Force, Envelopes, Fractals, Alligator, SAR, RVI, and OsMA) with high floating-point precision (10 decimal places). Broker order execution, netting/hedging accounting, series reversal, and runtime error code propagation are comprehensively verified. However, two test oracle issues exist where an assertion tests an implementation against itself (MACD) or locks in double precision truncation artifacts (`NormalizeDouble`), and shape verification tests use internal C# reflection rather than official MQL5 specifications as their oracle.

## Findings

### [P2] Self-referential test oracle in MACD accuracy verification
- **Where:** `tests/YO4X.Mql5.Engine.Tests/IndicatorAccuracyTests.cs:237-246`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
              if (index >= 3)
              {
                  Assert.Equal(fast.Value(0, 0) - slow.Value(0, 0), macd.Value(0, 0), 10);
              }
          }

          // Signal is a two-bar simple average of the main line, matching the bundled MetaTrader MACD.
          double previousMain = macd.Value(0, 1);
          double currentMain = macd.Value(0, 0);
          Assert.Equal((previousMain + currentMain) / 2.0, macd.Value(1, 0), 10);
  ```
- **Failure:** Unlike all other indicator tests which verify against explicit hand-calculated constants, `MacdMainLineIsTheDifferenceOfTheTwoExponentialAverages` checks `macd.Value(0, 0)` against `fast.Value(0, 0) - slow.Value(0, 0)` (two other instances sharing `MovingAverageCalculator`) and checks the signal buffer by reading `macd.Value(0, 1)` and `macd.Value(0, 0)` directly from the indicator under test. If `MovingAverageCalculator` contains an error in EMA smoothing multipliers, seeding, or warmup delays, both sides of the assertion evaluate to the identical wrong value and the test passes.
- **Fix:** Replace the dynamic self-comparison with fixed, hand-computed numbers for `macd.Value(0, 0)` and `macd.Value(1, 0)` on the `Sample` price series (matching the approach used for `OsMaIsTheMacdMainLineLessItsSignal` in `IndicatorExpansionAccuracyTests`).

### [P2] NormalizeDouble test locks in floating-point truncation artifact instead of true half-away-from-zero rounding
- **Where:** `tests/YO4X.Mql5.Runtime.Tests/Mql5ConversionTests.cs:21-22`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
      [InlineData(0.1 + 0.2, 2, 0.3)]
      [InlineData(1.005, 2, 1.0)]
      [InlineData(123.456, 0, 123.0)]
  ```
- **Failure:** In financial trade price normalization with `digits = 2`, rounding `1.005` half away from zero should yield `1.01`. In .NET, IEEE 754 binary representation causes `1.005` to be stored as `1.004999999999999893...`, which causes `Math.Round(1.005, 2, MidpointRounding.AwayFromZero)` to round down to `1.0`. The test asserts `expected = 1.0`, codifying the runtime's double precision binary truncation defect instead of asserting the correct financial rounding oracle.
- **Fix:** Update the test expectation to `[InlineData(1.005, 2, 1.01)]` and adjust the production `NormalizeDouble` implementation to use an epsilon offset or decimal scaling when identifying midpoint ties.

### [P3] Emitter shape test suites use runtime reflection rather than MQL5 specification as their oracle
- **Where:** `tests/YO4X.Mql5.Runtime.Tests/ByReferenceShapeTests.cs:23-30`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
      [Fact]
      public void RecordedShapesMatchTheRuntimeInterface()
      {
          Dictionary<string, string> derived = Derive();

          var differences = new List<string>();

          foreach ((string name, string expected) in derived)
          {
              if (!Mql5ClrTypes.RuntimeByReferenceParameters.TryGetValue(name, out string? recorded))
  ```
- **Failure:** `ByReferenceShapeTests` (and `ParameterTypeShapeTests:23-28`) dynamically inspect `typeof(IMql5Runtime).GetMethods()` at runtime and verify that `Mql5ClrTypes` matches the C# runtime interface. If an `IMql5Runtime` method is accidentally declared with by-value parameters or an incorrect type relative to the official MQL5 specification, both `Derive()` and `Mql5ClrTypes` agree with the corrupted interface, allowing signature drift away from MetaTrader 5 to pass without error.
- **Fix:** Verify `Mql5ClrTypes.RuntimeByReferenceParameters` and `RuntimeParameterTypes` against `Mql5BuiltinCatalog.All` signatures rather than reflecting `IMql5Runtime`.

## Referrals
- `src/Runtime/YO4X.Mql5.Runtime/Mql5Runtime.Conversion.cs:169` — `NormalizeDouble` performs direct binary `Math.Round` on `double`, failing exact decimal half-step round-ups (e.g. `1.005 -> 1.0` instead of `1.01`).
- `src/Runtime/YO4X.Mql5.Engine/Indicators/Mql5IndicatorFactory.cs:18-41` — Factory and engine lack implementations for 17 catalogued indicators declared in `IMql5Runtime` (`iAC`, `iAD`, `iAMA`, `iBearsPower`, `iBullsPower`, `iBWMFI`, `iChaikin`, `iDEMA`, `iFrAMA`, `iGator`, `iIchimoku`, `iMFI`, `iOBV`, `iTEMA`, `iTriX`, `iVIDyA`, `iVolumes`).

## Coverage gaps
- `src/Runtime/YO4X.Mql5.Engine/Indicators/Mql5RsiIndicator.cs:75` — The zero-change branch (`averageLoss <= 0.0 && averageGain <= 0.0 ? 50.0 : ...`) when input price is completely flat is untested in `IndicatorAccuracyTests`.
- `src/Runtime/YO4X.Mql5.Engine/Indicators/Mql5StochasticIndicator.cs:80` — The degenerate zero-range branch (`smoothedDenominator <= 0.0 ? 50.0 : ...`) when high equals low across the entire window is untested in `IndicatorAccuracyTests`.
- `src/Runtime/YO4X.Mql5.Engine/Indicators/Mql5WilliamsPercentRangeIndicator.cs:44` — The zero-range fallback branch (`range <= 0.0 ? 0.0 : ...`) when high equals low across all bars is untested in `IndicatorExpansionAccuracyTests`.
- `src/Runtime/YO4X.Mql5.Engine/Indicators/Mql5DeMarkerIndicator.cs:65` — The zero-movement fallback branch (`total <= 0.0 ? 0.0 : ...`) when high and low do not move across the window is untested in `IndicatorExpansionAccuracyTests`.
- `src/Runtime/YO4X.Mql5.Runtime/Mql5Runtime.MarketData.cs:21-65` — Single-bar market accessors (`IOpen`, `IHigh`, `ILow`, `IClose`, `IVolume`, `ITickVolume`, `IRealVolume`, `ISpread`, `IHighest`, and `ILowest`) have zero unit tests in `YO4X.Mql5.Runtime.Tests`.
- `src/Runtime/YO4X.Mql5.Runtime/Mql5Runtime.MarketData.cs:385-410` — Bulk series copy functions `CopyRates`, `CopyOpen`, `CopyHigh`, `CopyLow`, `CopyTime`, `CopyTickVolume`, `CopyRealVolume`, and `CopySpread` are not tested in `YO4X.Mql5.Runtime.Tests` (only `CopyClose` is tested).
- `src/Runtime/YO4X.Mql5.Runtime/Mql5Runtime.Indicator.cs:380-385` — Time-range overloads of `CopyBuffer` (`CopyBuffer(int, int, long, int, ref double[])` and `CopyBuffer(int, int, long, long, ref double[])`) are completely untested.
- `src/Runtime/YO4X.Mql5.Runtime/StandardLibrary/Mql5TradeTransaction.cs:13` — The `Mql5TradeTransaction` structure has no unit tests verifying its properties or `typename` resolution in `StandardLibraryTests` or `Mql5TypeInfoTests`.


--- agy: gemini-3.7-flash-high | effort=high | mode=plan (read-only) | 492.2s | 421425 tok | id=871a30bd-59d5-453a-be8e-a0c7f7065790
