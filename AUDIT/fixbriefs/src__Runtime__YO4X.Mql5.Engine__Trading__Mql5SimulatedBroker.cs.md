You are a fix agent on YO4X, a LIVE MetaTrader 5 / MQL5 algorithmic trading platform (.NET 10 backend, React frontend, an MQL5-to-C# transpiler, a deterministic backtest engine). An audit found defects in ONE file. Fix them.

THE ONLY FILE YOU MAY MODIFY:
  src/Runtime/YO4X.Mql5.Engine/Trading/Mql5SimulatedBroker.cs

Read that file completely first. You may read any other file for context, but you must not edit any other file, create files, delete files, or run commands.

FINDINGS TO FIX (11):

[1] [P0] Intra-bar margin stop out is deferred until bar close
    Where:   src/Runtime/YO4X.Mql5.Engine/Trading/Mql5SimulatedBroker.cs:159-165
    Failure: An account holds a leveraged Buy position. A high-volatility bar drops to a deep intrabar low where floating losses exceed account equity, driving `MarginLevel` to -50% (total liquidation). Because `EnforceStopOut()` is only invoked at line 165 after `bar.Close`, if the market bounces back before bar close and restores `MarginLevel` above `StopOutLevelPercent`, no stop out occurs. An account that went bankrupt intra-bar continues trading as if liquidation never happened.
    Suggested fix: Call `EnforceStopOut()` inside `MoveTo()` immediately after `Revalue()` at each intrabar price step (`Open`, `second`, `third`, `Close`).

[2] [P0] Intra-bar pending order activation at swing extremes evades same-bar StopLoss
    Where:   src/Runtime/YO4X.Mql5.Engine/Trading/Mql5SimulatedBroker.cs:282-283
    Failure: A strategy places a `BuyLimit` at 1.09700 with `SL = 1.09600` and `TP = 1.10500`. A bullish bar arrives: Open 1.10000, Low 1.09500, High 1.10600, Close 1.10200. At `MoveTo(bar.Low = 1.09500)`, `ProcessPositionStops` runs first (no positions), then `ProcessPendingActivations` runs second and opens the Buy position at 1.09700 with SL 1.09600 while market Bid is 1.09500 (below SL). Because stops are never re-evaluated at 1.09500 for the newly opened position, the next move `MoveTo(bar.High = 1.10600)` triggers `TakeProfit` at 1.10500. A trade that breached its stop loss and should have lost money is awarded a full winning trade.
    Suggested fix: Evaluate position stops on newly activated positions against the current quote inside `ProcessPendingActivations` (or immediately after `OpenExposure`) before progressing to subsequent intrabar ticks.

[3] [P0] Pending order activations bypass free margin validation entirely
    Where:   src/Runtime/YO4X.Mql5.Engine/Trading/Mql5SimulatedBroker.cs:399-408
    Failure: An account with $100.00 balance and $0.00 free margin has a resting pending order for 100.0 lots ($110,500 margin requirement). When price touches the pending order, `ProcessPendingActivations` calls `OpenExposure` without checking `HasMarginFor()`. The order activates unconditionally, creating massive open positions with negative free margin and infinite effective leverage, bypassing all risk and margin limits.
    Suggested fix: In `ProcessPendingActivations()`, check `HasMarginFor(side, order.Volume, fill)` before calling `OpenExposure()`. If false, record an order rejection event with `Mql5TradeRetcode.NoMoney` and delete the pending order without opening a position.

[4] [P1] Exit side commission is never charged on position close
    Where:   src/Runtime/YO4X.Mql5.Engine/Trading/Mql5SimulatedBroker.cs:998-1002
    Failure: With `CommissionPerLot = 7.0` ($7.00 per lot per side), a strategy opens and closes 1.00 lot EURUSD. On open, `position.Commission` is set to -$7.00. On close, `ClosePortion` calculates `commission = -7.00 * 1.0 = -7.00` and deducts it from balance. The exit-turn commission is never calculated or deducted, charging only $7.00 total instead of the required $14.00 round-turn commission.
    Suggested fix: In `ClosePortion()`, charge the exit commission `-Round2(options.CommissionPerLot * closeVolume)` in addition to releasing accrued entry commission.

[5] [P1] Netting position reduction and addition overwrites or deletes surviving position stops
    Where:   src/Runtime/YO4X.Mql5.Engine/Trading/Mql5SimulatedBroker.cs:901-902
    Failure: On a netting account, a strategy opens Buy 0.30 lots EURUSD with `SL = 1.09500` and `TP = 1.10800`. Later, it partially scales out by sending a Sell deal for 0.10 lots with `SL = 0, TP = 0`. Lines 925-926 execute on the surviving 0.20-lot Buy position and set `survivor.StopLoss = 0.0; survivor.TakeProfit = 0.0;`, silently removing all stop loss protection from the remaining open position. Similarly, adding volume with `SL = 0` overwrites existing position stops at lines 901-902.
    Suggested fix: In netting mode, only update `existing.StopLoss` / `TakeProfit` if `normalizedSl > 0` or `normalizedTp > 0`, and never overwrite `survivor` stops during opposite-direction position reduction.

[6] [P1] Rollover swap is never accrued on open positions across multi-day bars
    Where:   src/Runtime/YO4X.Mql5.Engine/Trading/Mql5SimulatedBroker.cs:950
    Failure: A strategy opens a 1.00 lot short position on a symbol with `SwapShort = -15.00` and holds it for 30 consecutive daily bars. Because `ApplyBar()` does not track calendar day rollovers or accrue swap onto `position.Swap`, the trade closes with $0.00 swap instead of -$450.00 swap deductions, falsifying swing strategy returns.
    Suggested fix: In `ApplyBar()`, check for date transitions across midnight server time, calculate daily swap (`SwapLong` / `SwapShort` multiplied by position volume), and accumulate it onto `position.Swap`.

[7] [P1] Stop loss validation in `ExecuteDeal` evaluates against entry price instead of closing quote
    Where:   src/Runtime/YO4X.Mql5.Engine/Trading/Mql5SimulatedBroker.cs:471-476
    Failure: On EURUSD with Bid = 1.10000, Ask = 1.10020 (spread 20 points) and StopsLevel = 0, a strategy submits a `BUY` order with `SL = 1.10010` (inside the spread). `ExecuteDeal` passes `fill = Ask = 1.10020` to `ValidateStops`, which calculates `1.10020 - 1.10010 = 10 points > 0` and approves the order. The position opens, but because Buy SL is evaluated against Bid (1.10000), `ProcessPositionStops` on the next tick sees `Bid (1.10000) <= SL (1.10010)` and immediately stops out the trade at a loss instead of rejecting the invalid order at submission.
    Suggested fix: In `ExecuteDeal`, pass `reference = side == Mql5PositionType.Buy ? Bid : Ask` to `ValidateStops`, matching the reference pricing used in `ModifyPositionStops`.

[8] [P2] `ModifyPending` skips stop loss/take profit validation and rejects valid price-neutral stop edits
    Where:   src/Runtime/YO4X.Mql5.Engine/Trading/Mql5SimulatedBroker.cs:663-671
    Failure: 1) `ModifyPending` assigns `order.StopLoss` and `order.TakeProfit` without calling `ValidateStops()`, allowing inverted or invalid stops to be attached to pending orders. 2) If an EA sends `req.Price = 0` to update only the SL/TP of an existing `BuyStop` order while market Ask has approached within `StopsLevelPoints` of `order.Price`, `ValidatePendingPrice` evaluates the existing price as if it were a new placement and rejects the modification with `Mql5TradeRetcode.InvalidPrice`.
    Suggested fix: Only validate pending price if `req.Price > 0` and changed from `order.Price`. Add `ValidateStops(side, order.Price, req.Sl, req.Tp)` validation before updating pending order stops.

