---
agent_id: L02
lane: MT5 Execution, Margin and Swap Specification Compliance
scope:
  - src/Runtime/YO4X.Mql5.Engine/Trading/Mql5SimulatedBroker.cs
status: COMPLETE
generated: 2026-08-29T11:40:00Z
counts: { P0: 0, P1: 5, P2: 2, P3: 0 }
---

# L02 — MT5 Execution, Margin and Swap Specification Compliance

## Scope audited
- `src/Runtime/YO4X.Mql5.Engine/Trading/Mql5SimulatedBroker.cs` (1,153 lines) — reviewed in full.

## Verdict
The simulated broker implements a clean deterministic intrabar bar-walking state machine for basic netting and hedging order flows, but exhibits significant behavioral divergence from documented MetaTrader 5 / MQL5 broker specifications. Critical broker-level execution constraints are missing or incorrectly modeled: stop-out liquidation ordering ignores accumulated swap and commission; swap accrual lacks 3-day rollover on Wednesday while erroneously charging multi-day swaps on Monday over weekend bar gaps; freeze level constraints (`SYMBOL_TRADE_FREEZE_LEVEL`) are completely ignored; request slippage deviation (`req.Deviation`) is unverified; pending orders lack expiration handling and stop-limit types; and intrabar stop triggers grant zero-slippage executions. Strategies tested against this engine will experience substantial backtest distortion when transitioned to live MT5 brokers.

---

### Comprehensive MT5 Broker Specification Comparison & Distortion Analysis

#### 1. Order Filling Modes (`ORDER_FILLING_FOK`, `ORDER_FILLING_IOC`, `ORDER_FILLING_RETURN`, `ORDER_FILLING_BOC`)
- **Documented MT5 Behaviour (MQL5 Reference: `ENUM_ORDER_TYPE_FILLING`, `SYMBOL_FILLING_MODE`):**
  - `ORDER_FILLING_FOK` (Fill or Kill): The order must be executed in full at the specified volume or rejected entirely (`TRADE_RETCODE_REJECT` = 10006).
  - `ORDER_FILLING_IOC` (Immediate or Cancel): The deal executes at maximum available market volume up to the requested volume; any unfilled volume is immediately cancelled (`TRADE_RETCODE_DONE_PARTIAL` = 10010).
  - `ORDER_FILLING_RETURN` / `ORDER_FILLING_BOC`: For market/limit orders under exchange execution, unfilled volume remains in the order book (Book or Cancel cancels if immediately executable). If an EA submits a filling mode unsupported by `SYMBOL_FILLING_MODE`, MT5 rejects with `TRADE_RETCODE_INVALID_FILL` (10030).
- **Simulated Broker Behaviour (`Mql5SimulatedBroker.cs:504-544`):**
  - `Mql5TradeRequest` contains no `type_filling` field. `ExecuteDeal` treats every market deal as having infinite top-of-book liquidity and returns `Mql5TradeRetcode.Done` (10009) for 100% of requested volume.
- **Backtest Distortion:**
  - Scalping, high-frequency, or large-lot strategies assume 100% fill rates at current top-of-book prices. In live MT5 execution, large orders experience frequent rejections or partial fills, breaking multi-leg or grid execution logic.

#### 2. Stops Level (`SYMBOL_TRADE_STOPS_LEVEL`) and Freeze Level (`SYMBOL_TRADE_FREEZE_LEVEL`) Constraints
- **Documented MT5 Behaviour (MQL5 Reference: `SYMBOL_TRADE_STOPS_LEVEL`, `SYMBOL_TRADE_FREEZE_LEVEL`, `TRADE_RETCODE_FROZEN`):**
  - `StopsLevel`: Minimum distance in points between current market price and placed SL/TP/pending price. Violation returns `TRADE_RETCODE_INVALID_STOPS` (10016).
  - `FreezeLevel`: Distance in points within which orders/positions are frozen. When market price is within `FreezeLevel` of a pending order price or an open position's SL/TP, the server forbids modifying (`TRADE_ACTION_MODIFY`, `TRADE_ACTION_SLTP`), deleting (`TRADE_ACTION_REMOVE`), or closing the position, returning `TRADE_RETCODE_FROZEN` (10022).
