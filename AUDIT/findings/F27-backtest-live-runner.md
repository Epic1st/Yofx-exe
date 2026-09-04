---
agent_id: F27
lane: backtest-live-runner
scope:
  - src/Runtime/YO4X.Mql5.Backtest/EngineRuntimeContext.cs
  - src/Runtime/YO4X.Mql5.Backtest/Mql5BacktestRunner.cs
  - src/Runtime/YO4X.Mql5.Live/LiveBarSeries.cs
  - src/Runtime/YO4X.Mql5.Live/LiveBrokerContext.cs
  - src/Runtime/YO4X.Mql5.Live/LiveStrategyRunner.cs
status: COMPLETE
generated: 2026-08-29T08:33:00Z
counts: { P0: 3, P1: 4, P2: 3, P3: 1 }
---

# F27 — backtest-live-runner

## Scope audited
- `src/Runtime/YO4X.Mql5.Backtest/EngineRuntimeContext.cs` (357 lines)
- `src/Runtime/YO4X.Mql5.Backtest/Mql5BacktestRunner.cs` (259 lines)
- `src/Runtime/YO4X.Mql5.Live/LiveBarSeries.cs` (232 lines)
- `src/Runtime/YO4X.Mql5.Live/LiveBrokerContext.cs` (412 lines)
- `src/Runtime/YO4X.Mql5.Live/LiveStrategyRunner.cs` (242 lines)

## Verdict
Broken across the live runtime boundary with severe backtest-live divergences. While `EngineRuntimeContext` and `Mql5BacktestRunner` provide a clean deterministic wrapper over the offline engine, `YO4X.Mql5.Live` contains critical flaws: unsynchronized multithreaded data races between vendor quote callbacks and strategy execution, destruction of opposing positions in hedging mode due to naive opposite-order closing heuristics, pending orders corrupting the open position collection, and total omission of trailing stop and order modification actions (`TRADE_ACTION_SLTP` / `TRADE_ACTION_MODIFY`). Strategies that backtest profitably will suffer position desynchronization, lost orders, and runtime faults in live execution.

## Findings

### [P0] Unsynchronized multithreaded quote ingestion races with live strategy execution
- **Where:** `src/Runtime/YO4X.Mql5.Live/LiveStrategyRunner.cs:188-210`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
  broker.QuoteObserver = (time, bid, ask) =>
  {
      if (series.Accept(time, bid, ask))
      {
          closes.Enqueue(true);
      }
  };
  ```
- **Failure:** The MT5 vendor network thread invokes `broker.QuoteObserver` concurrently while `strategy.OnTick()` executes on the worker thread. Inside `series.Accept()`, `bars.Add(bar)`, `indicator.Append(bar)`, and `bars.RemoveRange(0, excess)` modify internal lists without any synchronization lock. While `strategy.OnTick()` calls `CopyRates`, `BarClose(shift)`, or `IndicatorHandle` (which executes `foreach (Mql5Bar bar in bars)`), concurrent list mutations cause `InvalidOperationException: Collection was modified`, `ArgumentOutOfRangeException`, and corrupted historical reads. Furthermore, if a subsequent bar closes before `OnTick()` completes, bar shift indexing moves underneath the executing strategy.
- **Fix:** Synchronize state mutations inside `LiveBarSeries` with a reader-writer lock or private lock object, or queue incoming raw quotes onto a channel and process bar formation and indicator advancement strictly on the strategy execution thread.

### [P0] Opposite market order heuristic closes existing positions and breaks hedging and partial scaling
- **Where:** `src/Runtime/YO4X.Mql5.Live/LiveBrokerContext.cs:218-232`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
  // A close is an open in the opposite direction against a position we already hold.
  Mt5DemoOrderReceipt? facing = open.Find(position =>
      (position.Side == Mt5DemoSide.Buy && request.Type == 1)
      || (position.Side == Mt5DemoSide.Sell && request.Type == 0));
  if (facing is not null && Math.Abs(facing.Volume - request.Volume) < 1e-9)
  {
      Mt5DemoOrderReceipt closed = broker.CloseAsync(facing).GetAwaiter().GetResult();
      open.Remove(facing);
  ```
