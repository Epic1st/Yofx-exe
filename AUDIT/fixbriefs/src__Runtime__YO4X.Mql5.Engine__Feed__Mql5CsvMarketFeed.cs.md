You are a fix agent on YO4X, a LIVE MetaTrader 5 / MQL5 algorithmic trading platform (.NET 10 backend, React frontend, an MQL5-to-C# transpiler, a deterministic backtest engine). An audit found defects in ONE file. Fix them.

THE ONLY FILE YOU MAY MODIFY:
  src/Runtime/YO4X.Mql5.Engine/Feed/Mql5CsvMarketFeed.cs

Read that file completely first. You may read any other file for context, but you must not edit any other file, create files, delete files, or run commands.

FINDINGS TO FIX (4):

[1] [P1] CsvMarketFeed fails to enforce monotonic ascending time order and duplicate timestamp rejection
    Where:   [src/Runtime/YO4X.Mql5.Engine/Feed/Mql5CsvMarketFeed.cs:53-62](file:///C:/Users/Dev23/Desktop/yo4x/src/Runtime/YO4X.Mql5.Engine/Feed/Mql5CsvMarketFeed.cs#L53-L62)
    Failure: `ReadBars()` maintains no timestamp state between yielded bars. When given an unsorted CSV file or a file containing duplicate timestamps (e.g. `2024.01.05 00:00` followed by `2024.01.02 00:00`), bars are yielded in file order, violating `IMql5MarketFeed`'s contract ("Enumerates the bars in ascending time order"). This causes `Mql5SimulatedBroker.ApplyBar` to miscalculate swap accrual (`bar.Time.Date > time.Date`), corrupts indicator history series in `Mql5MarketContext`, and breaks backtest determinism.
    Suggested fix: Track `previousTime` in `ReadBars()` and reject or skip bars where `bar.Time <= previousTime` (or sort during feed construction).

[2] [P1] Lack of OHLC invariant and positive price validation allows impossible bar geometries into the engine
    Where:   [src/Runtime/YO4X.Mql5.Engine/Feed/Mql5CsvMarketFeed.cs:98-104](file:///C:/Users/Dev23/Desktop/yo4x/src/Runtime/YO4X.Mql5.Engine/Feed/Mql5CsvMarketFeed.cs#L98-L104)
    Failure: `TryParseBar` verifies only that the 4 price fields parse as `double`, never checking the fundamental OHLC invariants (`High >= Open`, `High >= Close`, `Low <= Open`, `Low <= Close`, `High >= Low`, or `Low > 0`). Input such as `2024.01.01 00:00,1.1000,1.0500,1.1500,1.0800` (where High is lower than Low) is accepted as a valid `Mql5Bar`. When passed to `Mql5SimulatedBroker.ApplyBar`, the intrabar step simulation walks through an inverted path (`Open(1.10) -> Low(1.15) -> High(1.05) -> Close(1.08)`), triggering limit orders and stop-losses at impossible fills.
    Suggested fix: Validate that `open > 0`, `high >= Math.Max(open, close)`, `low <= Math.Min(open, close)`, and `low > 0` before returning `true` in `TryParseBar`.

[3] [P1] Multi-character separator split corrupts comma-decimal European CSV rows into invalid price spikes
    Where:   [src/Runtime/YO4X.Mql5.Engine/Feed/Mql5CsvMarketFeed.cs:13](file:///C:/Users/Dev23/Desktop/yo4x/src/Runtime/YO4X.Mql5.Engine/Feed/Mql5CsvMarketFeed.cs#L13-L13) and [src/Runtime/YO4X.Mql5.Engine/Feed/Mql5CsvMarketFeed.cs:82](file:///C:/Users/Dev23/Desktop/yo4x/src/Runtime/YO4X.Mql5.Engine/Feed/Mql5CsvMarketFeed.cs#L82-L82)
    Failure: When parsing a European MetaTrader CSV export formatted with semicolon delimiters and comma decimal separators (e.g. `2024.01.01 00:00;1,10000;1,10120;1,09980;1,10050;321;12`), `Split(Separators)` splits simultaneously on `,` and `;`. This decomposes price numbers across separate tokens (`fields[1]="1"`, `fields[2]="10000"`, `fields[3]="1"`, `fields[4]="10120"`), resulting in Open=1.0, High=10000.0, Low=1.0, Close=10120.0. `TryParseBar` returns `true`, silently injecting 10,000x price spikes and corrupted spread/volume data into the engine rather than parsing properly or failing.
    Suggested fix: Detect or specify the column delimiter per feed/header rather than splitting on all separators at once, and parse floating-point tokens only after column boundaries are isolated.

[4] [P2] Silent omission of unparseable CSV rows masks incompatible formats and corrupt datasets
    Where:   [src/Runtime/YO4X.Mql5.Engine/Feed/Mql5CsvMarketFeed.cs:55-61](file:///C:/Users/Dev23/Desktop/yo4x/src/Runtime/YO4X.Mql5.Engine/Feed/Mql5CsvMarketFeed.cs#L55-L61)
    Failure: Any unparseable row (unsupported date format such as `yyyy/MM/dd`, non-numeric price token, or corrupted columns) returns `false` from `TryParseBar` and is silently dropped. If a user provides an export with an unrecognized timestamp layout, `ReadBars()` yields 0 bars without logging or error. The backtest runner finishes cleanly with 0 ticks and 0 trades, misleading the user into believing the backtest ran successfully on valid empty market data.
    Suggested fix: Provide a configurable parse failure mode or track and report skipped row counts so that malformed data files produce actionable errors or warnings.

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

