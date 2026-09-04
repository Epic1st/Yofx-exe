---
agent_id: I06
lane: Resource lifetime and disposal
scope:
  - whole tree (cross-cutting sweep)
status: COMPLETE
generated: 2026-08-29T11:45:00Z
counts: { P0: 0, P1: 0, P2: 3, P3: 2 }
---

# I06 — Resource lifetime and disposal

## Scope audited
- `src/Apps/YO4X.ControlPlane.Workers/WorkerOperationBoundary.cs` (169 lines)
- `src/Application/YO4X.Trading.Application/BrokerCommandCoordinator.cs` (816 lines)
- `src/Application/YO4X.Runtime.Application/StrategyEventProcessingCoordinator.cs` (465 lines)
- `src/Apps/YO4X.ControlPlane.Workers/Operations/WorkerTenantScanCoordinator.cs` (191 lines)
- `src/Apps/YO4X.Desktop/MainWindow.xaml.cs` (279 lines)
- `src/BuildingBlocks/YO4X.Persistence.Postgres/TenantPostgresTransaction.cs` (223 lines)
- `src/BuildingBlocks/YO4X.Persistence.Postgres/PostgresDatabase.cs` (185 lines)
- `src/BuildingBlocks/YO4X.Persistence.Postgres/PostgresTenantContextCapabilityProvider.cs` (279 lines)
- `src/BuildingBlocks/YO4X.Persistence.Postgres/PostgresOutboxRepository.cs` (198 lines)
- `src/Apps/YO4X.ControlPlane.Api/DevelopmentMt5ConnectionProbe.cs` (475 lines)
- `src/Apps/YO4X.ControlPlane.Api/LocalBrokerCredentialVault.cs` (486 lines)
- `src/Runtime/YO4X.Trading.ProcessIsolation/BrokerProcessClient.cs` (423 lines)
- `src/BuildingBlocks/YO4X.BuildingBlocks/BoundedBooleanProbe.cs` (157 lines)
- `src/Runtime/YO4X.Mt5.ConnectionProbe.Windows/Mt5NetApiTickHistoryClient.cs` (425 lines)
- `src/Runtime/YO4X.Mt5.ConnectionProbe.Windows/Mt5NetApiDemoTradeClient.cs` (774 lines)
- `src/Runtime/YO4X.Mt5.ConnectionProbe.Windows/PinnedMt5ServersDatEndpointReader.cs` (345 lines)
- `src/Tools/YO4X.MarketData.Mt5Import/LeanTickZipWriter.cs` (119 lines)
- `src/Infrastructure/YO4X.LocalSecrets.Windows/Mt5CredentialFileParser.cs` (465 lines)
- `src/Infrastructure/YO4X.LocalSecrets.Windows/DpapiLocalMt5CredentialVault.cs` (1308 lines)
- `src/Apps/YO4X.Conversion.Worker/Mql5QuarantineIntakeJob.cs` (1272 lines)
- `src/Apps/YO4X.ControlPlane.Api/ControlPlaneReadinessProbe.cs` (484 lines)
- `src/Runtime/YO4X.GatewayHost/BrokerCommandOneShotWorker.cs` (233 lines)
- `src/Runtime/YO4X.Trading.ProcessIsolation/AuthenticatedBrokerWorkerServer.cs` (135 lines)
- `src/Runtime/YO4X.Trading.ProcessIsolation/AuthenticatedBrokerConnectionProbeWorkerServer.cs` (95 lines)
- `src/Tools/YO4X.LiveBots/Program.cs` (294 lines)
- `src/Tools/YO4X.Mt5.EndpointDiscovery/Program.cs` (262 lines)
- `src/Tools/YO4X.Mt5.BrokerCatalogueImport/Program.cs` (650 lines)
- `src/Tools/YO4X.Mt5.AccountInspector/ConnectivitySweep.cs` (472 lines)
- `src/Tools/YO4X.Mt5.AccountInspector/BrokerEndpointDirectory.cs` (176 lines)
- `src/BuildingBlocks/YO4X.Api/AuthenticationExtensions.cs` (230 lines)

