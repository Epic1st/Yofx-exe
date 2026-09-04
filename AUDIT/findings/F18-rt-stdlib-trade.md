I have launched the process check and will wait for it to complete.
I will wait for the task to complete.
I will wait for the task list output.
---
agent_id: F18
lane: rt-stdlib-trade
scope:
  - src/Runtime/YO4X.Mql5.Runtime/StandardLibrary/Mql5Trade.cs
  - src/Runtime/YO4X.Mql5.Runtime/StandardLibrary/Mql5TradeConstants.cs
  - src/Runtime/YO4X.Mql5.Runtime/StandardLibrary/Mql5TradeTransaction.cs
status: COMPLETE
generated: 2026-08-29T08:27:00Z
counts: { P0: 1, P1: 3, P2: 2, P3: 0 }
---

# F18 — rt-stdlib-trade

## Scope audited
- `src/Runtime/YO4X.Mql5.Runtime/StandardLibrary/Mql5Trade.cs` (514 lines)
- `src/Runtime/YO4X.Mql5.Runtime/StandardLibrary/Mql5TradeConstants.cs` (514 lines)
- `src/Runtime/YO4X.Mql5.Runtime/StandardLibrary/Mql5TradeTransaction.cs` (63 lines)

## Verdict
The standard library `CTrade` surface and constants catalogue are cleanly structured and mirror the MQL5 property IDs faithfully. However, critical flaws exist in order execution and position lifecycle handling: `PositionClosePartial` does not clamp volume to position volume (causing unintended position reversal in netting accounts), `Mql5Trade` omits `SetMarginMode()` in its constructor (breaking `IsHedging()` branching), default filling mode `OrderFillingFok` causes outright rejection on Market Execution brokers, and symbol-level position methods fail to handle multi-position hedging semantics.

## Findings

### [P0] PositionClosePartial does not validate volume ceiling, reversing positions on netting accounts
- **Where:** `src/Runtime/YO4X.Mql5.Runtime/StandardLibrary/Mql5Trade.cs:437-452`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
  string resolved = Resolve(symbol);
  bool isLong = runtime.PositionGetInteger(Mql5TradeConstants.PositionType)
      == Mql5TradeConstants.PositionTypeBuy;

  Prepare(Mql5TradeConstants.TradeActionDeal, resolved);

  // Closing is an opposing deal: a long closes with a sell and a short with a buy, filled at
  // the price the opposing side of the book offers.
  Request.Type = isLong ? Mql5TradeConstants.OrderTypeSell : Mql5TradeConstants.OrderTypeBuy;
  Request.Price = runtime.SymbolInfoDouble(
      resolved,
      isLong ? Mql5TradeConstants.SymbolBid : Mql5TradeConstants.SymbolAsk);
  Request.Volume = volume;
  ```
- **Failure:** An EA holds an open Long position of 1.0 lot on a Netting account. During a partial take-profit step, `PositionClosePartial(symbol, 1.5)` is called (e.g. due to an unadjusted calculation or following a previous partial fill). Unlike official MQL5 `CTrade::PositionClosePartial` which checks `if (volume >= position_volume) return PositionClose(...)`, `CloseSelected` sends an opposing Sell order for the full 1.5 lots. In Netting mode, this completely liquidates the 1.0 lot Long position and opens a new 0.5 lot Short position, reversing the trading strategy into an unauthorized short exposure.
- **Fix:** In `PositionClosePartial`, read `runtime.PositionGetDouble(Mql5TradeConstants.PositionVolume)`. If `volume <= 0.0`, return `Reject(Mql5TradeConstants.RetcodeInvalid, "invalid volume")`; if `volume >= positionVolume`, delegate directly to `PositionClose(...)` with `positionVolume`.

### [P1] Constructor omits SetMarginMode initialization, causing IsHedging to evaluate false on hedging accounts
- **Where:** `src/Runtime/YO4X.Mql5.Runtime/StandardLibrary/Mql5Trade.cs:22-27`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
  public sealed class Mql5Trade(IMql5Runtime runtime)
  {
      private ulong magic;
      private ulong deviation = 10;
      private int typeFilling = Mql5TradeConstants.OrderFillingFok;
      private int marginMode;
      private bool async;
      private int logLevel = 2;
  ```