- **Failure:** On hedging accounts (the MT5 standard and backtest default), an EA opens a 0.01-lot Buy position. When an independent Sell signal triggers and sends a 0.01-lot Sell order (`TRADE_ACTION_DEAL`, `ORDER_TYPE_SELL`), `LiveBrokerContext.Open()` intercepts the order and calls `broker.CloseAsync(facing)` instead of submitting the Sell order. The existing Buy position is closed and the new Sell position is never opened. Furthermore, if an EA holds multiple positions and sends a close request targeting a specific ticket (`request.Position`), `open.Find()` ignores the ticket and closes whichever opposite position was added first.
- **Fix:** Only execute `CloseAsync` when `request.Position` or `request.Order` specifies an existing position ticket, or when explicitly operating under Netting mode; otherwise forward market orders directly to `broker.SendAsync()`.

### [P0] Pending orders are added to `open` position list, corrupting position counts and order state
- **Where:** `src/Runtime/YO4X.Mql5.Live/LiveBrokerContext.cs:278-281`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
  open.Add(placed);
  result.Retcode = Mql5Constants.TradeRetcode.Placed;
  result.Order = (ulong)placed.Ticket;
  return true;
  ```
- **Failure:** When a pending order (`BuyLimit`, `SellLimit`, `BuyStop`, `SellStop`) is placed, `LiveBrokerContext.Place()` adds the receipt to the `open` positions list. Consequently: 1) `PositionsTotal()` counts pending orders as active open positions; 2) an EA checking `if (PositionsTotal() == 0)` refuses to enter new trades; 3) `OrdersTotal()` returns `0` because pending orders are not tracked in a separate orders collection; 4) an opposing market order matches the pending order in `open.Find()` and attempts `broker.CloseAsync()` which fails with broker errors.
- **Fix:** Maintain a separate `List<Mt5DemoOrderReceipt> pendingOrders` collection for pending orders, implement `OrdersTotal()`, `OrderGetTicket()`, and `OrderSelect()` on `LiveBrokerContext`, and do not add pending orders to `open`.

### [P1] Stop loss / take profit modifications (`TRADE_ACTION_SLTP` and `TRADE_ACTION_MODIFY`) are refused live
- **Where:** `src/Runtime/YO4X.Mql5.Live/LiveBrokerContext.cs:189-201`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
  switch (request.Action)
  {
      case 1 when request.Type is 0 or 1:
          return Open(request, result);
      case 5:
          return Place(request, result);
      case 8:
          return Remove(request, result);
      default:
          journal($"refused action {request.Action} type {request.Type}: not supported live");
          result.Retcode = Mql5Constants.TradeRetcode.Invalid;
          return false;
  }
  ```
- **Failure:** In MQL5, `TRADE_ACTION_SLTP` (action 6) is the standard action to adjust position stops (trailing stops, breakeven stops), and `TRADE_ACTION_MODIFY` (action 7) updates pending orders. In backtest, both actions succeed via `Mql5SimulatedBroker`. In live, `LiveBrokerContext.OrderSend()` routes both actions to `default:`, logs a refusal, and returns `TradeRetcode.Invalid`. All trailing stops and position modifications silently fail in live trading.
- **Fix:** Add cases for action `6` (`Mql5TradeAction.Sltp`) and action `7` (`Mql5TradeAction.Modify`) that invoke `broker.ModifyAsync()`.

### [P1] `PositionGetDouble` for StopLoss/TakeProfit and `PositionGetInteger` for Magic number return 0 in Live
- **Where:** `src/Runtime/YO4X.Mql5.Live/LiveBrokerContext.cs:149-166`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
  public double PositionGetDouble(int propertyId) => selected is not { } position ? 0.0 : propertyId switch
  {
      3 => position.Volume,
      4 => position.Price,
      5 => position.Side == Mt5DemoSide.Buy ? series.Bid : series.Ask,
      10 => position.Profit,
      _ => 0.0,
  };

  public long PositionGetInteger(int propertyId) => selected is not { } position ? 0L : propertyId switch
  {
      1 => new DateTimeOffset(position.OpenTime, TimeSpan.Zero).ToUnixTimeSeconds(),
      2 => position.Side == Mt5DemoSide.Buy ? 0L : 1L,
      13 => position.Ticket,
      17 => position.Ticket,
      _ => 0L,
  };
  ```
- **Failure:** `POSITION_SL` is property 6, `POSITION_TP` is property 7, and `POSITION_MAGIC` is property 12. In backtest, `EngineRuntimeContext` returns valid values. In live, `PositionGetDouble(6)`, `PositionGetDouble(7)`, and `PositionGetInteger(12)` hit the discard branch and return `0.0` and `0L`. Strategies that filter positions by Magic number or calculate trailing stops relative to current StopLoss fail to recognize open positions or miscalculate stop levels.
- **Fix:** Store StopLoss, TakeProfit, and Magic in `Mt5DemoOrderReceipt`, and add property cases `6`, `7` to `PositionGetDouble` and `12` to `PositionGetInteger`.

### [P1] `PositionGetSymbol(int index)` fails to select position in `LiveBrokerContext`
- **Where:** `src/Runtime/YO4X.Mql5.Live/LiveBrokerContext.cs:145-146`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
  public string PositionGetSymbol(int index) =>
      index >= 0 && index < open.Count ? open[index].Symbol : string.Empty;
  ```
