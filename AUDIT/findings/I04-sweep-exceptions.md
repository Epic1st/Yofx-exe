---
agent_id: I04
lane: exception-handling
scope:
  - src/Runtime/YO4X.Mql5.Live/LiveBrokerContext.cs
  - src/Runtime/YO4X.Mql5.Live/LiveStrategyRunner.cs
  - src/Runtime/YO4X.Mql5.Live/LiveBarSeries.cs
  - src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5Binder.cs
  - src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5Parser.cs
  - src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5IsolatedCompileOrchestrator.cs
  - src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5SemanticEquivalenceVerifier.cs
  - src/Application/YO4X.Trading.Application/BrokerCommandCoordinator.cs
  - src/Application/YO4X.Runtime.Application/StrategyEventProcessingCoordinator.cs
  - src/Application/YO4X.Runtime.Application/StrategyEventEvidence.cs
  - src/Runtime/YO4X.GatewayHost/BrokerCommandOneShotWorker.cs
  - src/Apps/YO4X.ControlPlane.Workers/Operations/ControlWorkBackgroundService.cs
  - src/Apps/YO4X.ControlPlane.Workers/Operations/PostgresUserOperationWorkStore.cs
  - src/Apps/YO4X.ControlPlane.Workers/Outbox/OutboxDispatchCoordinator.cs
  - src/Apps/YO4X.ControlPlane.Workers/WorkerOperationBoundary.cs
  - src/Apps/YO4X.ControlPlane.Api/LocalBrokerCredentialVault.cs
  - src/BuildingBlocks/YO4X.BuildingBlocks/BoundedBooleanProbe.cs
  - src/BuildingBlocks/YO4X.Persistence.Postgres/PostgresDatabase.cs
  - src/BuildingBlocks/YO4X.Persistence.Postgres/TenantContextCapability.cs
  - src/BuildingBlocks/YO4X.Persistence.Postgres/PostgresTenantContextCapabilityProvider.cs
  - src/Infrastructure/YO4X.Trading.Postgres/PostgresBrokerCommandStore.cs
  - src/Infrastructure/YO4X.Runtime.Postgres/PostgresStrategyEventTransactionStore.cs
  - src/Infrastructure/YO4X.LocalSecrets.Windows/Mt5CredentialFileParser.cs
  - src/Infrastructure/YO4X.LocalSecrets.Windows/DpapiLocalMt5CredentialVault.cs
  - src/Runtime/YO4X.Trading.ProcessIsolation/BrokerProcessClient.cs
  - src/Runtime/YO4X.Trading.ProcessIsolation/IsolatedMt5ProcessGateway.cs
  - src/Runtime/YO4X.Mt5.ConnectionProbe.Windows/Mt5NetApiDemoTradeClient.cs
  - src/Runtime/YO4X.Mt5.ConnectionProbe.Windows/Mt5NetApiAccountReader.cs
  - src/Runtime/YO4X.Mt5.ConnectionProbe.Windows/Mt5NetApiTickHistoryClient.cs
  - src/Frontend/YO4X.Web/src/features/strategies/StrategyCard.tsx
  - src/Frontend/YO4X.Web/src/features/overlays/LaunchWizard.tsx
status: COMPLETE
generated: 2026-08-29T11:45:00Z
counts: { P0: 0, P1: 2, P2: 2, P3: 0 }
---

# I04 — exception-handling

