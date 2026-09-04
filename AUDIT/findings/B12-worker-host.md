---
agent_id: B12
lane: Control Plane Workers Host & Fail-Stop Semantics
scope:
  - src/Apps/YO4X.ControlPlane.Workers/Program.cs
  - src/Apps/YO4X.ControlPlane.Workers/WorkerFailStopHosting.cs
  - src/Apps/YO4X.ControlPlane.Workers/WorkerHealthEndpoints.cs
  - src/Apps/YO4X.ControlPlane.Workers/WorkerHealthSnapshot.cs
  - src/Apps/YO4X.ControlPlane.Workers/WorkerOperationBoundary.cs
  - src/Apps/YO4X.ControlPlane.Workers/WorkerReadiness.cs
  - src/Apps/YO4X.ControlPlane.Workers/WorkerReadinessOptions.cs
status: COMPLETE
generated: 2026-08-29T11:27:00Z
counts: { P0: 0, P1: 1, P2: 0, P3: 0 }
---

# B12 — Control Plane Workers Host & Fail-Stop Semantics

## Scope audited
- `src/Apps/YO4X.ControlPlane.Workers/Program.cs` (49 lines)
- `src/Apps/YO4X.ControlPlane.Workers/WorkerFailStopHosting.cs` (15 lines)
- `src/Apps/YO4X.ControlPlane.Workers/WorkerHealthEndpoints.cs` (26 lines)
- `src/Apps/YO4X.ControlPlane.Workers/WorkerHealthSnapshot.cs` (9 lines)
- `src/Apps/YO4X.ControlPlane.Workers/WorkerOperationBoundary.cs` (169 lines)
- `src/Apps/YO4X.ControlPlane.Workers/WorkerReadiness.cs` (298 lines)
- `src/Apps/YO4X.ControlPlane.Workers/WorkerReadinessOptions.cs` (19 lines)

## Verdict
The control plane workers subsystem exhibits robust isolation, safe DI lifetime management, linearizable state transitions with synchronized health snapshot generation, and strict unconfirmed-termination propagation in `WorkerOperationBoundary`. However, `WorkerReadiness.GetLive()` acts as a false-green liveness probe: it unconditionally returns HTTP 200 (`Healthy: true`) without checking whether background workstreams have terminally halted or stopped. This prevents orchestrators (such as Kubernetes) from detecting dead worker loops and restarting the process.

## Findings

### [P1] Liveness probe unconditionally reports healthy when worker workstreams are terminally stopped
- **Where:** `src/Apps/YO4X.ControlPlane.Workers/WorkerReadiness.cs:62`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
  public WorkerHealthSnapshot GetLive() =>
      new(ContractVersion, Role, true, "live", "process_live");
  ```
- **Failure:** When hosted background workstreams (`OutboxDispatch` and `ControlWork`) fail or encounter a fatal unconfirmed termination condition (`WorkerOperationTerminationUnconfirmedException`), their work loops exit and transition `WorkerReadiness` into `RequiredWorkstreamState.Stopped`. While `/health/startup` and `/health/ready` fail closed, the liveness endpoint `/health/live` invokes `GetLive()`, which unconditionally returns HTTP 200 (`Healthy: true`, `process_live`) as long as the ASP.NET Core web host is reachable. Container orchestrators (e.g. Kubernetes) evaluating liveness probes will never restart or replace the dead worker process, stranding the worker in a permanent zombie state where dispatching and control loops are inactive.
- **Fix:** Synchronize on `_sync` in `WorkerReadiness.GetLive()` and check `AnyState(RequiredWorkstreamState.Stopped)`. If any required workstream is stopped, return an unhealthy snapshot (`Healthy = false`, code `"worker_stopped"`) so container liveness probes fail and trigger an orchestrator restart.

## Referrals
None.

## Coverage gaps
- `src/Apps/YO4X.ControlPlane.Workers/WorkerOperationBoundary.cs:94` — The branch where external `cancellationToken` cancellation is triggered during an in-flight operation and `ObserveTerminationAsync` fails confirmation within `cancellationConfirmationTimeout` (verifying that `WorkerOperationTerminationUnconfirmedException` is thrown during shutdown of an uncooperative task).
- `src/Apps/YO4X.ControlPlane.Workers/WorkerReadiness.cs:129` — The `now < healthyAt.Value` branch in `GetReady()` verifying that backwards system clock skew invalidates the readiness lease.


--- agy: gemini-3.7-flash-high | effort=high | mode=plan (read-only) | 97.3s | 223508 tok | id=e972e86c-14d6-4df7-9739-a6bda455212d
