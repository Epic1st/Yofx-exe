---
agent_id: I05
lane: Async / Await Correctness
scope:
  - whole tree (cross-cutting sweep)
status: COMPLETE
generated: 2026-08-29T11:35:00Z
counts: { P0: 1, P1: 1, P2: 1, P3: 1 }
---

# I05 — Async / Await Correctness

## Scope audited
- `src/Runtime/YO4X.Mql5.Live/LiveBrokerContext.cs` (517 lines)
- `src/Runtime/YO4X.Mql5.Live/LiveStrategyRunner.cs` (262 lines)
- `src/Runtime/YO4X.Mt5.ConnectionProbe.Windows/Mt5NetApiDemoTradeClient.cs` (774 lines)
- `src/Runtime/YO4X.Mt5.ConnectionProbe.Windows/Mt5NetApiTickHistoryClient.cs` (425 lines)
- `src/Runtime/YO4X.Trading.ProcessIsolation/BrokerProcessClient.cs` (423 lines)
- `src/Runtime/YO4X.Trading.ProcessIsolation/IsolatedMt5ProcessGateway.cs` (227 lines)
- `src/Runtime/YO4X.Trading.ProcessIsolation/AuthenticatedBrokerWorkerServer.cs` (135 lines)
- `src/Runtime/YO4X.GatewayHost/BrokerCommandOneShotWorker.cs` (233 lines)
- `src/Application/YO4X.Trading.Application/BrokerCommandCoordinator.cs` (816 lines)
- `src/Application/YO4X.Runtime.Application/StrategyEventProcessingCoordinator.cs` (465 lines)
- `src/BuildingBlocks/YO4X.BuildingBlocks/BoundedBooleanProbe.cs` (157 lines)
- `src/BuildingBlocks/YO4X.Persistence.Postgres/PostgresDatabase.cs` (185 lines)
- `src/BuildingBlocks/YO4X.Persistence.Postgres/PostgresOutboxRepository.cs` (198 lines)
- `src/Apps/YO4X.ControlPlane.Workers/WorkerOperationBoundary.cs` (169 lines)
- `src/Apps/YO4X.ControlPlane.Workers/Operations/WorkerTenantScanCoordinator.cs` (191 lines)
- `src/Apps/YO4X.ControlPlane.Workers/Operations/ControlWorkBackgroundService.cs` (267 lines)
- `src/Apps/YO4X.ControlPlane.Workers/Outbox/OutboxDispatcherBackgroundService.cs` (78 lines)
- `src/Apps/YO4X.ControlPlane.Api/DevelopmentMt5ConnectionProbe.cs` (475 lines)
- `src/Apps/YO4X.ControlPlane.Api/LocalBrokerCredentialVault.cs` (486 lines)
- `src/Apps/YO4X.SecretIngestion.Api/SecretBodyReader.cs` (74 lines)
- `src/Apps/YO4X.Desktop/MainWindow.xaml.cs` (279 lines)
- `src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5IsolatedCompileOrchestrator.cs` (1337 lines)
- `src/Infrastructure/YO4X.RuntimeControl.Postgres/UserOperationProtocolSingleFlight.cs` (111 lines)

## Verdict
The async/await foundation across Postgres persistence, process-isolation boundaries, and hosted worker services is disciplined and robust, utilizing `ConfigureAwait(false)`, structured cancellation, and defensive error-handling. However, critical async anti-patterns exist in the live trading runtime and concurrency building blocks: synchronous `.GetAwaiter().GetResult()` blocking on async network I/O in the MQL5 live trading path, probe starvation across cache expirations during late-completing dependency timeouts, and unobserved task exceptions when gateway calls time out in the trading coordinator.

## Findings

### [P0] Synchronous GetAwaiter().GetResult() on Async Broker Calls in Live Trading Path
- **Where:** `src/Runtime/YO4X.Mql5.Live/LiveBrokerContext.cs:292`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
  Mt5DemoOrderReceipt closed = broker.CloseAsync(target).GetAwaiter().GetResult();
  ```
- **Failure:** In `LiveStrategyRunner.DriveAsync`, ticks arriving from the broker queue are dispatched to `strategy.OnTick()` on a thread-pool thread. When the strategy places, modifies, or closes an order, `LiveBrokerContext` (lines 292, 316-317, 347, 372, and 391) calls `.GetAwaiter().GetResult()` to block synchronously on `Mt5NetApiDemoTradeClient`'s async network and reflection methods (`SendAsync`, `CloseAsync`, `ModifyAsync`, `CancelAsync`). If the broker connection experiences latency, packet loss, or stalls, the thread-pool thread is blocked. Furthermore, because `LiveBrokerContext` does not accept or propagate the runner's `CancellationToken`, host cancellation or shutdown cannot interrupt the blocked thread, stalling tick processing and preventing stop-loss or emergency position closures from executing.
- **Fix:** Update `LiveBrokerContext` and `IMql5MarketContext` to support asynchronous execution (or bridge order requests via an asynchronous channel), and propagate the runner's `CancellationToken` through all order operations instead of blocking synchronously on async tasks.

### [P1] Expired Single-Flight Probe Returns Stale Result and Prevents Future Probes on Hung Dependency
- **Where:** `src/BuildingBlocks/YO4X.BuildingBlocks/BoundedBooleanProbe.cs:94`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
  bool dependencyCompleted = probeTask.IsCompleted;
  PublishSnapshot(completion, value, dependencyCompleted);

  if (!dependencyCompleted)
  {
      await ObserveLateProbeAsync(probeTask, completion.Task).ConfigureAwait(false);
  }
  ```