- **Failure:** On a Retail Hedging account (`ACCOUNT_MARGIN_MODE_RETAIL_HEDGING = 2`), an EA instantiates `CTrade trade;`. In official MQL5, `CTrade::CTrade()` calls `SetMarginMode()` in its constructor so `trade.IsHedging()` is immediately `true`. In `Mql5Trade.cs`, `marginMode` defaults to `0` (Netting). Unless the EA manually calls `trade.SetMarginMode()`, `trade.IsHedging()` evaluates to `false` and `trade.MarginMode()` returns `0`. Any EA branching on `if (trade.IsHedging())` to route hedging vs netting position handling executes the wrong logic branch.
- **Fix:** Call `SetMarginMode()` in the constructor body or initialize `private int marginMode = (int)runtime.AccountInfoInteger(Mql5AccountConstants.MarginMode);`.

### [P1] Hardcoded default OrderFillingFok causes immediate order rejections on Market Execution symbols
- **Where:** `src/Runtime/YO4X.Mql5.Runtime/StandardLibrary/Mql5Trade.cs:26`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
      private int typeFilling = Mql5TradeConstants.OrderFillingFok;
  ```
- **Failure:** Most retail Forex symbols with Market Execution permit only `ORDER_FILLING_IOC` or `ORDER_FILLING_RETURN` and reject `ORDER_FILLING_FOK`. A standard EA that instantiates `CTrade trade;` and calls `trade.Buy(0.1)` without explicitly calling `SetTypeFillingBySymbol()` transmits `Request.TypeFilling = 0` (`OrderFillingFok`). The broker rejects every trade request with `TRADE_RETCODE_INVALID_FILL` (10030), preventing the EA from entering any trades.
- **Fix:** In `Prepare()`, if filling mode is not explicitly configured by the user, dynamically resolve `runtime.SymbolInfoInteger(symbol, Mql5TradeConstants.SymbolFillingMode)` and choose an allowed filling flag.

### [P1] PositionClose and PositionModify by symbol operate only on a single position in Hedging mode
- **Where:** `src/Runtime/YO4X.Mql5.Runtime/StandardLibrary/Mql5Trade.cs:298-304`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
      public bool PositionModify(string? symbol, double stopLoss, double takeProfit)
      {
          string resolved = Resolve(symbol);
          return runtime.PositionSelect(resolved)
              ? ModifySelected(resolved, stopLoss, takeProfit)
              : Reject(Mql5TradeConstants.RetcodeInvalid, "no open position on " + resolved);
      }
  ```