- **Failure:** MQL5 standard specification and `IMql5MarketContext` contract mandate that `PositionGetSymbol(index)` selects the position for subsequent property reads. In `EngineRuntimeContext.cs:96-98`, `PositionGetSymbol` selects the position via `engine.PositionGetTicket(index)`. In `LiveBrokerContext`, `selected` is not assigned. An EA iterating open positions via `for (int i = 0; i < PositionsTotal(); i++) { PositionGetSymbol(i); double p = PositionGetDouble(POSITION_PROFIT); }` reads null `selected` (or stale position data) in live.
- **Fix:** In `LiveBrokerContext.PositionGetSymbol(int index)`, assign `selected = index >= 0 && index < open.Count ? open[index] : null;` before returning `selected?.Symbol ?? string.Empty`.

### [P1] `LiveBarSeries.Publish` hardcodes bar spread to 0, corrupting `CopyRates` spread live
- **Where:** `src/Runtime/YO4X.Mql5.Live/LiveBarSeries.cs:138-146`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
  var bar = new Mql5Bar(
      formingOpenTime,
      formingOpen,
      formingHigh,
      formingLow,
      formingClose,
      formingTicks,
      0);
  bars.Add(bar);
  ```
- **Failure:** When publishing closed bars in live mode, `bar.Spread` is hardcoded to `0`. When an EA calls `CopyRates()`, `LiveBrokerContext.CopyRates()` populates `Mql5Rates.Spread = bar.Spread` (0). In backtest, `EngineRuntimeContext.CopyRates()` populates the actual spread (e.g. 20 points). Any EA that uses `rates[i].Spread` for spread filters, slippage buffers, or volatility checks reads `0` in live and takes trades during wide-spread market conditions it would avoid in backtest.
- **Fix:** Track current quote spread `(int)Math.Round((Ask - Bid) / Point)` inside `LiveBarSeries` and record the spread on the published `Mql5Bar`.

### [P2] `LivePeriods.Identifier` omits `W1` (weekly) timeframe
- **Where:** `src/Runtime/YO4X.Mql5.Live/LiveBrokerContext.cs:400-410`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
  public static int Identifier(int minutes) => minutes switch
  {
      1 => Mql5Constants.Timeframes.M1,
      5 => Mql5Constants.Timeframes.M5,
      15 => Mql5Constants.Timeframes.M15,
      30 => Mql5Constants.Timeframes.M30,
      60 => Mql5Constants.Timeframes.H1,
      240 => Mql5Constants.Timeframes.H4,
      1440 => Mql5Constants.Timeframes.D1,
      _ => Mql5Constants.Timeframes.Current,
  };
  ```
- **Failure:** In `EngineRuntimeContext.cs:353`, 10080 minutes is mapped to `Mql5Constants.Timeframes.W1` (`PERIOD_W1`). In `LivePeriods.Identifier`, 10080 is omitted and falls through to `PERIOD_CURRENT` (`0`). If a strategy runs on a weekly chart and checks `Period() == PERIOD_W1`, it returns true in backtest but false in live.
- **Fix:** Add `10080 => Mql5Constants.Timeframes.W1` to `LivePeriods.Identifier`.

