---
agent_id: F15
lane: runtime-indicator-terminal-chart
scope:
  - src/Runtime/YO4X.Mql5.Runtime/Mql5Runtime.Indicator.cs
  - src/Runtime/YO4X.Mql5.Runtime/Mql5Runtime.Terminal.cs
  - src/Runtime/YO4X.Mql5.Runtime/Mql5Runtime.Chart.cs
  - src/Runtime/YO4X.Mql5.Runtime/Mql5ChartObjectStore.cs
status: COMPLETE
generated: 2026-08-29T11:28:30Z
counts: { P0: 0, P1: 3, P2: 3, P3: 0 }
---

# F15 — runtime-indicator-terminal-chart

## Scope audited
- [Mql5Runtime.Indicator.cs](file:///C:/Users/Dev23/Desktop/yo4x/src/Runtime/YO4X.Mql5.Runtime/Mql5Runtime.Indicator.cs) (505 lines) — Technical indicator built-ins (`iMA`, `iRSI`, `CopyBuffer`, `IndicatorCreate`, `IndicatorRelease`, plot stubs).
- [Mql5Runtime.Terminal.cs](file:///C:/Users/Dev23/Desktop/yo4x/src/Runtime/YO4X.Mql5.Runtime/Mql5Runtime.Terminal.cs) (470 lines) — Terminal state queries, simulated tick counters, `ZeroMemory`, logging, and unsupported environment built-ins.
- [Mql5Runtime.Chart.cs](file:///C:/Users/Dev23/Desktop/yo4x/src/Runtime/YO4X.Mql5.Runtime/Mql5Runtime.Chart.cs) (693 lines) — Chart property accessors and graphical object stub built-ins (`ObjectCreate`, `ObjectSet*`, `ObjectGet*`, `ChartSet*`, `ChartGet*`).
- [Mql5ChartObjectStore.cs](file:///C:/Users/Dev23/Desktop/yo4x/src/Runtime/YO4X.Mql5.Runtime/Mql5ChartObjectStore.cs) (360 lines) — In-memory state store backing graphical object and chart property recordings.

## Verdict
The architectural seam between the runtime and the market context is well-conceived, but three serious semantic bugs affect indicator lifecycle and data retrieval: releasing an indicator does not evict it from the runtime handle cache (leading to stale handle reuse), `IndicatorCreate` completely drops the `symbol` parameter and misaligns arguments passed to the engine, and `CopyBufferCore` discards the `ArraySetAsSeries` timeseries flag whenever buffer reallocation occurs. Chart object storage records properties effectively but decouples creation anchor points from subsequent property reads and omits `LastError` propagation on `ObjectMove`.

## Findings

### [P1] `IndicatorRelease` does not evict handle from runtime cache, returning dead handles on re-creation
- **Where:** [Mql5Runtime.Indicator.cs:369](file:///C:/Users/Dev23/Desktop/yo4x/src/Runtime/YO4X.Mql5.Runtime/Mql5Runtime.Indicator.cs#L369)
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
  /// <inheritdoc />
  public bool IndicatorRelease(int indicatorHandle) => context.IndicatorRelease(indicatorHandle);
  ```
- **Failure:** An MQL5 strategy initializes an indicator `int h = iMA(NULL, 0, 14, 0, MODE_SMA, PRICE_CLOSE);`. `Handle(...)` caches `indicatorHandles[key] = h` and returns `h`. When the strategy releases the indicator via `IndicatorRelease(h)`, `context.IndicatorRelease(h)` destroys the indicator on the engine, but `indicatorHandles` retains `key -> h`. If the strategy later calls `iMA(NULL, 0, 14, 0, MODE_SMA, PRICE_CLOSE)` again, `Handle(...)` returns the cached `h` without requesting a new handle from `context`. Subsequent calls to `CopyBuffer(h, ...)` or `BarsCalculated(h)` fail because the engine no longer recognizes `h`.
- **Fix:** Upon a successful return from `context.IndicatorRelease(indicatorHandle)`, remove the corresponding entry from `indicatorHandles`.

### [P1] `IndicatorCreate` drops `symbol` argument and passes misaligned arguments to market context
- **Where:** [Mql5Runtime.Indicator.cs:349-366](file:///C:/Users/Dev23/Desktop/yo4x/src/Runtime/YO4X.Mql5.Runtime/Mql5Runtime.Indicator.cs#L349-L366)
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
  public int IndicatorCreate(string? symbol, int period, int indicatorType, Mql5Param[]? parameters = null)
  {
      object[] arguments = new object[2 + (parameters?.Length ?? 0)];
      arguments[0] = Timeframe(period);
      arguments[1] = indicatorType;
      for (int index = 0; index < (parameters?.Length ?? 0); index++)
      {
          arguments[2 + index] = parameters![index];
      }

      int handle = context.IndicatorHandle("IndicatorCreate", arguments);
  ```
- **Failure:** A strategy calls `IndicatorCreate("EURUSD", PERIOD_H1, IND_MA, params)`. The `symbol` argument is ignored. `arguments[0]` is assigned the integer timeframe and `arguments[1]` the indicator type. `context.IndicatorHandle` receives timeframe where symbol is expected, losing the target symbol entirely and misaligning parameter indexing for the engine.
- **Fix:** Resolve `symbol` with `Resolve(symbol)` and populate `arguments[0] = resolvedSymbol`, `arguments[1] = Timeframe(period)`, `arguments[2] = indicatorType`, followed by `parameters` in an array of size `3 + parameters.Length`.

### [P1] `CopyBufferCore` drops `ArraySetAsSeries` flag on buffer resize, leaving timeseries data unreversed
- **Where:** [Mql5Runtime.Indicator.cs:449-458](file:///C:/Users/Dev23/Desktop/yo4x/src/Runtime/YO4X.Mql5.Runtime/Mql5Runtime.Indicator.cs#L449-L458)
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
  double[] target = buffer ?? [];
  if (range.Kind != Mql5CopyRangeKind.TimeRange && range.Count > 0 && target.Length < range.Count)
  {
      Array.Resize(ref target, range.Count);
  }

  int written = context.CopyBufferRange(indicatorHandle, bufferNumber, range, ref target);
  buffer = target;
  return Finish(written, buffer);
  ```
- **Failure:** A strategy initializes an empty dynamic buffer `double buf[] = [];`, marks it as a timeseries via `ArraySetAsSeries(buf, true)`, and calls `CopyBuffer(h, 0, 0, 10, ref buf)`. Because `target.Length` (0) < `range.Count` (10), `Array.Resize(ref target, 10)` creates a new array instance. `ConditionalWeakTable` tracks series flags by object reference, so the resized array is not flagged in `seriesFlags`. `Finish` sees `IsSeriesArray(buffer) == false` and skips `Array.Reverse`. `buf[0]` contains the oldest bar value instead of the current bar value, and `buf` permanently loses its timeseries indexing behavior.
- **Fix:** Check `bool isSeries = IsSeriesArray(target);` before resizing, and re-apply `SetSeriesArray(target, true)` after `Array.Resize` if `isSeries` was true.

### [P2] `ElapsedMilliseconds` truncates `TimeCurrent` to whole seconds, destroying sub-second tick count resolution
- **Where:** [Mql5Runtime.Terminal.cs:462-468](file:///C:/Users/Dev23/Desktop/yo4x/src/Runtime/YO4X.Mql5.Runtime/Mql5Runtime.Terminal.cs#L462-L468)
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
  private long ElapsedMilliseconds()
  {
      long now = Mql5Time.FromDateTime(context.TimeCurrent);
      clockBaseline ??= now;
      long elapsed = now - clockBaseline.Value;
      return elapsed < 0 ? 0 : elapsed * 1000;
  }
  ```
- **Failure:** Ticks arriving at `12:00:00.100` and `12:00:00.600` produce identical integer second timestamps from `Mql5Time.FromDateTime`, causing `elapsed` to evaluate to `0`. `GetTickCount()`, `GetTickCount64()`, and `GetMicrosecondCount()` return `0` for all ticks within the same second and jump in 1000 ms increments at whole-second boundaries. Strategies using `GetTickCount()` to throttle operations (e.g. 500 ms delay) measure 0 ms elapsed between intra-second ticks.
- **Fix:** Track `clockBaseline` as a `DateTime` and compute `(long)(context.TimeCurrent - clockBaseline.Value).TotalMilliseconds` directly on the `DateTime` instances.

### [P2] `ObjectMove` does not set `LastError` when targeting a nonexistent chart object
- **Where:** [Mql5Runtime.Chart.cs:275-280](file:///C:/Users/Dev23/Desktop/yo4x/src/Runtime/YO4X.Mql5.Runtime/Mql5Runtime.Chart.cs#L275-L280)
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
  /// <inheritdoc />
  public bool ObjectMove(long chartId, string? name, int pointIndex, long time, double price)
  {
      RecordChartCall(nameof(ObjectMove));
      return ChartObjects.Move(ResolveChartId(chartId), name ?? string.Empty, pointIndex, time, price);
  }
  ```
- **Failure:** An EA calls `ObjectMove(0, "nonexistent", 0, time, price)`. `ChartObjects.Move` returns `false`. Unlike `ObjectDelete`, `ObjectSetInteger`, `ObjectSetDouble`, and `ObjectSetString`, `ObjectMove` does not call `SetError(Mql5ErrorCodes.ObjectNotFound)`. A subsequent call to `GetLastError()` returns `0` (or an unrelated previous code), violating MQL5 error reporting invariants.
- **Fix:** Check the boolean result of `ChartObjects.Move` and call `SetError(Mql5ErrorCodes.ObjectNotFound)` if it returns `false`.

### [P2] `Mql5ChartObjectStore.Create` anchor points are not accessible to `ObjectGetInteger` / `ObjectGetDouble`
- **Where:** [Mql5ChartObjectStore.cs:64-78](file:///C:/Users/Dev23/Desktop/yo4x/src/Runtime/YO4X.Mql5.Runtime/Mql5ChartObjectStore.cs#L64-L78)
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
  internal bool Create(long chartId, string name, int type, int subWindow, IReadOnlyList<(long Time, double Price)> anchors)
  {
      Record();
      ChartState state = State(chartId);
      if (state.Objects.ContainsKey(name))
      {
          return false;
      }

      Mql5ChartObject created = new(name, type, subWindow);
      created.Anchors.AddRange(anchors);
      state.Objects[name] = created;
      state.Ordered.Add(created);
      return true;
  }
  ```
- **Failure:** A strategy creates an object via `ObjectCreate(0, "Line", OBJ_TREND, 0, t1, p1, t2, p2)` and subsequently reads `ObjectGetDouble(0, "Line", OBJPROP_PRICE, 0)`. `ObjectCreate` stores the coordinates in `Anchors`, but `TryGetDouble` only checks `target.Doubles[(propertyId, modifier)]`. Because `target.Doubles` is not populated with the anchor values, `TryGetDouble` returns `0.0` instead of `p1`.
- **Fix:** Mirror initial anchor coordinates into `target.Integers` and `target.Doubles` (or fallback to `Anchors` when querying coordinate properties) during object creation and move operations.

## Referrals
- [Mql5Runtime.MarketData.cs](file:///C:/Users/Dev23/Desktop/yo4x/src/Runtime/YO4X.Mql5.Runtime/Mql5Runtime.MarketData.cs) — `CopySeriesCore` and `CopyRatesCore` resize target buffers without preserving `seriesFlags`, dropping timeseries reversal when dynamic arrays grow.

## Coverage gaps
- `Mql5Runtime.Indicator.cs:369` — No unit test verifies that after calling `IndicatorRelease(handle)`, requesting the same indicator with identical parameters contacts the market context for a fresh handle rather than reusing the released handle.
- `Mql5Runtime.Indicator.cs:349` — No unit test asserts parameter ordering and symbol forwarding in `IndicatorCreate`.
- `Mql5Runtime.Indicator.cs:450` — `CopySeriesReversesOutputForATargetFlaggedAsATimeseries` only tests pre-sized arrays; the dynamic growth branch `target.Length < range.Count` is untested for timeseries arrays.
- `Mql5Runtime.Terminal.cs:462` — `TickCountsAdvanceWithSimulatedTimeAndStartAtZero` only steps time by whole seconds (`AddSeconds(5)`), leaving sub-second precision untested.


--- agy: gemini-3.7-flash-high | effort=high | mode=plan (read-only) | 106.1s | 266639 tok | id=6db554ba-98e7-4f41-b4fb-10aa069886ff