- **Failure:** On a Hedging account with multiple open positions on "EURUSD" (e.g. Ticket #101 of 0.5 lots and Ticket #102 of 1.0 lot), an EA calls `PositionModify("EURUSD", newSL, newTP)` or `PositionClose("EURUSD")`. `runtime.PositionSelect("EURUSD")` selects only the position with the lowest ticket (`#101`). `PositionModify` updates stops only on `#101`, leaving `#102` with unprotected stale stops. `PositionClose` closes only `#101`, leaving `#102` open in the market without the EA's knowledge.
- **Fix:** When `IsHedging()` is true, `PositionClose(symbol)` and `PositionModify(symbol, sl, tp)` must iterate through all open positions matching the symbol via `PositionsTotal()` and apply the operation to every matching position.

### [P2] OrderModify and OrderDelete send empty symbol and omit OrderSelect existence validation
- **Where:** `src/Runtime/YO4X.Mql5.Runtime/StandardLibrary/Mql5Trade.cs:405-414`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
          Prepare(Mql5TradeConstants.TradeActionModify, string.Empty);
          Request.Order = ticket;
          Request.Price = price;
          Request.StopLoss = stopLoss;
          Request.TakeProfit = takeProfit;
          Request.TypeTime = typeTime;
          Request.Expiration = expiration;
          Request.StopLimit = stopLimit;
          return Send();
  ```
- **Failure:** In official MQL5 CTrade, `OrderModify` and `OrderDelete` first call `OrderSelect(ticket)`; if the order does not exist, they return `false` immediately, and if present, they set `m_request.symbol = OrderGetString(ORDER_SYMBOL)`. In `Mql5Trade.cs`, `OrderModify` and `OrderDelete` pass `string.Empty` to `Prepare()`, leaving `Request.Symbol = ""` and `Request.Type = 0` (Buy). If an EA passes an expired or invalid order ticket, it bypasses local validation and sends a malformed request to the trade server rather than rejecting locally with `TRADE_RETCODE_INVALID`.
- **Fix:** Call `runtime.OrderSelect(ticket)` in `OrderModify` and `OrderDelete`. If false, return `Reject(Mql5TradeConstants.RetcodeInvalid, "no order with ticket " + ticket)`. If true, set `Request.Symbol = runtime.OrderGetString(Mql5TradeConstants.OrderSymbol)`.

### [P2] Buy and Sell dispatch deal request with price 0.0 when quote retrieval fails
- **Where:** `src/Runtime/YO4X.Mql5.Runtime/StandardLibrary/Mql5Trade.cs:210-213`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
          string resolved = Resolve(symbol);
          double at = price > 0.0 ? price : runtime.SymbolInfoDouble(resolved, Mql5TradeConstants.SymbolAsk);
          return PositionOpen(resolved, Mql5TradeConstants.OrderTypeBuy, volume, at, stopLoss, takeProfit, comment);
  ```
- **Failure:** If market data is uninitialized or the symbol quote is unavailable, `runtime.SymbolInfoDouble(resolved, SymbolAsk)` returns `0.0`. `Buy` assigns `at = 0.0` and dispatches `PositionOpen` with `Price = 0.0`. On Instant or Request execution brokers, sending a deal order with price 0.0 causes a broker server rejection, whereas official MQL5 CTrade validates `if (price <= 0.0) return false;` after market price lookup and rejects locally before sending.
- **Fix:** In `Buy` and `Sell`, check `if (at <= 0.0) return Reject(Mql5TradeConstants.RetcodeInvalid, "failed to get current quote for " + resolved);` before calling `PositionOpen`.

## Referrals
- `src/Runtime/YO4X.Mql5.Live/LiveBrokerContext.cs` — `OrderSend` handles only Deal (Buy/Sell), Pending, and Remove actions; `TRADE_ACTION_SLTP` (6), `TRADE_ACTION_MODIFY` (7), and `TRADE_ACTION_CLOSE_BY` (10) are unsupported and rejected.
- `src/Runtime/YO4X.Mql5.Backtest/EngineRuntimeContext.cs` — `TryMapAction` and `TryMapOrderType` unconditionally require a valid `EngineOrderType` even for actions like `TRADE_ACTION_SLTP` and `TRADE_ACTION_REMOVE` which do not have an order type.

## Coverage gaps
- `src/Runtime/YO4X.Mql5.Runtime/StandardLibrary/Mql5Trade.cs:88-92` — Fallback branch in `SetTypeFillingBySymbol` when `permitted` mask is 0 or contains no FOK/IOC flags is not covered in `StandardLibraryTests.cs`.
- `src/Runtime/YO4X.Mql5.Runtime/StandardLibrary/Mql5Trade.cs:405-423` — `OrderModify` and `OrderDelete` behavior with non-existent tickets and empty symbol strings is untested against broker rejection handling.
- `src/Runtime/YO4X.Mql5.Runtime/StandardLibrary/Mql5Trade.cs:345-367` — `PositionClosePartial` behavior when requested volume exceeds current position volume on netting accounts is untested.


--- agy: gemini-3.7-flash-high | effort=high | mode=plan (read-only) | 333.1s | 437621 tok | id=a0f2571f-b9d8-417c-83c4-ce6a7a0f1311