## Scope audited
- `src/Runtime/YO4X.Mql5.Live/LiveBrokerContext.cs` (516 lines)
- `src/Runtime/YO4X.Mql5.Live/LiveStrategyRunner.cs` (261 lines)
- `src/Runtime/YO4X.Mql5.Live/LiveBarSeries.cs` (278 lines)
- `src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5Binder.cs` (2694 lines)
- `src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5Parser.cs` (3064 lines)
- `src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5IsolatedCompileOrchestrator.cs` (1336 lines)
- `src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5SemanticEquivalenceVerifier.cs` (667 lines)
- `src/Application/YO4X.Trading.Application/BrokerCommandCoordinator.cs` (815 lines)
- `src/Application/YO4X.Runtime.Application/StrategyEventProcessingCoordinator.cs` (464 lines)
- `src/Application/YO4X.Runtime.Application/StrategyEventEvidence.cs` (1356 lines)
- `src/Runtime/YO4X.GatewayHost/BrokerCommandOneShotWorker.cs` (232 lines)
- `src/Apps/YO4X.ControlPlane.Workers/Operations/ControlWorkBackgroundService.cs` (266 lines)
- `src/Apps/YO4X.ControlPlane.Workers/Operations/PostgresUserOperationWorkStore.cs` (3830 lines)
- `src/Apps/YO4X.ControlPlane.Workers/Outbox/OutboxDispatchCoordinator.cs` (444 lines)
- `src/Apps/YO4X.ControlPlane.Workers/WorkerOperationBoundary.cs` (168 lines)
- `src/Apps/YO4X.ControlPlane.Api/LocalBrokerCredentialVault.cs` (485 lines)
- `src/BuildingBlocks/YO4X.BuildingBlocks/BoundedBooleanProbe.cs` (156 lines)
- `src/BuildingBlocks/YO4X.Persistence.Postgres/PostgresDatabase.cs` (184 lines)
- `src/BuildingBlocks/YO4X.Persistence.Postgres/TenantContextCapability.cs` (193 lines)
- `src/BuildingBlocks/YO4X.Persistence.Postgres/PostgresTenantContextCapabilityProvider.cs` (278 lines)
- `src/Infrastructure/YO4X.Trading.Postgres/PostgresBrokerCommandStore.cs` (1157 lines)
- `src/Infrastructure/YO4X.Runtime.Postgres/PostgresStrategyEventTransactionStore.cs` (662 lines)
- `src/Infrastructure/YO4X.LocalSecrets.Windows/Mt5CredentialFileParser.cs` (464 lines)
- `src/Infrastructure/YO4X.LocalSecrets.Windows/DpapiLocalMt5CredentialVault.cs` (1307 lines)
- `src/Runtime/YO4X.Trading.ProcessIsolation/BrokerProcessClient.cs` (422 lines)
- `src/Runtime/YO4X.Trading.ProcessIsolation/IsolatedMt5ProcessGateway.cs` (226 lines)
- `src/Runtime/YO4X.Mt5.ConnectionProbe.Windows/Mt5NetApiDemoTradeClient.cs` (773 lines)
- `src/Runtime/YO4X.Mt5.ConnectionProbe.Windows/Mt5NetApiAccountReader.cs` (387 lines)
- `src/Runtime/YO4X.Mt5.ConnectionProbe.Windows/Mt5NetApiTickHistoryClient.cs` (424 lines)
- `src/Frontend/YO4X.Web/src/features/strategies/StrategyCard.tsx` (104 lines)
- `src/Frontend/YO4X.Web/src/features/overlays/LaunchWizard.tsx` (736 lines)

## Verdict
Exception handling across the core persistence, outbox, and broker command coordinator architectures is disciplined, with strict boundary redacting, zero-allocation cryptographic memory zeroing in `catch`/`finally` blocks, and typed state-recovery markers. However, several critical paths exhibit improper exception handling: `LiveBrokerContext` uses an overly restrictive exception filter that allows transport and process boundary exceptions during order submission to escape and terminate the live strategy runner loop; `Mql5Binder` swallows catalog invocation exceptions and falls back to an obsolete MQL4 function symbol set; `BrokerCommandOneShotWorker` catches cancellations during graceful host shutdown and marks the host as failed; and `LiveBarSeries` silently swallows price-to-decimal conversion failures and returns zero digits for symbol precision.

## Findings

