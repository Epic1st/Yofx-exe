---
agent_id: F20
lane: engine-broker-sim
scope:
  - src/Runtime/YO4X.Mql5.Engine/Trading/Mql5SimulatedBroker.cs
status: COMPLETE
generated: 2026-08-29T08:22:00Z
counts: { P0: 3, P1: 4, P2: 1, P3: 2 }
---

# F20 — engine-broker-sim

## Scope audited
- `src/Runtime/YO4X.Mql5.Engine/Trading/Mql5SimulatedBroker.cs` (1078 lines)

## Verdict
Broken on critical simulation paths. While single-order basic deal execution is clean, severe flaws exist in pending order margin bypass, intra-bar pending activation stop-loss sequencing, intra-bar stop-out evasion, netting stop-loss erasure, and exit commission omission. These bugs cause backtests to systematically turn losing trades into winning trades, permit infinite leverage, and undercharge trading costs.

## Findings

### [P0] Pending order activations bypass free margin validation entirely
- **Where:** `src/Runtime/YO4X.Mql5.Engine/Trading/Mql5SimulatedBroker.cs:399-408`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
  OpenExposure(
      side,
      order.Volume,
      spec.NormalizePrice(fill),
      order.StopLoss,
      order.TakeProfit,
      order.Magic,
      order.Comment,
      result: null);
  ```
- **Failure:** An account with $100.00 balance and $0.00 free margin has a resting pending order for 100.0 lots ($110,500 margin requirement). When price touches the pending order, `ProcessPendingActivations` calls `OpenExposure` without checking `HasMarginFor()`. The order activates unconditionally, creating massive open positions with negative free margin and infinite effective leverage, bypassing all risk and margin limits.
- **Fix:** In `ProcessPendingActivations()`, check `HasMarginFor(side, order.Volume, fill)` before calling `OpenExposure()`. If false, record an order rejection event with `Mql5TradeRetcode.NoMoney` and delete the pending order without opening a position.

### [P0] Intra-bar pending order activation at swing extremes evades same-bar StopLoss
- **Where:** `src/Runtime/YO4X.Mql5.Engine/Trading/Mql5SimulatedBroker.cs:282-283`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
  ProcessPositionStops(gapAllowed);
  ProcessPendingActivations(gapAllowed);
  ```
- **Failure:** A strategy places a `BuyLimit` at 1.09700 with `SL = 1.09600` and `TP = 1.10500`. A bullish bar arrives: Open 1.10000, Low 1.09500, High 1.10600, Close 1.10200. At `MoveTo(bar.Low = 1.09500)`, `ProcessPositionStops` runs first (no positions), then `ProcessPendingActivations` runs second and opens the Buy position at 1.09700 with SL 1.09600 while market Bid is 1.09500 (below SL). Because stops are never re-evaluated at 1.09500 for the newly opened position, the next move `MoveTo(bar.High = 1.10600)` triggers `TakeProfit` at 1.10500. A trade that breached its stop loss and should have lost money is awarded a full winning trade.
- **Fix:** Evaluate position stops on newly activated positions against the current quote inside `ProcessPendingActivations` (or immediately after `OpenExposure`) before progressing to subsequent intrabar ticks.

### [P0] Intra-bar margin stop out is deferred until bar close
- **Where:** `src/Runtime/YO4X.Mql5.Engine/Trading/Mql5SimulatedBroker.cs:159-165`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
  MoveTo(bar.Open, gapAllowed: true);
  MoveTo(second, gapAllowed: false);
  MoveTo(third, gapAllowed: false);
  MoveTo(bar.Close, gapAllowed: false);

  Revalue();
  EnforceStopOut();
  ```
- **Failure:** An account holds a leveraged Buy position. A high-volatility bar drops to a deep intrabar low where floating losses exceed account equity, driving `MarginLevel` to -50% (total liquidation). Because `EnforceStopOut()` is only invoked at line 165 after `bar.Close`, if the market bounces back before bar close and restores `MarginLevel` above `StopOutLevelPercent`, no stop out occurs. An account that went bankrupt intra-bar continues trading as if liquidation never happened.
- **Fix:** Call `EnforceStopOut()` inside `MoveTo()` immediately after `Revalue()` at each intrabar price step (`Open`, `second`, `third`, `Close`).

### [P1] Stop loss validation in `ExecuteDeal` evaluates against entry price instead of closing quote
- **Where:** `src/Runtime/YO4X.Mql5.Engine/Trading/Mql5SimulatedBroker.cs:471-476`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
  double fill = MarketFillPrice(side);

  if (!ValidateStops(side, fill, req.Sl, req.Tp, out string stopsError))
  {
      return Fail(result, Mql5TradeRetcode.InvalidStops, stopsError, req);
  }
  ```