- **Simulated Broker Behaviour (`Mql5SimulatedBroker.cs:658-793`, `859-901`):**
  - `StopsLevelPoints` is checked during SL/TP placement and pending order creation (`ValidateStops`, `ValidatePendingPrice`).
  - `FreezeLevelPoints` is **completely ignored** across `ModifyPositionStops`, `ModifyPending`, `RemovePending`, and `CloseByTicket`.
- **Backtest Distortion:**
  - Breakout or news-trading EAs attempting to trail stops or cancel pending orders immediately before market touch will succeed in backtesting but fail with `TRADE_RETCODE_FROZEN` in live trading.

#### 3. Margin Calculation per Calculation Mode and Role of Contract Size, Leverage, and Tick Value
- **Documented MT5 Behaviour (MQL5 Reference: `ENUM_SYMBOL_CALC_MODE`, `ACCOUNT_MARGIN_MODE`):**
  - `SYMBOL_CALC_MODE_FOREX`: Initial Margin = `Volume * ContractSize / Leverage * MarginRate * BaseToDepositRate`.
  - `SYMBOL_CALC_MODE_CFD`: Margin = `Volume * ContractSize * MarketPrice * MarginRate * QuoteToDepositRate`.
  - `SYMBOL_CALC_MODE_CFD_INDEX`: Margin = `Volume * ContractSize * MarketPrice * TickPrice / TickSize`.
  - `SYMBOL_CALC_MODE_FUTURES` / `EXCH_FUTURES`: Fixed initial margin (`SYMBOL_MARGIN_INITIAL`) independent of leverage.
  - Hedged positions in hedging accounts utilize `SYMBOL_MARGIN_HEDGED` (often 0 or 50% margin for matched volume).
- **Simulated Broker Behaviour (`Mql5SimulatedBroker.cs:91-104`, `930-945`, `Mql5SymbolSpec.cs:96-104`):**
  - Hardcodes a single formula: `Volume * ContractSize * price * QuoteToDepositRate / Leverage`. In hedging mode, total margin is a simple arithmetic sum of each position's margin with zero hedged margin discounting.
- **Backtest Distortion:**
  - Non-forex instruments (indices, commodities, crypto, futures) and indirect forex pairs (e.g. USDJPY where base is USD) calculate incorrect margin requirements (often off by multiple orders of magnitude), inducing premature margin rejections or unrealistic over-leveraging.

#### 4. Margin Call and Stop Out Levels and Liquidation Ordering
- **Documented MT5 Behaviour (MQL5 Reference: `ACCOUNT_MARGIN_SO_CALL`, `ACCOUNT_MARGIN_SO_SO`, `ENUM_ACCOUNT_STOPOUT_MODE`):**
  - Margin Call: Warns when `MarginLevel <= MarginCallLevel`; blocks margin-increasing operations.
  - Stop Out: When `MarginLevel <= StopOutLevel`, broker forcibly liquidates positions. Under `ACCOUNT_STOPOUT_MODE_PERCENT`, in hedging mode the server liquidates the position with the largest total floating loss (`POSITION_PROFIT + POSITION_SWAP + POSITION_COMMISSION`) first.
- **Simulated Broker Behaviour (`Mql5SimulatedBroker.cs:457-502`):**
  - Margin call warning is unimplemented.
  - Stop out liquidation sorts candidate positions strictly by `candidate.Profit` (gross price profit), completely ignoring accrued `Swap` and `Commission`.
- **Backtest Distortion:**
  - Positions with massive accumulated negative swap are kept open while positions with smaller gross losses are liquidated first, producing unfaithful equity trajectories in carry-trade drawdowns.

#### 5. Swap / Rollover Charging Including Triple-Swap Day
- **Documented MT5 Behaviour (MQL5 Reference: `SYMBOL_SWAP_ROLLOVER3DAYS`, `SYMBOL_SWAP_MODE`):**
  - Swaps accrue at daily server rollover (typically 23:59:00). On the triple-swap day (`SYMBOL_SWAP_ROLLOVER3DAYS`, Wednesday for T+2 Forex), 3x swap is charged to cover Saturday and Sunday settlements. No swaps accrue over the weekend market close.
