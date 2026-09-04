---
agent_id: B10
lane: Control Plane Workers Operations
scope:
  - src/Apps/YO4X.ControlPlane.Workers/Operations/ControlWorkBackgroundService.cs
  - src/Apps/YO4X.ControlPlane.Workers/Operations/ControlWorkContracts.cs
  - src/Apps/YO4X.ControlPlane.Workers/Operations/ControlWorkOptions.cs
  - src/Apps/YO4X.ControlPlane.Workers/Operations/ControlWorkReadiness.cs
  - src/Apps/YO4X.ControlPlane.Workers/Operations/PostgresCredentialGrantExpiryStore.cs
  - src/Apps/YO4X.ControlPlane.Workers/Operations/PostgresDeploymentProjectionStore.cs
  - src/Apps/YO4X.ControlPlane.Workers/Operations/PostgresWorkerInfrastructure.cs
  - src/Apps/YO4X.ControlPlane.Workers/Operations/PostgresWorkerRegistration.cs
  - src/Apps/YO4X.ControlPlane.Workers/Operations/UserOperationDispatchEnvelope.cs
  - src/Apps/YO4X.ControlPlane.Workers/Operations/WorkerPolicySignatureTrustStore.cs
  - src/Apps/YO4X.ControlPlane.Workers/Operations/WorkerTenantScanCoordinator.cs
status: COMPLETE
generated: 2026-08-29T11:27:00Z
counts: { P0: 0, P1: 0, P2: 1, P3: 1 }
---

# B10 — Control Plane Workers Operations

## Scope audited

- `src/Apps/YO4X.ControlPlane.Workers/Operations/ControlWorkBackgroundService.cs` (267 lines)
- `src/Apps/YO4X.ControlPlane.Workers/Operations/ControlWorkContracts.cs` (61 lines)
- `src/Apps/YO4X.ControlPlane.Workers/Operations/ControlWorkOptions.cs` (164 lines)
- `src/Apps/YO4X.ControlPlane.Workers/Operations/ControlWorkReadiness.cs` (85 lines)
- `src/Apps/YO4X.ControlPlane.Workers/Operations/PostgresCredentialGrantExpiryStore.cs` (229 lines)
- `src/Apps/YO4X.ControlPlane.Workers/Operations/PostgresDeploymentProjectionStore.cs` (963 lines)
- `src/Apps/YO4X.ControlPlane.Workers/Operations/PostgresWorkerInfrastructure.cs` (539 lines)
- `src/Apps/YO4X.ControlPlane.Workers/Operations/PostgresWorkerRegistration.cs` (178 lines)
- `src/Apps/YO4X.ControlPlane.Workers/Operations/UserOperationDispatchEnvelope.cs` (429 lines)
- `src/Apps/YO4X.ControlPlane.Workers/Operations/WorkerPolicySignatureTrustStore.cs` (132 lines)
- `src/Apps/YO4X.ControlPlane.Workers/Operations/WorkerTenantScanCoordinator.cs` (191 lines)

## Verdict

The operations infrastructure for the Control Plane workers is architecturally sound, robustly isolated across tenants, and cryptographically rigorous. Durable tenant and deployment scan cursors are strictly serialized in PostgreSQL with row-level locks and database-enforced rotation triggers that prevent starvation and duplicate processing. Cryptographic policy signature verification in `WorkerPolicySignatureTrustStore` fails closed with constant-time equality checks and sensitive memory zeroing, and credential grant expiry is enforced on read during reservation as well as on background cleanup. Two minor robustness/quality issues were identified regarding backoff on repeated cycle failures and null-safety in digest validation.

## Findings

### [P2] Missing exponential backoff on repeated dependency or store operation failures in ControlWorkBackgroundService
- **Where:** `src/Apps/YO4X.ControlPlane.Workers/Operations/ControlWorkBackgroundService.cs:55-60`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
              while (!stoppingToken.IsCancellationRequested)
              {
                  DateTimeOffset now = _timeProvider.GetUtcNow();
                  _ = await RunCycleOnceAsync(now, stoppingToken).ConfigureAwait(false);
                  await Task.Delay(_options.PollInterval, _timeProvider, stoppingToken).ConfigureAwait(false);
              }
  ```
- **Failure:** When a database outage, network partition, or store failure occurs, `RunCycleOnceAsync` records the failure and returns `RequiredDependencyUnavailable` or `StoreOperationFailed`. However, `ExecuteAsync` discards the outcome (`_ = await ...`) and waits only for the fixed `_options.PollInterval` (which can be configured down to 100ms, defaulting to 1s). During sustained outages, the worker loops at maximum polling frequency, repeatedly executing probes and hammering the database without backoff or jitter.
- **Fix:** Inspect the `ControlWorkCycleOutcome` returned by `RunCycleOnceAsync` and apply an exponential backoff delay with jitter when consecutive cycles fail.

### [P3] `UserOperationDispatchGuard.IsSha256` throws `NullReferenceException` instead of returning false
- **Where:** `src/Apps/YO4X.ControlPlane.Workers/Operations/UserOperationDispatchEnvelope.cs:127-128`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
      private static bool IsSha256(string value) => value.Length == 64
          && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');
  ```
- **Failure:** If `FixedBindingEquals` is invoked with a null string (such as unpopulated dispatch or target binding properties evaluated in `IsReconciliationBindingCurrent`), `value.Length` dereferences null and throws `NullReferenceException` instead of evaluating to false and cleanly failing closed.
- **Fix:** Change `IsSha256` parameter to `string? value` and use pattern matching `value is { Length: 64 } && value.All(...)` consistent with `PostgresDeploymentProjectionStore.cs:873`.

## Referrals

None.

## Coverage gaps

- `src/Apps/YO4X.ControlPlane.Workers/Operations/PostgresDeploymentProjectionStore.cs:592-604`: The branch verifying reconciliation challenge bindings against `consumption.challenge_id` and `route_deployment_id == deployment.Id` lacks test coverage for mismatching challenge IDs returned from corrupted joins.
- `src/Apps/YO4X.ControlPlane.Workers/Operations/WorkerTenantScanCoordinator.cs:157-161`: The branch throwing `InvalidOperationException` when `durableStep.RotationCount > rotationCeiling.Value` has no unit test verifying that multi-rotation overruns are caught and rejected.


--- agy: gemini-3.7-flash-high | effort=high | mode=plan (read-only) | 133.9s | 262396 tok | id=864eb3c4-e6d5-4332-80e1-f65c7a8e851c