- **Failure:** When `probeTask` exceeds `probeTimeout`, `PublishSnapshot` is called with `releaseSingleFlight: false`, which publishes a `false` snapshot with timestamp $T_0$ and completes `completion.Task`, but deliberately leaves `inFlight = completion.Task`. If the underlying probe delegate hangs (e.g. hung network socket), `ExecuteProbeAsync` awaits `ObserveLateProbeAsync` until that underlying task finishes. After the cache `lifetime` expires at $T_0 + \text{lifetime}$, subsequent callers calling `GetAsync` see that `lastCompleted` has expired, but `inFlight` is still referencing the completed `completion.Task`. As a result, `GetAsync` returns `inFlight` directly without initiating a new probe, immediately returning stale `false` snapshots indefinitely until the late task unblocks.
- **Fix:** In `PublishSnapshot`, clear `inFlight = null` whenever `completion.TrySetResult` is invoked so subsequent callers past the cache lifetime can schedule a new probe even if a previous late probe is still running.

### [P2] Unobserved Faulted Gateway Tasks on Timeout or Cancellation in Broker Command Coordinator
- **Where:** `src/Application/YO4X.Trading.Application/BrokerCommandCoordinator.cs:256`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
  GatewaySendResult raw = await send
      .WaitAsync(
          gatewayWindow - elapsedBeforeAwait,
          cancellationToken)
      .ConfigureAwait(false);
  ```
- **Failure:** `gateway.SendAsync(claim.Command, gatewayCancellation.Token)` creates and returns `Task<GatewaySendResult> send`. If `send.WaitAsync(...)` times out or `cancellationToken` cancels, `WaitAsync` throws a `TimeoutException` or `OperationCanceledException` and jumps to `catch (Exception)` at line 287. Unlike the pre-await check at line 248 which calls `_ = ObserveGatewayCompletionAsync(send);`, the catch block does not attach any observation continuation to `send`. If the background send operation subsequently faults with a transport exception, the exception remains unobserved on the underlying task, raising `TaskScheduler.UnobservedTaskException`. (A symmetric issue exists in `ReconcileAsync` at line 494).
- **Fix:** In `BrokerCommandCoordinator.DispatchAsync` and `ReconcileAsync`, register an exception observation continuation (`task.ContinueWith(static t => _ = t.Exception, ...)` or `ObserveGatewayCompletionAsync`) in a `finally` block or inside the catch block whenever `WaitAsync` does not complete successfully.

### [P3] Redundant Thread-Pool Dispatch of Already-Asynchronous Delegate in Bounded Boolean Probe
- **Where:** `src/BuildingBlocks/YO4X.BuildingBlocks/BoundedBooleanProbe.cs:70`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
  probeTask = Task.Run(
      async () => await probe(timeout.Token).ConfigureAwait(false),
      CancellationToken.None);
  ```
- **Failure:** `BoundedBooleanProbe` accepts `Func<CancellationToken, ValueTask<bool>> probe`. In `ExecuteProbeAsync`, it passes an async lambda that awaits `probe(...)` into `Task.Run`. This allocates an extra closure, schedules an unnecessary thread-pool work item, and creates an extra wrapper `Task` around an operation that is already asynchronous (`ValueTask<bool>`). On high-frequency health probes, this causes unnecessary thread hops and allocation overhead.
- **Fix:** Invoke `probe(timeout.Token).AsTask()` directly instead of wrapping the call inside `Task.Run(async () => ...)`.

## Referrals
- `src/Runtime/YO4X.Mt5.ConnectionProbe.Windows/Mt5NetApiTickHistoryClient.cs:239` — synchronous `ManualResetEventSlim.Wait(timeout)` blocks thread pool during tick history extraction without cooperative `CancellationToken` support.

## Coverage gaps
- `src/Application/YO4X.Trading.Application/BrokerCommandCoordinator.cs:287` — catch block when `send.WaitAsync` times out is untested for unobserved task exception emission when the background gateway transport faults late.
- `src/BuildingBlocks/YO4X.BuildingBlocks/BoundedBooleanProbe.cs:94` — late probe timeout path where `ObserveLateProbeAsync` outlives `lifetime` is untested for probe starvation and persistent stale-false caching.


--- agy: gemini-3.7-flash-high | effort=high | mode=plan (read-only) | 381.0s | 381962 tok | id=f25a5cbd-37e3-4932-831c-9bf0c4d433b3