- **Simulated Broker Behaviour (`Mql5SimulatedBroker.cs:153-157`, `293-308`):**
  - Computes `(bar.Time.Date - time.Date).Days`. Wednesday rollover charges only 1 day of swap. When bars jump from Friday close to Monday open across the weekend gap, `(Monday - Friday).Days = 3`, charging 3 days of swap on Monday morning.
- **Backtest Distortion:**
  - Mid-week swing trades holding Wednesday->Thursday are undercharged by 66%, while weekend-held trades incur erratic swap jumps on Monday instead of mid-week settlement.

#### 6. Slippage and Requote Behaviour
- **Documented MT5 Behaviour (MQL5 Reference: `MqlTradeRequest.deviation`, `TRADE_RETCODE_REQUOTE`):**
  - `deviation` sets maximum allowable price deviation in points. In Instant execution, if market moves beyond `deviation`, broker issues `TRADE_RETCODE_REQUOTE` (10004). In Market execution, fill is executed at prevailing market quote with stochastic positive or negative slippage.
- **Simulated Broker Behaviour (`Mql5SimulatedBroker.cs:504-544`, `808-814`):**
  - `req.Deviation` is ignored. Adverse slippage (`options.SlippagePoints`) is statically and deterministically added to every market deal.
- **Backtest Distortion:**
  - EAs expecting requote rejections or price improvement in calm markets experience static penalty degradation regardless of `deviation`.

#### 7. Pending Order Activation, Intrabar Execution, and Expiration
- **Documented MT5 Behaviour (MQL5 Reference: `ENUM_ORDER_TYPE`, `ENUM_ORDER_TYPE_TIME`, `ORDER_TYPE_BUY_STOP_LIMIT`):**
  - Stop orders triggered across a market gap must execute at the gap opening price (slippage), not the resting order price.
  - Pending orders support expirations (`ORDER_TIME_GTC`, `ORDER_TIME_DAY`, `ORDER_TIME_SPECIFIED`).
  - Stop-Limit orders (`ORDER_TYPE_BUY_STOP_LIMIT`, `ORDER_TYPE_SELL_STOP_LIMIT`) activate a resting limit order when the stop price is hit.
- **Simulated Broker Behaviour (`Mql5SimulatedBroker.cs:367-455`, `583-655`):**
  - Stop-Limit orders are rejected with `Mql5TradeRetcode.Invalid`. Expiration dates are not supported (orders are perpetual GTC).
  - Intrabar price moves (`gapAllowed = false`) fill pending stop orders exactly at `order.Price` regardless of intrabar volatility.
- **Backtest Distortion:**
  - Breakout strategies benefit from zero-slippage fills during violent intrabar thrusts, while day-expiration pending orders linger indefinitely.

---

## Findings

### [P1] Stop-out liquidation ignores accrued swap and commission when selecting worst position
- **Where:** `src/Runtime/YO4X.Mql5.Engine/Trading/Mql5SimulatedBroker.cs:468-475`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
              Mql5Position worst = positions[0];
              foreach (Mql5Position candidate in positions)
              {
                  if (candidate.Profit < worst.Profit)
                  {
                      worst = candidate;
                  }
              }
  ```
- **Failure:** In Hedging mode, MT5 stop-out rules mandate liquidating the open position with the largest total floating loss, which equals `POSITION_PROFIT + POSITION_SWAP + POSITION_COMMISSION`. `Mql5SimulatedBroker.EnforceStopOut` compares only `candidate.Profit` (gross price profit). If Position A has `Profit = -$200`, `Swap = $0` (Total loss -$200) and Position B has `Profit = -$150`, `Swap = -$300` (Total loss -$450), Position A is liquidated first because `-200 < -150`, leaving the true worst-performing position open and distorting account liquidation dynamics.
- **Fix:** Update the worst-position selection loop to compare net floating profit: `double candidateNet = candidate.Profit + candidate.Commission + candidate.Swap;` and compare `candidateNet < worstNet`.

### [P1] Swap accrual lacks triple-swap rollover and overcharges weekend bar gaps
- **Where:** `src/Runtime/YO4X.Mql5.Engine/Trading/Mql5SimulatedBroker.cs:153-157`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
          if (time != DateTime.UnixEpoch && bar.Time.Date > time.Date)
          {
              int days = (bar.Time.Date - time.Date).Days;
              AccrueSwap(days);
          }
  ```