## Verdict
Disposal discipline across database connections, transactions, data readers, streams, and cryptographic buffers is consistently sound. `TenantPostgresTransaction`, `PostgresDatabase`, and repository queries systematically enforce `await using` on connections, commands, and readers, preventing connection pool exhaustion. However, several resource lifetime issues were identified where asynchronous `CancellationTokenSource` instances are prematurely disposed by `using` blocks while their underlying asynchronous operations remain in-flight, and synchronization primitives allocated on high-frequency leases are never disposed.

## Findings

### [P2] Premature CancellationTokenSource disposal while background operation is running in WorkerOperationBoundary
- **Where:** `src/Apps/YO4X.ControlPlane.Workers/WorkerOperationBoundary.cs:66`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
  using var operationCancellation = CancellationTokenSource.CreateLinkedTokenSource(
      cancellationToken);
  Task<T> operationTask = operation(operationCancellation.Token)
      ?? throw new InvalidOperationException("The worker dependency returned no operation task.");
  try
  {
      return await operationTask.WaitAsync(
  ```
- **Failure:** When a bounded worker operation exceeds `operationTimeout` or `cancellationToken` cancels, `ExecuteCoreAsync` invokes `ObserveTerminationAsync`. If `operationTask` fails to stop within `cancellationConfirmationTimeout`, the method attaches `_ = ObserveLateCompletionAsync(operationTask);` and throws `WorkerOperationTerminationUnconfirmedException` at line 89. Exiting `ExecuteCoreAsync` triggers the `using var operationCancellation` disposal. Because `operationTask` continues executing in the background, downstream asynchronous calls (such as database command execution or socket streams) that check or register callbacks on `operationCancellation.Token` throw `ObjectDisposedException`, generating unobserved task exceptions and preventing clean asynchronous shutdown.
- **Fix:** Defer disposal of `operationCancellation` until `operationTask` reaches a terminal state by registering disposal inside a continuation attached to `operationTask`, rather than disposing it synchronously when `terminationObserved` is false.

### [P2] Gateway CancellationTokenSource disposed while background gateway send remains in flight
- **Where:** `src/Application/YO4X.Trading.Application/BrokerCommandCoordinator.cs:187`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
  using var gatewayCancellation = CancellationTokenSource.CreateLinkedTokenSource(
      cancellationToken);
  ```
- **Failure:** In `BrokerCommandCoordinator.DispatchAsync`, `gatewayCancellation` is created with a `using` declaration and passed to `gateway.SendAsync(claim.Command, gatewayCancellation.Token)` (line 240). If `send.WaitAsync` times out at line 256 or `cancellationToken` cancels, `DispatchAsync` catches the exception or returns `GatewayTimeoutUnknown`, exiting the `using var gatewayCancellation` block at line 295. Disposing `gatewayCancellation` while `gateway.SendAsync` is still actively executing in the background causes in-flight socket/process operations attempting to register or unregister cancellation callbacks to throw `ObjectDisposedException`. (A symmetric issue exists in `ReconcileAsync` at line 488).
- **Fix:** Defer disposal of `gatewayCancellation` until the underlying `send` task finishes by attaching a terminal continuation (`send.ContinueWith(static (_, state) => ((CancellationTokenSource)state!).Dispose(), gatewayCancellation, ...)`), rather than disposing it at the end of the `using` block.

### [P2] Untrusted evaluation CancellationTokenSource disposed on host cancellation while background task is running
- **Where:** `src/Application/YO4X.Runtime.Application/StrategyEventProcessingCoordinator.cs:210`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
  catch (OperationCanceledException)
  {
      return Result(
          StrategyEventProcessingOutcome.EvaluationCancelled,
          "strategy_host_cancelled",
          reference);
  }
  ```
- **Failure:** In `StrategyEventProcessingCoordinator.ExecuteAsync`, `evaluationCancellation` is passed to `Task.Run(() => strategyHost.EvaluateAsync(..., evaluationCancellation.Token))`. If the untrusted host throws an internal `OperationCanceledException` while `cancellationToken.IsCancellationRequested` is false, execution enters `catch (OperationCanceledException)` at line 210 without setting `cancellationDisposalDeferred = true`. The `finally` block at lines 226–229 then immediately invokes `evaluationCancellation.Dispose()`. If background evaluation worker threads are still running, accessing the disposed `evaluationCancellation.Token` throws `ObjectDisposedException`.
- **Fix:** Set `cancellationDisposalDeferred = true` in all catch blocks where the background `evaluation` task may still be running, and dispose `evaluationCancellation` only via a terminal continuation on `evaluation`.

### [P3] WorkerTenantScanLease lifecycle SemaphoreSlim leaked on every cycle disposal
- **Where:** `src/Apps/YO4X.ControlPlane.Workers/Operations/WorkerTenantScanCoordinator.cs:172`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
  public async ValueTask DisposeAsync()
  {
      await lifecycleGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
      try
      {
          if (disposed)
          {
              return;
          }

          disposed = true;
          consumerGate.Release();
      }
      finally
      {
          lifecycleGate.Release();
      }
  }
  ```
- **Failure:** `WorkerTenantScanLease` instantiates `private readonly SemaphoreSlim lifecycleGate = new(1, 1);` on line 96 for every lease created across all background worker workstreams (`Outbox`, `CredentialGrantExpiry`, `DeploymentProjection`, `UserOperations`). In `DisposeAsync()`, `lifecycleGate` is acquired, `consumerGate` is released, and `lifecycleGate` is released, but `lifecycleGate.Dispose()` is never called. Under continuous background worker polling, thousands of unmanaged wait handles are left undisposed and rely entirely on GC finalization.
- **Fix:** In `WorkerTenantScanLease.DisposeAsync()`, dispose `lifecycleGate` after releasing it, or avoid allocating a per-lease `SemaphoreSlim` by using a lightweight non-blocking synchronisation mechanism.

### [P3] Undisposed Process instance on external link navigation in Desktop Shell
- **Where:** `src/Apps/YO4X.Desktop/MainWindow.xaml.cs:99`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
  if (requested is not null && DesktopNavigationPolicy.CanOpenExternally(requested))
  {
      Process.Start(new ProcessStartInfo(requested.AbsoluteUri) { UseShellExecute = true });
      StatusText.Text = "Opened the external HTTPS page in your default browser.";
  }
  ```
- **Failure:** In `MainWindow.xaml.cs` (lines 99 and 137), `Process.Start(...)` launches the default browser for external HTTPS navigation. `Process.Start` instantiates a `Process` object that wraps an unmanaged OS process handle (`SafeProcessHandle`). The returned instance is neither wrapped in a `using` statement nor explicitly disposed, leaking the process object and OS handle until finalization.
- **Fix:** Wrap the call in `using var process = Process.Start(...)` or discard and dispose the returned `Process` object.

## Referrals
None.

## Coverage gaps
- `src/Apps/YO4X.ControlPlane.Workers/WorkerOperationBoundary.cs:88` — unconfirmed termination timeout path where `operationTask` continues running after `operationCancellation` disposal is untested for downstream `ObjectDisposedException` propagation.
- `src/Application/YO4X.Trading.Application/BrokerCommandCoordinator.cs:256` — timeout and cancellation paths during `gateway.SendAsync` are untested for token callback access on the disposed `gatewayCancellation`.


--- agy: gemini-3.7-flash-high | effort=high | mode=plan (read-only) | 298.3s | 547231 tok | id=d97c668e-6d20-414a-b9e3-7afe3728c29b
