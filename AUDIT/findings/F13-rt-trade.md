---
agent_id: F13
lane: rt-trade
scope:
  - src/Runtime/YO4X.Mql5.Runtime/Mql5Runtime.Trade.cs
  - src/Runtime/YO4X.Mql5.Runtime/Mql5TradeTypes.cs
status: COMPLETE
generated: 2026-08-29T08:27:00Z
counts: { P0: 0, P1: 1, P2: 2, P3: 1 }
---

# F13 — rt-trade

## Scope audited
- `src/Runtime/YO4X.Mql5.Runtime/Mql5Runtime.Trade.cs` (485 lines)
- `src/Runtime/YO4X.Mql5.Runtime/Mql5TradeTypes.cs` (189 lines)

## Verdict
The data contract types (`Mql5TradeRequest`, `Mql5TradeResult`, `Mql5TradeCheckResult`) accurately map the full MQL5 field sets without dropping members. However, the runtime dispatch implementation has a critical semantic defect: all 12 out-parameter entity getters (`PositionGet*`, `OrderGet*`, `HistoryOrderGet*`, `HistoryDealGet*`) unconditionally return `true`, emitting falsified success with zeroed/empty values when no position or order is selected. Additionally, `OrderCheck` leaks stale fields when context evaluation fails with a null result, and `OrderSend` with `out` result produces an invalid `Retcode = 0` upon context failure.

## Findings

### [P1] Out-parameter entity property getters unconditionally return true when no entity is selected
- **Where:** `src/Runtime/YO4X.Mql5.Runtime/Mql5Runtime.Trade.cs:289`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
  public bool PositionGetDouble(int propertyId, out double value)
  {
      value = context.PositionGetDouble(propertyId);
      return true;
  }
  ```
- **Failure:** In MQL5, the out-parameter overloads of `PositionGetDouble`, `PositionGetInteger`, `PositionGetString`, `OrderGetDouble`, `OrderGetInteger`, `OrderGetString`, `HistoryOrderGetDouble`, `HistoryOrderGetInteger`, `HistoryOrderGetString`, `HistoryDealGetDouble`, `HistoryDealGetInteger`, and `HistoryDealGetString` return `false` and set `_LastError` if no entity is currently selected or if the requested ticket does not exist. In `Mql5Runtime.Trade.cs` (lines 289–313, 337–361, 391–415, 440–464), all 12 out-parameter overloads unconditionally `return true;`. When a strategy executes `if (PositionGetDouble(POSITION_PRICE_OPEN, out price))`, the check evaluates to `true` with `price = 0.0` even when no position exists, triggering invalid order updates, faulty trailing stops, or division-by-zero against phantom positions.
- **Fix:** Verify selection/existence in context before returning, and return `false` while setting `SetError(Mql5ErrorCodes.TradePositionNotFound / TradeOrderNotFound / TradeDealNotFound)` when no entity is selected or the property lookup fails.

### [P2] OrderCheck retains stale data in result structure when produced result is null
- **Where:** `src/Runtime/YO4X.Mql5.Runtime/Mql5Runtime.Trade.cs:228`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
  bool ok = context.OrderCheck(request, out Mql5TradeCheckResult produced);
  if (produced is not null)
  {
      result.Retcode = produced.Retcode;
      result.Balance = produced.Balance;
      result.Equity = produced.Equity;
      result.Profit = produced.Profit;
      result.Margin = produced.Margin;
      result.MarginFree = produced.MarginFree;
      result.MarginLevel = produced.MarginLevel;
      result.Comment = produced.Comment;
  }

  return ok;
  ```
- **Failure:** When `context.OrderCheck` returns `false` and assigns `produced = null`, `Mql5Runtime.OrderCheck` leaves `result` un-cleared. If a strategy reuses an `Mql5TradeCheckResult` instance across multiple calls, fields such as `MarginFree`, `Margin`, and `Retcode` retain values from preceding successful checks despite the current check failing.
- **Fix:** Add an `else` branch that calls `result.Clear()` when `produced is null` (matching the `CopyInto` behavior in `OrderSend`).