- **Failure:** MT5 forex symbols apply a 3-day swap rollover on Wednesday night (`SYMBOL_SWAP_ROLLOVER3DAYS`) for weekend settlement, charging 0 swap on weekend calendar gaps. `ApplyBar` calculates raw calendar difference `(bar.Time.Date - time.Date).Days`. On Wednesday night (Wednesday to Thursday), it charges 1 day of swap instead of 3. When a feed advances from Friday close (e.g. 2026-05-01) to Monday open (e.g. 2026-05-04), `(Monday - Friday).Days = 3`, charging 3 days of swap on Monday morning.
- **Fix:** Iterate day-by-day across the elapsed date range, applying 3x swap multiplier on the configured rollover day (Wednesday for Forex) and 0 swap for Saturday and Sunday rollovers.

### [P1] Freeze level (`SYMBOL_TRADE_FREEZE_LEVEL`) constraints are completely ignored
- **Where:** `src/Runtime/YO4X.Mql5.Engine/Trading/Mql5SimulatedBroker.cs:658-676`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
          double reference = position.Type == Mql5PositionType.Buy ? Bid : Ask;
          if (!ValidateStops(position.Type, reference, sl, tp, out string stopsError))
          {
              return Fail(result, Mql5TradeRetcode.InvalidStops, stopsError, req);
          }
  ```
- **Failure:** In real MT5, when `SYMBOL_TRADE_FREEZE_LEVEL > 0`, the server rejects modification or deletion of pending orders or position SL/TP when market price is within freeze distance with `TRADE_RETCODE_FROZEN` (10022). `Mql5SimulatedBroker` contains no checks against `spec.FreezeLevelPoints` in `ModifyPositionStops`, `ModifyPending`, or `RemovePending`. An EA can cancel or modify a pending order or stop loss 0.1 point before market touch, succeeding in backtest when live MT5 would freeze the order.
- **Fix:** Add freeze level validation helper checking `Math.Abs(marketPrice - orderPrice) >= spec.FreezeLevelPoints * spec.Point` and return `Mql5TradeRetcode.Frozen` (10022) if violated.

### [P1] Slippage `Deviation` parameter in trade request is unchecked
- **Where:** `src/Runtime/YO4X.Mql5.Engine/Trading/Mql5SimulatedBroker.cs:522-524`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
          Mql5PositionType side = req.Type == Mql5OrderType.Buy ? Mql5PositionType.Buy : Mql5PositionType.Sell;
          double fill = MarketFillPrice(side);
          double reference = side == Mql5PositionType.Buy ? Bid : Ask;
  ```
- **Failure:** `Mql5TradeRequest.Deviation` specifies the maximum allowable slippage in points. In `ExecuteDeal`, `MarketFillPrice` unconditionally applies `options.SlippagePoints * spec.Point` without inspecting `req.Deviation`. If an EA specifies `req.Deviation = 2` and `options.SlippagePoints = 10`, real MT5 Instant Execution would return `TRADE_RETCODE_REQUOTE` (10004) or reject the deal, but `ExecuteDeal` executes at 10 points slippage with `Mql5TradeRetcode.Done`.
- **Fix:** Validate if `req.Deviation > 0 && options.SlippagePoints > req.Deviation`, and if so, return `Fail(result, Mql5TradeRetcode.Requote, "price deviation exceeded", req)` or reject based on execution mode.

### [P1] Intrabar stop activations ignore price jumps and grant zero-slippage perfect fills
- **Where:** `src/Runtime/YO4X.Mql5.Engine/Trading/Mql5SimulatedBroker.cs:382-385`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
                  case Mql5OrderType.BuyStop:
                      touched = ask >= order.Price;
                      fill = gapAllowed ? Math.Max(order.Price, ask) : order.Price;
                      break;
  ```
- **Failure:** In `ApplyBar` (lines 166-168), `MoveTo` is called with `gapAllowed: false` for intrabar segments (`second`, `third`, `bar.Close`). When a `BuyStop` order is triggered during a rapid intrabar thrust (e.g. price jumps from 1.1000 past stop at 1.1020 directly to bar High 1.1080), `gapAllowed == false` causes `fill = order.Price` (1.1020). In real MT5, stop orders convert to market orders upon activation and incur adverse slippage across price jumps.
- **Fix:** Allow realistic slippage modeling or tick-level price traversal for intrabar stop activations rather than unconditionally clamping `fill` to `order.Price`.

### [P2] Pending orders lack expiration handling (`ORDER_TIME_DAY` / `ORDER_TIME_SPECIFIED`)
- **Where:** `src/Runtime/YO4X.Mql5.Engine/Trading/Mql5SimulatedBroker.cs:368-375`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
      private void ProcessPendingActivations(bool gapAllowed)
      {
          foreach (Mql5PendingOrder order in pendingOrders.ToArray())
          {
              double ask = Ask;
              double quote = Bid;
              bool touched;
              double fill;
  ```