### [P2] `CopyRates` and `CopySeries` throw `NullReferenceException` on unallocated target arrays
- **Where:** `src/Runtime/YO4X.Mql5.Backtest/EngineRuntimeContext.cs:213, 282`, `src/Runtime/YO4X.Mql5.Live/LiveBrokerContext.cs:352`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
  if (target.Length < count)
  {
      Array.Resize(ref target, count);
  }
  ```
- **Failure:** In MQL5, `CopyRates` and series copy functions are designed to accept unallocated dynamic arrays (`Mql5Rates[] rates = null`). Because `target.Length` is evaluated before checking `target is null` or calling `Array.Resize`, passing an uninitialized array immediately throws `NullReferenceException` and aborts strategy execution.
- **Fix:** Check `if (target is null || target.Length < count) Array.Resize(ref target, count);`.

### [P2] `LiveStrategyRunner` skips `OnDeinit` when `OnInit` returns failure, leaking resources
- **Where:** `src/Runtime/YO4X.Mql5.Live/LiveStrategyRunner.cs:156-167`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
  if (declaredInit is { ReturnType.Name: "Int32" })
  {
      int code = (int)(declaredInit.Invoke(strategy, null) ?? 0);
      if (code != 0)
      {
          return new LiveRunOutcome(
              LiveStopReason.InitRefused,
              $"the strategy's OnInit returned {code}",
              0,
              0);
      }
  }
  ```
- **Failure:** In MQL5 lifecycle specification and in backtest (`Mql5StrategyHost.cs:68`), `OnDeinit(Mql5DeinitReason.InitFailed)` is guaranteed to run if `OnInit` returns a non-zero code. In `LiveStrategyRunner`, when `OnInit` returns non-zero, `RunAsync` returns immediately without calling `strategy.OnDeinit()`. Any resources allocated in `OnInit` prior to failure (file handles, sockets, memory) are leaked.
- **Fix:** Call `strategy.OnDeinit(Mql5DeinitReason.InitFailed)` in `LiveStrategyRunner` before returning `LiveStopReason.InitRefused`.

### [P3] `LiveStrategyRunner` hardcodes reason 0 (`REASON_PROGRAM`) to `OnDeinit` on fault or cancellation
- **Where:** `src/Runtime/YO4X.Mql5.Live/LiveStrategyRunner.cs:231`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
  broker.QuoteObserver = null;
  try
  {
      strategy.OnDeinit(0);
  }
  ```
- **Failure:** When a live run terminates due to a strategy exception (`LiveStopReason.Faulted`) or operator cancellation (`LiveStopReason.Requested`), `strategy.OnDeinit(0)` is invoked with hardcoded reason `0` (`REASON_PROGRAM`). In backtest (`Mql5StrategyHost.cs:140-145`), abnormal stops pass `REASON_CLOSE` (9). If a strategy cleans up open positions conditionally on `reason == REASON_CLOSE`, it receives the wrong deinitialization code live.
- **Fix:** Pass `cancellationToken.IsCancellationRequested ? Mql5DeinitReason.Remove : Mql5DeinitReason.Close` to `strategy.OnDeinit()`.

## Referrals
- `src/Runtime/YO4X.Mql5.Engine/Hosting/Mql5StrategyHost.cs:82-84` — `context.AppendBar(bar)` or indicator advancement and `broker.ApplyBar(bar)` execute before `strategy.OnTick()`, preventing strategies from executing on the bar's open quote prior to bar completion.
- `src/Runtime/YO4X.Mt5.ConnectionProbe.Windows/Mt5NetApiDemoTradeClient.cs:655-661` — `QuoteObserver` callback is fired synchronously inside vendor socket event dispatch without try/catch or rate-limiting.
- `src/Runtime/YO4X.Mql5.Engine/Trading/Mql5SimulatedBroker.cs:159-165` — Intra-bar margin stop out is deferred until bar close.

## Coverage gaps
- `LiveBrokerContext.cs:284-297` — `Remove` (cancel pending order) path when ticket exists on broker but is already filled or cancelled lacks test coverage.
- `LiveBarSeries.cs:155-162` — `Trim()` behavior when newly registered indicators are added after `bars` list has exceeded `maximumBars` is untested.
- `Mql5BacktestRunner.cs:207-219` — Strategy constructor throwing runtime exceptions other than `MissingMethodException`/`TargetInvocationException`/`InvalidOperationException` is untested.


--- agy: gemini-3.7-flash-high | effort=high | mode=plan (read-only) | 258.6s | 338516 tok | id=d322a566-68b2-42b9-9243-15780afc6500