### [P2] OrderSend with out-parameter initializes Retcode to 0 on context failure
- **Where:** `src/Runtime/YO4X.Mql5.Runtime/Mql5Runtime.Trade.cs:194`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
  bool accepted = context.OrderSend(request, out result);
  result ??= new Mql5TradeResult();

  if (!accepted)
  {
      SetError(Mql5ErrorCodes.TradeSendFailed);
  }
  ```
- **Failure:** If `context.OrderSend` returns `false` and outputs `result = null`, `result ??= new Mql5TradeResult()` creates an instance with `Retcode == 0`. In MQL5, `0` is not a valid `ENUM_TRADE_RETURN_CODES` member (standard return codes are in the 10004–10044 range). Strategies or wrappers evaluating `result.retcode` will encounter `0`, bypassing standard trade rejection handling and error diagnostics.
- **Fix:** When `!accepted` and `result.Retcode == 0`, assign `result.Retcode = (uint)Mql5Constants.TradeRetcode.Invalid`.

### [P3] OrderCalcMargin and OrderCalcProfit use parameter name action instead of orderType and do not set LastError
- **Where:** `src/Runtime/YO4X.Mql5.Runtime/Mql5Runtime.Trade.cs:245`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
  public bool OrderCalcMargin(int action, string? symbol, double volume, double price, out double margin)
      => context.OrderCalcMargin(action, Resolve(symbol), volume, price, out margin);

  /// <inheritdoc />
  public bool OrderCalcProfit(int action, string? symbol, double volume, double priceOpen, double priceClose, out double profit)
      => context.OrderCalcProfit(action, Resolve(symbol), volume, priceOpen, priceClose, out profit);
  ```
- **Failure:** In official MQL5, the first argument to `OrderCalcMargin` and `OrderCalcProfit` is `ENUM_ORDER_TYPE` (0 for buy, 1 for sell). Naming the parameter `action` in `IMql5Runtime` invites callers to pass `ENUM_TRADE_REQUEST_ACTIONS` (`TRADE_ACTION_DEAL = 1`), inadvertently calculating sell margin instead of buy margin. Furthermore, neither method calls `SetError` when calculation returns `false`.
- **Fix:** Rename parameter `action` to `orderType` and call `SetError(Mql5ErrorCodes.InvalidParameter)` or `SetError(Mql5ErrorCodes.MarketUnknownSymbol)` when calculation fails.

## Referrals
- `src/Runtime/YO4X.Mql5.CodeGen/Mql5ClrTypes.cs:195` — `RuntimeMemberAliases` maps `retcode`, `deal`, `bid`, `ask`, `request_id`, and `retcode_external` for `MqlTradeResult` but omits `order`, `volume`, `price`, and `comment`.
- `src/Runtime/YO4X.Mql5.Backtest/EngineRuntimeContext.cs:126` — `OrderSend` mapping drops `TypeFilling`, `TypeTime`, `Expiration`, and `PositionBy` from `Mql5TradeRequest` because `EngineRequest` does not model them.
- `src/Runtime/YO4X.Mql5.Live/LiveBrokerContext.cs:233` — `OrderSend` ignores `request.Symbol`, `request.Deviation`, `request.TypeFilling`, `request.TypeTime`, and `request.Expiration`.
- `src/Runtime/YO4X.Mql5.Runtime/Mql5Constants.cs:153` — `TradeRetcode` lacks standard MQL5 return codes including `10030` (`TRADE_RETCODE_INVALID_FILL`), `10021` (`TRADE_RETCODE_PRICE_OFF`), `10022` (`TRADE_RETCODE_INVALID_EXPIRATION`), and `10033` (`TRADE_RETCODE_LIMIT_ORDERS`).

## Coverage gaps
- Untested branch in `src/Runtime/YO4X.Mql5.Runtime/Mql5Runtime.Trade.cs:228`: `OrderCheck` when `context.OrderCheck` returns `false` with `produced == null`, leaving `result` un-cleared.
- Untested branch in `src/Runtime/YO4X.Mql5.Runtime/Mql5Runtime.Trade.cs:194`: `OrderSend(Mql5TradeRequest, out Mql5TradeResult)` when `context.OrderSend` returns `false` with `result == null`, producing zeroed `Retcode = 0`.
- Untested branches in `src/Runtime/YO4X.Mql5.Runtime/Mql5Runtime.Trade.cs:289-313, 337-361, 391-415, 440-464`: All 12 out-parameter property getters when no position/order/history entity is selected or ticket is invalid.


--- agy: gemini-3.7-flash-high | effort=high | mode=plan (read-only) | 191.0s | 299924 tok | id=18d0b871-d74c-4565-9c81-e3820e037bfa
