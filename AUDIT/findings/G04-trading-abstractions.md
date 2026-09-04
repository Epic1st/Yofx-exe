---
agent_id: G04
lane: Trading & Strategy Contract Abstractions
scope:
  - src/Runtime/YO4X.Trading.Abstractions/**
  - src/Runtime/YO4X.Strategy.Abstractions/**
status: COMPLETE
generated: 2026-08-29T11:30:00Z
counts: { P0: 0, P1: 5, P2: 3, P3: 2 }
---

# G04 — Trading & Strategy Contract Abstractions

## Scope audited
- `src/Runtime/YO4X.Trading.Abstractions/AuthorizedBrokerCommand.cs` (548 lines)
- `src/Runtime/YO4X.Trading.Abstractions/BrokerCommandLifecycle.cs` (188 lines)
- `src/Runtime/YO4X.Trading.Abstractions/ExecutionLeaseTrust.cs` (16 lines)
- `src/Runtime/YO4X.Trading.Abstractions/GatewayModels.cs` (238 lines)
- `src/Runtime/YO4X.Trading.Abstractions/IMt5Gateway.cs` (38 lines)
- `src/Runtime/YO4X.Trading.Abstractions/Properties/AssemblyInfo.cs` (5 lines)
- `src/Runtime/YO4X.Trading.Abstractions/YO4X.Trading.Abstractions.csproj` (15 lines)
- `src/Runtime/YO4X.Strategy.Abstractions/StrategyActions.cs` (263 lines)
- `src/Runtime/YO4X.Strategy.Abstractions/StrategyCanonicalText.cs` (56 lines)
- `src/Runtime/YO4X.Strategy.Abstractions/StrategyContract.cs` (258 lines)
- `src/Runtime/YO4X.Strategy.Abstractions/StrategyEvents.cs` (284 lines)
- `src/Runtime/YO4X.Strategy.Abstractions/StrategySnapshots.cs` (239 lines)
- `src/Runtime/YO4X.Strategy.Abstractions/YO4X.Strategy.Abstractions.csproj` (15 lines)

## Verdict
The contract layer establishes rigorous cryptographic digest binding, immutability, and deterministic JSON canonicalization across strategy and gateway boundaries. However, critical domain gaps in enum default assignments, lifecycle state transitions after reconciliation, missing order types in snapshots, and overly restrictive SL/TP invariants prevent valid trading behaviors and create failure modes on active order paths.

## Findings

### [P1] `PlaceOrderAction` and `UpdateProtectionAction` prohibit zero StopLoss and TakeProfit, rejecting unhedged entries and preventing protection removal
- **Where:** `src/Runtime/YO4X.Strategy.Abstractions/StrategyActions.cs:138-139`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
  ArgumentOutOfRangeException.ThrowIfNegativeOrZero(stopLoss);
  ArgumentOutOfRangeException.ThrowIfNegativeOrZero(takeProfit);
  ```
- **Failure:** When a strategy submits a `PlaceOrderAction` without fixed broker-side stop-loss or take-profit orders (e.g. `stopLoss: 0m` or `takeProfit: 0m` for algorithmic exits, trailing stops, or brokers with `SupportsBrokerHostedStopLoss == false`), the constructor throws `ArgumentOutOfRangeException`, rejecting the order. In `UpdateProtectionAction` (lines 191–192), `ThrowIfNegativeOrZero` similarly prevents passing `0m` to clear an existing stop-loss or take-profit on MT5 positions.
- **Fix:** Change `stopLoss` and `takeProfit` to nullable `decimal?` (or allow `0m`), replacing `ThrowIfNegativeOrZero` with `ThrowIfNegative` so zero or null denotes absent protection.

### [P1] Default enum values across trading and strategy domain map to active execution members (`Buy`, `Market`, `Place`, `Accepted`) rather than `Unknown`
- **Where:** `src/Runtime/YO4X.Trading.Abstractions/GatewayModels.cs:43-47`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
  public enum BrokerOrderSide
  {
      Buy = 0,
      Sell = 1
  }
  ```
- **Failure:** In `BrokerOrderSide` (0 = `Buy`), `RequestedOrderSide` (0 = `Buy`), `BrokerOrderType` (0 = `Market`), `BrokerCommandAction` (0 = `Place`), `RequestedExposureHint` (0 = `Increase`), and `GatewayCommandDisposition` (0 = `Accepted`), the `default(TEnum)` value of `0` evaluates to an active trading command or success state. Any uninitialized struct field, zeroed memory buffer, or partial deserializer payload defaults to placing a `Buy Market` order or treating an uninitialized gateway result as `Accepted`.
- **Fix:** Introduce explicit `Unknown = 0` / `Unspecified = 0` members as the default value for each enum, starting operational members at index 1.

### [P1] `BrokerCommandLifecycle` marks `Reconciled` as terminal, permanently blocking subsequent fills for active reconciled orders
- **Where:** `src/Runtime/YO4X.Trading.Abstractions/BrokerCommandLifecycle.cs:49-53`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
  public bool IsTerminal =>
      State is BrokerCommandState.Filled
          or BrokerCommandState.Cancelled
          or BrokerCommandState.Rejected
          or BrokerCommandState.Reconciled;
  ```
- **Failure:** When an in-flight command is reconciled with an active outcome such as `BrokerReconciliationMatch.Acknowledged` or `BrokerReconciliationMatch.PartiallyFilled`, `CompleteReconciliation` sets `State = BrokerCommandState.Reconciled`. Because `IsTerminal` includes `BrokerCommandState.Reconciled`, and `RecordPartialFill` / `RecordFilled` strictly enforce `RequireOneOf(BrokerCommandState.Acknowledged, BrokerCommandState.PartiallyFilled)`, subsequent fills received from the broker throw `DomainException("broker_command.transition_invalid")` and are dropped.
- **Fix:** In `CompleteReconciliation`, transition `State` to match the underlying match (`Acknowledged`, `PartiallyFilled`, `Filled`, `Cancelled`, `Rejected`) instead of unconditionally setting `BrokerCommandState.Reconciled`.

### [P1] `PlaceOrderAction` permits `RequestedOrderType.Limit`, `Stop`, and `StopLimit` with `requestedPrice = null`, emitting pending orders without trigger prices
- **Where:** `src/Runtime/YO4X.Strategy.Abstractions/StrategyActions.cs:133-136`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
  if (requestedPrice is { } normalizedRequestedPrice)
  {
      ArgumentOutOfRangeException.ThrowIfNegativeOrZero(normalizedRequestedPrice, nameof(requestedPrice));
  }
  ```
- **Failure:** When `OrderType` is `RequestedOrderType.Limit`, `Stop`, or `StopLimit`, `PlaceOrderAction` does not validate that `requestedPrice` is non-null. A strategy can instantiate `new PlaceOrderAction(..., orderType: RequestedOrderType.Limit, requestedPrice: null, ...)`, which passes constructor validation and validator bounds checks, but emits an invalid pending order missing an entry price to the gateway.
- **Fix:** In `PlaceOrderAction`, throw `ArgumentException` if `requestedPrice` is null when `OrderType != RequestedOrderType.Market`, and verify `requestedPrice` is null or ignored when `OrderType == RequestedOrderType.Market`.

### [P1] `AuthorizedBrokerCommand.HasValidTargetShape` does not validate positive volume for `Place` or `Close` actions
- **Where:** `src/Runtime/YO4X.Trading.Abstractions/AuthorizedBrokerCommand.cs:479-503`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
  BrokerCommandAction.Place =>
      command.TargetKind is null
      && command.TargetBrokerId is null
      && command.ExpectedTargetVolume is null
      && command.ExpectedTargetStatus is null
      && command.ExpectedTargetStopLoss is null
      && command.ExpectedTargetTakeProfit is null,
  ```
- **Failure:** For `BrokerCommandAction.Place`, `HasValidTargetShape` verifies target fields are null but never validates `command.Volume > 0`. For `BrokerCommandAction.Close`, `command.Volume <= command.ExpectedTargetVolume` allows negative values (e.g. `-5.0m <= 1.0m` returns `true`). An `AuthorizedBrokerCommand` can be signed and authorized with zero or negative trade volume.
- **Fix:** In `HasValidTargetShape`, assert `command.Volume > 0` for `Place` and `command.Volume is > 0 and <= command.ExpectedTargetVolume` for `Close`.

### [P2] `StrategyResult.SnapshotActions` does not validate elements against `null`, allowing null references in `IReadOnlyList<RequestedAction>`
- **Where:** `src/Runtime/YO4X.Strategy.Abstractions/StrategyContract.cs:51-57`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
  var result = new RequestedAction[count];
  for (int index = 0; index < count; index++)
  {
      result[index] = list[index];
  }

  return result;
  ```
- **Failure:** Passing a collection containing null (e.g. `[null!]`) to `new StrategyResult(state, actions)` succeeds and populates `RequestedActions` with null items despite the non-nullable `IReadOnlyList<RequestedAction>` contract. Any downstream consumer iterating `RequestedActions` without defensive null checks encounters a `NullReferenceException`.
- **Fix:** In `SnapshotActions`, throw `ArgumentException` if any element in `source` is null, matching the validation in `StrategySnapshot.Create`.

### [P2] `StrategyPendingOrderSnapshot` omits `OrderType`, preventing strategies from identifying pending order trigger mechanics
- **Where:** `src/Runtime/YO4X.Strategy.Abstractions/StrategySnapshots.cs:37-46`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
  public sealed record StrategyPendingOrderSnapshot(
      string OrderId,
      string Symbol,
      StrategyPositionSide Side,
      decimal Volume,
      decimal RequestedPrice,
      decimal? StopLoss,
      decimal? TakeProfit,
      bool OwnedByDeployment);
  ```
- **Failure:** `StrategyPendingOrderSnapshot` captures only `StrategyPositionSide Side` and `RequestedPrice`. When evaluating snapshot state, a strategy cannot distinguish whether a pending order is a `Buy Limit` (triggers below market) or `Buy Stop` (triggers above market), leading to flawed order management and hedging decisions.
- **Fix:** Add a `RequestedOrderType OrderType` property to `StrategyPendingOrderSnapshot`.

### [P2] `GatewayOperationResult<T>` permits `IsSuccess = true` with `Value = null`, breaking consumer null-safety assumptions
- **Where:** `src/Runtime/YO4X.Trading.Abstractions/GatewayModels.cs:236-237`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
  public sealed record GatewayOperationResult<T>(bool IsSuccess, string Code, T? Value)
      where T : class;
  ```
- **Failure:** `GatewayOperationResult<T>` does not enforce that `Value` is non-null when `IsSuccess` is `true`. A gateway implementation can instantiate `new GatewayOperationResult<BrokerAccountSnapshot>(true, "ok", null)`. Callers checking `if (result.IsSuccess)` and accessing `result.Value.Balance` throw `NullReferenceException` at runtime.
- **Fix:** Add static factory methods `Success(T value, string code)` throwing on null, and `Failure(string code, T? value = null)`.

### [P3] `BrokerDealSnapshot` omits `PositionId` and `DealEntryType`, preventing reconciliation of position lifecycle and fees
- **Where:** `src/Runtime/YO4X.Trading.Abstractions/GatewayModels.cs:140-147`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
  public sealed record BrokerDealSnapshot(
      string DealId,
      string OrderId,
      string Symbol,
      BrokerOrderSide Side,
      decimal Volume,
      decimal Price,
      DateTimeOffset BrokerTimestampUtc);
  ```
- **Failure:** In MT5, every deal contains `DEAL_POSITION_ID` and `DEAL_ENTRY` (`In`, `Out`, `InOut`, `OutBy`). Without `PositionId` and `DealEntryType` in `BrokerDealSnapshot`, reconciliation engines cannot determine whether a deal opened, modified, or closed a specific position, or whether it represents a balance correction or fee.
- **Fix:** Add `string? PositionId` and `string? DealEntry` to `BrokerDealSnapshot`.

### [P3] `ExecutionEvent` allows `StrategyExecutionEventKind.Filled` with zero filled volume and null fill price
- **Where:** `src/Runtime/YO4X.Strategy.Abstractions/StrategyEvents.cs:204-208`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
  ArgumentOutOfRangeException.ThrowIfNegative(filledVolume);
  if (fillPrice is { } normalizedFillPrice)
  {
      ArgumentOutOfRangeException.ThrowIfNegativeOrZero(normalizedFillPrice, nameof(fillPrice));
  }
  ```
- **Failure:** `ExecutionEvent` allows constructing a `StrategyExecutionEventKind.Filled` or `PartiallyFilled` event with `FilledVolume = 0m` and `FillPrice = null`. A strategy handler receiving a fill event without price and volume data encounters zero-division or null reference errors in internal state updates.
- **Fix:** In `ExecutionEvent` constructor, require `filledVolume > 0` and `fillPrice != null` when `ExecutionKind` is `PartiallyFilled` or `Filled`.

## Referrals
- `src/Application/YO4X.Trading.Application/BrokerCommandCoordinator.cs:649-656` — `NormalizeGatewayResult` does not validate that `result.OrderId` matches `command.CommandId` for new market order placement when `BrokerRequestId` is present.
- `src/Runtime/YO4X.Trading.ProcessIsolation/BrokerWorkerContracts.cs:71-79` — `BrokerWorkerResponse` allows `IsSuccess = true` concurrently with `SendResult.Disposition == Rejected`, creating conflicting protocol success semantics.

## Coverage gaps
- `src/Runtime/YO4X.Trading.Abstractions/BrokerCommandLifecycle.cs:77-91` — In `RecordGatewayResult`, passing a `GatewaySendResult` with null or whitespace `Code` invokes `Transition` which throws unhandled `ArgumentException`, leaving `State` stuck in `SendInProgress`.
- `src/Runtime/YO4X.Strategy.Abstractions/StrategyContract.cs:61-72` — In `StrategyResult.SnapshotActions`, an `IEnumerable<RequestedAction>` whose enumerator throws during `MoveNext()` leaves partially populated action lists unmanaged before validation.


--- agy: gemini-3.7-flash-high | effort=high | mode=plan (read-only) | 136.0s | 239820 tok | id=4e38a24e-b5eb-47dc-8331-c651ee5cba83