- **Failure:** On EURUSD with Bid = 1.10000, Ask = 1.10020 (spread 20 points) and StopsLevel = 0, a strategy submits a `BUY` order with `SL = 1.10010` (inside the spread). `ExecuteDeal` passes `fill = Ask = 1.10020` to `ValidateStops`, which calculates `1.10020 - 1.10010 = 10 points > 0` and approves the order. The position opens, but because Buy SL is evaluated against Bid (1.10000), `ProcessPositionStops` on the next tick sees `Bid (1.10000) <= SL (1.10010)` and immediately stops out the trade at a loss instead of rejecting the invalid order at submission.
- **Fix:** In `ExecuteDeal`, pass `reference = side == Mql5PositionType.Buy ? Bid : Ask` to `ValidateStops`, matching the reference pricing used in `ModifyPositionStops`.

### [P1] Netting position reduction and addition overwrites or deletes surviving position stops
- **Where:** `src/Runtime/YO4X.Mql5.Engine/Trading/Mql5SimulatedBroker.cs:901-902`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
  existing.StopLoss = normalizedSl;
  existing.TakeProfit = normalizedTp;
  ```
- **Failure:** On a netting account, a strategy opens Buy 0.30 lots EURUSD with `SL = 1.09500` and `TP = 1.10800`. Later, it partially scales out by sending a Sell deal for 0.10 lots with `SL = 0, TP = 0`. Lines 925-926 execute on the surviving 0.20-lot Buy position and set `survivor.StopLoss = 0.0; survivor.TakeProfit = 0.0;`, silently removing all stop loss protection from the remaining open position. Similarly, adding volume with `SL = 0` overwrites existing position stops at lines 901-902.
- **Fix:** In netting mode, only update `existing.StopLoss` / `TakeProfit` if `normalizedSl > 0` or `normalizedTp > 0`, and never overwrite `survivor` stops during opposite-direction position reduction.

### [P1] Exit side commission is never charged on position close
- **Where:** `src/Runtime/YO4X.Mql5.Engine/Trading/Mql5SimulatedBroker.cs:998-1002`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
  double gross = Round2(spec.ProfitOf(delta, closeVolume));
  double commission = Round2(position.Commission * fraction);
  double swap = Round2(position.Swap * fraction);

  balance = Round2(balance + gross + commission + swap);
  ```
- **Failure:** With `CommissionPerLot = 7.0` ($7.00 per lot per side), a strategy opens and closes 1.00 lot EURUSD. On open, `position.Commission` is set to -$7.00. On close, `ClosePortion` calculates `commission = -7.00 * 1.0 = -7.00` and deducts it from balance. The exit-turn commission is never calculated or deducted, charging only $7.00 total instead of the required $14.00 round-turn commission.
- **Fix:** In `ClosePortion()`, charge the exit commission `-Round2(options.CommissionPerLot * closeVolume)` in addition to releasing accrued entry commission.

### [P1] Rollover swap is never accrued on open positions across multi-day bars
- **Where:** `src/Runtime/YO4X.Mql5.Engine/Trading/Mql5SimulatedBroker.cs:950`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
  Swap = 0.0,
  ```
- **Failure:** A strategy opens a 1.00 lot short position on a symbol with `SwapShort = -15.00` and holds it for 30 consecutive daily bars. Because `ApplyBar()` does not track calendar day rollovers or accrue swap onto `position.Swap`, the trade closes with $0.00 swap instead of -$450.00 swap deductions, falsifying swing strategy returns.
- **Fix:** In `ApplyBar()`, check for date transitions across midnight server time, calculate daily swap (`SwapLong` / `SwapShort` multiplied by position volume), and accumulate it onto `position.Swap`.

### [P2] `ModifyPending` skips stop loss/take profit validation and rejects valid price-neutral stop edits
- **Where:** `src/Runtime/YO4X.Mql5.Engine/Trading/Mql5SimulatedBroker.cs:663-671`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
  double price = req.Price > 0.0 ? spec.NormalizePrice(req.Price) : order.Price;
  if (!ValidatePendingPrice(order.Type, price, out string priceError))
  {
      return Fail(result, Mql5TradeRetcode.InvalidPrice, priceError, req);
  }

  order.Price = price;
  order.StopLoss = req.Sl > 0.0 ? spec.NormalizePrice(req.Sl) : 0.0;
  order.TakeProfit = req.Tp > 0.0 ? spec.NormalizePrice(req.Tp) : 0.0;
  ```