[9] [P2] `ValidatePendingPrice` rejects valid limit orders at market price when `StopsLevelPoints == 0`
    Where:   src/Runtime/YO4X.Mql5.Engine/Trading/Mql5SimulatedBroker.cs:841
    Failure: On ECN accounts where `StopsLevelPoints = 0` (`minimum = 0.0`), `tolerance` is `0.5 * Point`. Placing a `BuyLimit` order at the current Ask price evaluates `ask - price = 0.0`. While `ask - price >= minimum` is satisfied, `ask - price > tolerance` evaluates to `false`, erroneously rejecting the order with `"BuyLimit at ... is on the wrong side of the market"`.
    Suggested fix: Remove the strict `> tolerance` requirement when `minimum == 0.0`, testing `ask - price >= minimum - tolerance`.

[10] [P3] StopOut journal event logs pre-liquidation balance
    Where:   src/Runtime/YO4X.Mql5.Engine/Trading/Mql5SimulatedBroker.cs:440-448
    Failure: When stop out occurs with balance $10,000 and floating loss -$9,500, `Record()` is called before `ClosePortion()`. The `StopOut` event in the audit journal records `Balance = 10000.0` instead of the post-liquidation realized balance of $500.0, corrupting event journal consistency.
    Suggested fix: Record the `StopOut` order event after `ClosePortion()` has executed and updated `balance`.

[11] [P3] `CloseAll` closes positions at raw Bid/Ask without applying slippage
    Where:   src/Runtime/YO4X.Mql5.Engine/Trading/Mql5SimulatedBroker.cs:237-238
    Failure: `options.SlippagePoints` is applied to all market entries and exits in `ExecuteDeal` and `CloseByTicket`. However, `CloseAll()` (invoked at end of backtest) closes positions at raw `Bid`/`Ask` without slippage, giving end-of-run closes an unearned price bonus.
    Suggested fix: Use `MarketFillPrice(position.Type == Mql5PositionType.Buy ? Mql5PositionType.Sell : Mql5PositionType.Buy)` in `CloseAll()`.

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