### [P1] LiveBrokerContext OrderSend exception filter allows transport failures to crash live runner
- **Where:** `src/Runtime/YO4X.Mql5.Live/LiveBrokerContext.cs:267`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
        catch (Exception exception) when (exception is InvalidOperationException
            or ArgumentOutOfRangeException
            or InvalidDataException
            or TimeoutException)
        {
            // A refusal is the broker's answer, not a crash. The strategy is told the order
            // failed, exactly as MQL5 would, and the reason is recorded for the operator.
            journal("order refused: " + exception.Message);
            result.Retcode = Mql5Constants.TradeRetcode.Reject;
            return false;
        }
  ```
- **Failure:** In `LiveBrokerContext.OrderSend`, order mutations (`Open`, `Place`, `Modify`, `Remove`) synchronously block on asynchronous gateway methods via `.GetAwaiter().GetResult()` (`broker.SendAsync`, `broker.CloseAsync`, `broker.ModifyAsync`, `broker.CancelAsync`). If a broker transport, socket, or process boundary exception occurs (such as `BrokerProcessBoundaryException`, `SocketException`, `IOException`, `HttpRequestException`, or `TaskCanceledException`), the filter in line 267 does not match. Because MQL5 `OrderSend` is expected to return `false` on order failure without throwing, the uncaught exception escapes `OrderSend` and propagates to `LiveStrategyRunner.cs:229`, halting the live strategy runner with `LiveStopReason.Faulted` rather than rejecting the order and preserving live execution.
- **Fix:** Expand the exception filter in `LiveBrokerContext.OrderSend` to handle all non-catastrophic exceptions (including `BrokerProcessBoundaryException`, `IOException`, `SocketException`, and `HttpRequestException`), log the error to the journal, and return `false` with `Mql5Constants.TradeRetcode.Reject` (or `Error`).

### [P1] Mql5Binder catches and swallows catalog exceptions to resurrect legacy MQL4 functions
- **Where:** `src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5Binder.cs:2201`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
            try
            {
                // The catalog is authoritative when present, including about absence:
                // it deliberately omits MQL4 carry-overs, and the binder must not
                // resurrect them from its own fallback set.
                return IsKnownFunc(name);
            }
#pragma warning disable CA1031
            catch (Exception)
#pragma warning restore CA1031
            {
                // Fall through to the embedded set.
            }
  ```
- **Failure:** The documented design rule in `Mql5Binder` stipulates that the runtime catalog is authoritative when present, especially regarding absence, and must not resurrect MQL4 legacy functions. When `IsKnownFunc` or `TryGetConstantMethod` encounters a runtime error during reflection execution, the bare `catch (Exception)` block silently swallows the fault with no warning and falls through to `Mql5BinderFallback.Functions.Contains(name)` (line 2208) and `Mql5BinderFallback.IsConstant(name)` (line 2237). This resurrects deprecated MQL4 functions and constants into the binder symbol table, allowing incompatible or invalid strategies to pass transpilation and semantic verification.
- **Fix:** If `CatalogAvailable` is true, handle or log catalog execution faults and return `false` rather than falling through to `Mql5BinderFallback`; only consult `Mql5BinderFallback` when `CatalogAvailable` is false at initialization.

### [P2] BrokerCommandOneShotWorker catches host shutdown cancellation and marks runtime status Failed
- **Where:** `src/Runtime/YO4X.GatewayHost/BrokerCommandOneShotWorker.cs:225`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
        catch (Exception)
        {
            // No exception details are logged: they may contain connection or
            // durable command material. The public status is deliberately fixed.
            runtimeStatus.MarkFailed();
        }
  ```
- **Failure:** When the hosting service initiates a graceful termination, `cancellationToken` is cancelled, causing `executor.ExecuteAsync(cancellationToken)` to throw `OperationCanceledException`. Because `RunOnceAsync` catches general `Exception` without excluding `OperationCanceledException` when cancellation has been requested, the graceful shutdown is treated as an unexpected crash and invokes `runtimeStatus.MarkFailed()`, leaving the host's terminal health status as `Failed` and emitting false-positive operational alerts.
- **Fix:** Add `catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)` prior to the general catch block to allow clean shutdown without marking the status as `Failed`.

### [P2] LiveBarSeries InferDigits silently swallows decimal cast exceptions and resets precision to zero
- **Where:** `src/Runtime/YO4X.Mql5.Live/LiveBarSeries.cs:273`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
        try
        {
            return ((decimal)value).Scale;
        }
        catch
        {
            return 0;
        }
  ```
- **Failure:** In `LiveBarSeries.InferDigits`, symbol precision (`_Digits`) is inferred by casting price doubles to `decimal` and reading `.Scale`. When `(decimal)value` throws `OverflowException` or when double values produce trailing-zero truncation, the bare `catch` block catches the exception and returns `0`. This sets `inferredDigits = 0` for the bar series, distorting pip calculations, stop-loss / take-profit tick offsets, and position lot sizing for live market feeds.
- **Fix:** Catch specific `OverflowException`, determine scale via formatted string inspection or broker symbol specification metadata, and fall back to existing symbol digits or default market precision rather than returning 0.

## Referrals
None.

## Coverage gaps
None.


--- agy: gemini-3.7-flash-high | effort=high | mode=plan (read-only) | 378.6s | 469112 tok | id=9c8cdce4-2bcb-4123-aca4-4a0328d9aa93
