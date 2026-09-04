You are a fix agent on YO4X, a LIVE MetaTrader 5 / MQL5 algorithmic trading platform (.NET 10 backend, React frontend, an MQL5-to-C# transpiler, a deterministic backtest engine). An audit found defects in ONE file. Fix them.

THE ONLY FILE YOU MAY MODIFY:
  src/Runtime/YO4X.Mql5.Runtime/Mql5Runtime.MarketData.cs

Read that file completely first. You may read any other file for context, but you must not edit any other file, create files, delete files, or run commands.

FINDINGS TO FIX (4):

[1] [P0] ArraySetAsSeries flag is silently destroyed when Copy* functions resize target buffer
    Where:   src/Runtime/YO4X.Mql5.Runtime/Mql5Runtime.MarketData.cs:390
    Failure: A strategy flags a dynamic array as a timeseries via `ArraySetAsSeries(rates, true)` (e.g. starting with `rates = []` or size 0) and calls `CopyClose("EURUSD", PERIOD_M1, 0, 10, ref rates)`. `Array.Resize(ref buffer, 10)` allocates a new array instance. Because `ConditionalWeakTable<object, SeriesFlag>` keys by object reference, the series flag is not transferred to the new array instance. When `Finish` checks `IsSeriesArray(target)`, it returns `false` and skips `Array.Reverse`. The caller receives chronological (oldest-first) data where `rates[0]` is the bar from 10 periods ago instead of the current bar, and `ArrayGetAsSeries(rates)` now returns `false`.
    Suggested fix: Check `bool series = IsSeriesArray(buffer)` prior to `Array.Resize` in `CopySeriesCore` and `CopyRatesCore`, and call `SetSeriesArray(buffer, true)` if the original array was flagged, or delegate array resizing to `ArrayResize` which already handles flag propagation.

[2] [P1] Dynamic destination arrays are not resized to actual copied element count
    Where:   src/Runtime/YO4X.Mql5.Runtime/Mql5Runtime.MarketData.cs:388
    Failure: A caller passes a dynamic array to `CopyRates` or `CopyClose` requesting 50 bars (`range.Count = 50`), but the broker history only contains 10 bars. `buffer` is resized to 50, 10 elements are populated, and `written = 10` is returned. `target` remains length 50 with 40 unpopulated zero/default entries. Strategies iterating over `ArraySize(target)` or checking bounds process 40 ghost bars with zero prices/timestamps, corrupting indicator computations.
    Suggested fix: If `written >= 0` and `buffer.Length != written` on a dynamic array target, resize `buffer` to `written` elements before returning from `Finish`.

[3] [P2] `Finish` sets `ERR_INDICATOR_DATA_NOT_FOUND` (4806) for market price and history copy failures
    Where:   src/Runtime/YO4X.Mql5.Runtime/Mql5Runtime.MarketData.cs:416
    Failure: When market history access built-ins (`CopyRates`, `CopyTime`, `CopyOpen`, `CopyHigh`, `CopyLow`, `CopyClose`, `CopySpread`, `CopyTickVolume`, `CopyRealVolume`) fail due to unavailable price history, `Finish` records error `4806` (`ERR_INDICATOR_DATA_NOT_FOUND`). In canonical MQL5, history access built-ins set history error codes such as `4401` (`ERR_HISTORY_NOT_FOUND`). Strategies inspecting `GetLastError()` to branch on missing history data receive an indicator error instead.
    Suggested fix: Distinguish indicator buffer queries from market data queries so history copies set `Mql5ErrorCodes.MarketNotSelected` or a dedicated history error code on failure.

[4] [P3] Finish records indicator error code for market data timeseries failures
    Where:   src/Runtime/YO4X.Mql5.Runtime/Mql5Runtime.MarketData.cs:416
    Failure: When `CopyRates`, `CopyOpen`, or `CopyTime` fails (e.g. symbol not found or history not yet loaded), `Finish` records error 4806 (`ERR_INDICATOR_DATA_NOT_FOUND`). Strategies inspecting `GetLastError()` to identify market/history synchronization issues expect history error codes (e.g. 4401 `ERR_HISTORY_NOT_FOUND` or 4301 `ERR_MARKET_UNKNOWN_SYMBOL`) and fail to match on indicator-specific error codes.
    Suggested fix: Differentiate between indicator buffer copies and price series copies in `Finish` to set the appropriate market data error code.

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