- **Failure:** 1) `ModifyPending` assigns `order.StopLoss` and `order.TakeProfit` without calling `ValidateStops()`, allowing inverted or invalid stops to be attached to pending orders. 2) If an EA sends `req.Price = 0` to update only the SL/TP of an existing `BuyStop` order while market Ask has approached within `StopsLevelPoints` of `order.Price`, `ValidatePendingPrice` evaluates the existing price as if it were a new placement and rejects the modification with `Mql5TradeRetcode.InvalidPrice`.
- **Fix:** Only validate pending price if `req.Price > 0` and changed from `order.Price`. Add `ValidateStops(side, order.Price, req.Sl, req.Tp)` validation before updating pending order stops.

### [P3] StopOut journal event logs pre-liquidation balance
- **Where:** `src/Runtime/YO4X.Mql5.Engine/Trading/Mql5SimulatedBroker.cs:440-448`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
  Balance = balance,
  Retcode = Mql5TradeRetcode.Done,
  Detail = string.Create(
      CultureInfo.InvariantCulture,
      $"margin level {MarginLevel} fell below stop out {options.StopOutLevelPercent}"),
  });

  double price = worst.Type == Mql5PositionType.Buy ? Bid : Ask;
  ClosePortion(worst, worst.Volume, price, Mql5CloseReason.StopOut);
  ```
- **Failure:** When stop out occurs with balance $10,000 and floating loss -$9,500, `Record()` is called before `ClosePortion()`. The `StopOut` event in the audit journal records `Balance = 10000.0` instead of the post-liquidation realized balance of $500.0, corrupting event journal consistency.
- **Fix:** Record the `StopOut` order event after `ClosePortion()` has executed and updated `balance`.

### [P3] `CloseAll` closes positions at raw Bid/Ask without applying slippage
- **Where:** `src/Runtime/YO4X.Mql5.Engine/Trading/Mql5SimulatedBroker.cs:237-238`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
  double price = position.Type == Mql5PositionType.Buy ? Bid : Ask;
  ClosePortion(position, position.Volume, price, reason);
  ```
- **Failure:** `options.SlippagePoints` is applied to all market entries and exits in `ExecuteDeal` and `CloseByTicket`. However, `CloseAll()` (invoked at end of backtest) closes positions at raw `Bid`/`Ask` without slippage, giving end-of-run closes an unearned price bonus.
- **Fix:** Use `MarketFillPrice(position.Type == Mql5PositionType.Buy ? Mql5PositionType.Sell : Mql5PositionType.Buy)` in `CloseAll()`.

## Referrals
- `src/Runtime/YO4X.Mql5.Engine/Trading/Mql5SymbolSpec.cs` — `QuoteToDepositRate` is constant and does not update with quote price movements for cross currency pairs.
- `src/Runtime/YO4X.Mql5.Engine/Hosting/Mql5StrategyHost.cs` — `broker.ApplyBar` is executed before `strategy.OnTick`, preventing strategies from executing on the bar's open quote prior to bar completion.

## Coverage gaps
- `Mql5SimulatedBroker.cs:419-450` — Stop-out tie-breaking behavior when multiple positions have identical floating profit is untested.
- `Mql5SimulatedBroker.cs:314,330,357-369` — Price gap fills when `gapAllowed == true` across bar open boundary lack edge-case assertion tests.
- `Mql5SimulatedBroker.cs:919-935` — Netting deal execution where incoming volume exactly matches existing volume is untested against reverse-direction residual volume.


--- agy: gemini-3.7-flash-high | effort=high | mode=plan (read-only) | 133.3s | 217086 tok | id=4b1bcef4-e4b8-4ace-9195-19c9cb1cadba