- **Failure:** MT5 supports pending order expiration modes (`ORDER_TIME_DAY`, `ORDER_TIME_SPECIFIED`, `ORDER_TIME_SPECIFIED_DAY`). In `Mql5SimulatedBroker`, pending orders have no expiration timestamp and `ProcessPendingActivations` never expires orders. A pending order intended for single-day session trading remains active indefinitely across days or weeks until price touches it.
- **Fix:** Add expiration timestamp support to `Mql5PendingOrder` and evict expired orders in `ProcessPendingActivations` before evaluating price triggers.

### [P2] Deal execution silently ignores order filling modes and assumes infinite full fills
- **Where:** `src/Runtime/YO4X.Mql5.Engine/Trading/Mql5SimulatedBroker.cs:535-543`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
          long ticket = OpenExposure(side, volume, fill, req.Sl, req.Tp, req.Magic, req.Comment, result);
          result.Retcode = Mql5TradeRetcode.Done;
          result.Volume = volume;
          result.Price = fill;
          result.Position = ticket;
          result.Order = ticket;
          result.Deal = ticket;
          result.Comment = "done";
          return true;
  ```
- **Failure:** Real MT5 execution enforces `ORDER_FILLING_FOK`, `ORDER_FILLING_IOC`, and `ORDER_FILLING_RETURN`. If an unsupported filling mode is requested, MT5 returns `TRADE_RETCODE_INVALID_FILL` (10030). `ExecuteDeal` has no filling mode validation and always fills 100% of requested volume at top of book, masking liquidity constraints and partial fill requirements.
- **Fix:** Support filling mode flags in trade requests, validate against symbol filling capabilities, and simulate partial fill / cancellation semantics when enabled.

---

## Referrals
- `src/Runtime/YO4X.Mql5.Engine/Trading/Mql5SymbolSpec.cs:96-104` — `MarginOf` hardcodes a single Forex calculation mode (`volume * ContractSize * price * QuoteToDepositRate / leverage`) that breaks on CFDs, indices, futures, and indirect FX pairs.
- `src/Runtime/YO4X.Mql5.Engine/Trading/Mql5TradeRequest.cs:1-66` — `Mql5TradeRequest` lacks MT5 standard fields `type_filling`, `type_time`, and `expiration`.
- `src/Runtime/YO4X.Mql5.Engine/Hosting/Mql5RunOptions.cs:41-45` — `Mql5RunOptions` lacks `MarginCallLevelPercent` (`ACCOUNT_MARGIN_SO_CALL`) and `ACCOUNT_STOPOUT_MODE_MONEY`.

---

## Coverage gaps
- `src/Runtime/YO4X.Mql5.Engine/Trading/Mql5SimulatedBroker.cs:934-942` — `HasMarginFor` netting margin offset branch: untested under multi-deal partial netting flips where requested volume partially offsets existing opposite volume and opens a remainder.
- `src/Runtime/YO4X.Mql5.Engine/Trading/Mql5SimulatedBroker.cs:465-501` — `EnforceStopOut` multi-position loop: untested when multiple hedged positions have identical floating loss or when liquidating one position restores margin level above stop out while leaving other positions open.
- `src/Runtime/YO4X.Mql5.Engine/Trading/Mql5SimulatedBroker.cs:995-1008` — `OpenExposure` netting opposite deal remainder branch: untested when incoming opposing deal volume exactly equals existing position volume versus when remainder is below `VolumeEpsilon`.


--- agy: gemini-3.7-flash-high | effort=high | mode=plan (read-only) | 99.6s | 159361 tok | id=b2321d7b-de51-4ae9-94e6-b29443afc45e
