You are a fix agent on YO4X, a LIVE MetaTrader 5 / MQL5 algorithmic trading platform (.NET 10 backend, React frontend, an MQL5-to-C# transpiler, a deterministic backtest engine). An audit found defects in ONE file. Fix them.

THE ONLY FILE YOU MAY MODIFY:
  src/Runtime/YO4X.Mql5.Engine/Indicators/Mql5IndicatorFactory.cs

Read that file completely first. You may read any other file for context, but you must not edit any other file, create files, delete files, or run commands.

FINDINGS TO FIX (3):

[1] [P1] Mql5IndicatorFactory.CreateAtr uses arity 2 instead of 1, corrupting ATR period with timeframe constant
    Where:   src/Runtime/YO4X.Mql5.Engine/Indicators/Mql5IndicatorFactory.cs:125
    Failure: In MQL5, `iATR` takes 3 parameters: `(string symbol, ENUM_TIMEFRAMES period, int ma_period)`. There is only 1 indicator calculation parameter (`ma_period`). When an EA calls `iATR("EURUSD", PERIOD_H1, 14)`, `parameters` contains `["EURUSD", 16385, 14]`. `Numeric(parameters, 2)` strips the string symbol but retains both numbers because `expected` is set to `2`. As a result, `values[0]` (`16385`) is passed as `period`, and `values[1]` (`14`) is passed as `smoothing`. An ATR indicator with a 16,385-bar period is instantiated instead of a 14-bar ATR, returning `EmptyValue` (0.0) across backtests and disabling indicator-dependent trade logic.
    Suggested fix: Change `Numeric(parameters, 2)` to `Numeric(parameters, 1)` in `CreateAtr` so that leading timeframe constants are stripped when resolving the ATR period.

[2] [P1] Parameter truncation in `Mql5IndicatorFactory.Numeric` misaligns arguments when timeframe is passed with omitted trailing parameters
    Where:   src/Runtime/YO4X.Mql5.Engine/Indicators/Mql5IndicatorFactory.cs:318-322
    Failure: In MQL5, indicator functions take `(symbol, timeframe, ...params)` where trailing parameters have default values. When an EA invokes an indicator with symbol, timeframe, and a subset of parameters (for instance `iMA("EURUSD", PERIOD_H1, 20)` where `PERIOD_H1 = 16385`), `TryCoerce` produces `values = [16385.0, 20.0]`. Because `expected = 4` and `values.Count (2) <= expected (4)`, `Numeric` does not strip the leading timeframe. `CreateMovingAverage` then assigns `Period = 16385` and `Shift = 20` instead of `Period = 20` and `Shift = 0`. Similarly, `iMACD("EURUSD", PERIOD_M15, 12, 26, 9)` yields `fastPeriod = 15`, `slowPeriod = 12`, and `signalPeriod = 26`, corrupting the indicator completely.
    Suggested fix: Detect leading timeframe/symbol arguments based on parameter position and argument types (e.g., checking if the first argument is a string or null symbol, and the second is a timeframe enum/integer) rather than relying on `values.Count > expected`.

[3] [P1] `iADX` incorrectly computes SMMA (Wilder) smoothing instead of canonical MetaTrader 5 EMA smoothing
    Where:   src/Runtime/YO4X.Mql5.Engine/Indicators/Mql5IndicatorFactory.cs:65-66 and src/Runtime/YO4X.Mql5.Engine/Indicators/Mql5AdxIndicator.cs:46-49
    Failure: In MetaTrader 5, `iADX` and `iADXWilder` are distinct indicators with different smoothing formulas. `iADXWilder` adheres strictly to Welles Wilder's SMMA smoothing ($\alpha = 1/N$), whereas the standard MetaTrader 5 `iADX` uses Exponential Moving Average (EMA, $\alpha = \frac{2}{N+1}$) smoothing for True Range, $+DM$, $-DM$, and $DX$. `Mql5IndicatorFactory` maps both `iADX` and `iADXWilder` to `Mql5AdxIndicator` which hardcodes `Mql5MaMethod.Smma`. For a standard 14-period ADX, `iADX` computes smoothing with $\alpha = 1/14 \approx 0.0714$ instead of canonical MT5 EMA $\alpha = 2/15 \approx 0.1333$, making `iADX` significantly more lagging than in MetaTrader 5.
    Suggested fix: Allow `Mql5AdxIndicator` to accept a smoothing method (`Mql5MaMethod.Ema` for `iADX` and `Mql5MaMethod.Smma` for `iADXWilder`).

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

