---
agent_id: E16
lane: Modules: Commands, Outbox, Deployments, ReadModels
scope:
  - src/Modules/Commands/YO4X.Commands/CommandTarget.cs
  - src/Modules/Commands/YO4X.Commands/ImpactPreview.cs
  - src/Modules/Commands/YO4X.Commands/TypedCommand.cs
  - src/Modules/Commands/YO4X.Commands/YO4X.Commands.csproj
  - src/Modules/Outbox/YO4X.Outbox/OutboxMessage.cs
  - src/Modules/Outbox/YO4X.Outbox/OutboxSchemaVersion.cs
  - src/Modules/Outbox/YO4X.Outbox/YO4X.Outbox.csproj
  - src/Modules/Deployments/YO4X.Deployments/Deployment.cs
  - src/Modules/Deployments/YO4X.Deployments/YO4X.Deployments.csproj
  - src/Modules/ReadModels/YO4X.ReadModels/OperationalReadModels.cs
  - src/Modules/ReadModels/YO4X.ReadModels/YO4X.ReadModels.csproj
status: COMPLETE
generated: 2026-08-29T11:26:26Z
counts: { P0: 1, P1: 3, P2: 1, P3: 0 }
---

# E16 — Modules: Commands, Outbox, Deployments, ReadModels

## Scope audited
- `src/Modules/Commands/YO4X.Commands/CommandTarget.cs` (283 lines)
- `src/Modules/Commands/YO4X.Commands/ImpactPreview.cs` (312 lines)
- `src/Modules/Commands/YO4X.Commands/TypedCommand.cs` (517 lines)
- `src/Modules/Commands/YO4X.Commands/YO4X.Commands.csproj` (14 lines)
- `src/Modules/Outbox/YO4X.Outbox/OutboxMessage.cs` (146 lines)
- `src/Modules/Outbox/YO4X.Outbox/OutboxSchemaVersion.cs` (184 lines)
- `src/Modules/Outbox/YO4X.Outbox/YO4X.Outbox.csproj` (14 lines)
- `src/Modules/Deployments/YO4X.Deployments/Deployment.cs` (276 lines)
- `src/Modules/Deployments/YO4X.Deployments/YO4X.Deployments.csproj` (14 lines)
- `src/Modules/ReadModels/YO4X.ReadModels/OperationalReadModels.cs` (33 lines)
- `src/Modules/ReadModels/YO4X.ReadModels/YO4X.ReadModels.csproj` (14 lines)

## Verdict
The audited modules provide foundational building blocks for lifecycle management, outbox messaging, and operational read models, but contain critical state machine and contract vulnerabilities. Specifically, `Deployment` maintains a stale reconciliation flag across containment transitions, allowing running deployments with live open positions to be immediately marked `Stopped` without verifying broker flatness. Additionally, `DeploymentConfiguration.ConfigurationHash` causes infinite recursive serialization leading to runtime process termination, and `TypedCommand` bypasses version watermark checks when dispatching against referenced previews.

## Findings

### [P0] Stale `BrokerReconciled` flag allows running deployments to be marked stopped before positions are flat
- **Where:** `src/Modules/Deployments/YO4X.Deployments/Deployment.cs:207-239`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
  public void EnterCloseOnly(string actorId, string reasonCode, string correlationId, DateTimeOffset occurredAt)
  {
      if (State is DeploymentState.Stopped or DeploymentState.Revoked)
      {
          throw InvalidTransition(DeploymentState.CloseOnly);
      }

      TransitionTo(DeploymentState.CloseOnly, actorId, reasonCode, correlationId, occurredAt);
  }
  ```
  ```csharp
  public void ConfirmFlatAndStopped(string actorId, string correlationId, DateTimeOffset occurredAt)
  {
      if (State is not (DeploymentState.StopAfterFlat or DeploymentState.Stopping or DeploymentState.CloseOnly))
      {
          throw InvalidTransition(DeploymentState.Stopped);
      }

      if (!BrokerReconciled)
      {
          throw new DomainException("BROKER_RECONCILIATION_REQUIRED", "A deployment cannot be reported stopped before broker reconciliation.");
      }

      TransitionTo(DeploymentState.Stopped, actorId, "FLAT_RECONCILED", correlationId, occurredAt);
  }
  ```
- **Failure:** When a deployment starts and reconciles, `ConfirmReconciled` sets `BrokerReconciled = true`. When the deployment later transitions to `EnterCloseOnly` or `StopAfterFlat` to flatten open positions, neither method resets `BrokerReconciled` to `false`. A caller can immediately invoke `ConfirmFlatAndStopped`, which passes because `BrokerReconciled` remains `true` from startup, marking the deployment `Stopped` and recording `FLAT_RECONCILED` while real trading positions remain open on the broker.
- **Fix:** Reset `BrokerReconciled = false` inside `EnterCloseOnly` and `StopAfterFlat`, and allow `BeginReconciliation` from `CloseOnly` and `StopAfterFlat` to re-validate terminal flat status before stopping.

### [P1] `DeploymentConfiguration.ConfigurationHash` property causes infinite recursive serialization and `StackOverflowException`
- **Where:** `src/Modules/Deployments/YO4X.Deployments/Deployment.cs:42`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
  public string ConfigurationHash => CanonicalJson.Sha256(this);
  ```
- **Failure:** `DeploymentConfiguration` is a record serialized by `CanonicalJson.Serialize(this)`. `System.Text.Json` reflects on public get-only properties during serialization, evaluating `ConfigurationHash`. Evaluating the getter calls `CanonicalJson.Sha256(this)` again, causing an infinite recursive loop resulting in an uncatchable `StackOverflowException` and process termination.
- **Fix:** Add `[System.Text.Json.Serialization.JsonIgnore]` to `ConfigurationHash` or replace the property with a dedicated method `ComputeConfigurationHash()` that serializes an explicit DTO contract omitting the hash itself.

### [P1] `TypedCommand.BeginDispatch` bypasses resource version watermark checks for referenced impact previews
- **Where:** `src/Modules/Commands/YO4X.Commands/TypedCommand.cs:244-261`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
  if (currentImpactPreview.ResolvedTargets.Count > 0)
  {
      var expected = currentImpactPreview.ResolvedTargets
          .Select(target => (target.ResourceId, target.ResourceVersion))
          .OrderBy(target => target.ResourceId)
          .ToArray();
      var actual = definitions
          .Select(target => (target.ResourceId, target.ResourceVersion))
          .OrderBy(target => target.ResourceId)
          .ToArray();

      if (!expected.SequenceEqual(actual))
      {
          throw new DomainException(
              "COMMAND_TARGET_SNAPSHOT_MISMATCH",
              "Frozen command targets do not match the revalidated resource snapshot.");
      }
  }
  ```
- **Failure:** When an impact preview is created using `ImpactPreview.CreateReferenced`, `ResolvedTargets` is empty (`Count == 0`). `BeginDispatch` guards target version verification behind `if (currentImpactPreview.ResolvedTargets.Count > 0)`. When dispatching against referenced previews, target version checking is entirely skipped and target versions are not checked against `currentImpactPreview.ResourceVersionWatermark`, allowing commands to dispatch against modified or higher-version resources without detection.
- **Fix:** Validate that all target definitions satisfy `definition.ResourceVersion <= currentImpactPreview.ResourceVersionWatermark` when `ResolvedTargets` is empty, and verify the snapshot reference.

### [P1] `Deployment.ConfirmReconciled` unconditionally forces state to `Running`, overriding restrictive states
- **Where:** `src/Modules/Deployments/YO4X.Deployments/Deployment.cs:195-204`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
  public void ConfirmReconciled(string actorId, string correlationId, DateTimeOffset occurredAt)
  {
      if (State != DeploymentState.Reconciling)
      {
          throw InvalidTransition(DeploymentState.Running);
      }

      BrokerReconciled = true;
      TransitionTo(DeploymentState.Running, actorId, "BROKER_RECONCILED", correlationId, occurredAt);
  }
  ```
- **Failure:** If a deployment enters `Fenced` or `Faulted` from a restricted mode (such as `CloseOnly` or `StopAfterFlat`) and undergoes reconciliation (`BeginReconciliation`), calling `ConfirmReconciled` unconditionally transitions the deployment state directly to `Running`. This unintentionally resurrects a restricted deployment back into full active trading and permits opening new positions.
- **Fix:** Track the target/pre-fault intended state or parameterize `ConfirmReconciled` to transition to the appropriate reconciled state (`Running`, `CloseOnly`, or `Stopped`) based on the underlying reconciliation evidence.

### [P2] `OutboxSchemaVersion.ValidateStored` silently skips validation for non-v4 message types with malformed versions
- **Where:** `src/Modules/Outbox/YO4X.Outbox/OutboxSchemaVersion.cs:90-101`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
  bool requiresExactSchemaVersion =
      messageType.StartsWith("yo4x.", StringComparison.Ordinal)
      && (messageType.EndsWith(".requested.v4", StringComparison.Ordinal)
          || string.Equals(
              messageType,
              "yo4x.user-operation.reconciliation-requested.v3",
              StringComparison.Ordinal));
  ValidateNumericVersionProperty(
      document.RootElement,
      "schemaVersion",
      resolvedVersion,
      requiresExactSchemaVersion);
  ```
- **Failure:** `requiresExactSchemaVersion` is hardcoded to `true` only for `.requested.v4` and `yo4x.user-operation.reconciliation-requested.v3`. For other versioned message types (e.g. `v2`, `v3`, `v5`), `required` is `false`. When `required` is `false`, `ValidateNumericVersionProperty` returns immediately without error if `schemaVersion` is absent or string-valued (e.g. `"schemaVersion": "invalid"`), allowing malformed wire payloads to silently bypass outbox schema validation.
- **Fix:** Enforce `requiresExactSchemaVersion = true` for all versioned message types matching `yo4x.*.vN` where `resolvedVersion > 1`, and reject non-numeric version values regardless of `required`.

## Referrals
- `src/Apps/YO4X.ControlPlane.Workers/Operations/PostgresDeploymentProjectionStore.cs:174` — Unhandled `InvalidOperationException` on projection CAS failure terminates entire tenant scan loop; scan cursor advances before projection succeeds, dropping failed projection retries until next full rotation.
- `src/Infrastructure/YO4X.Admin.Postgres/AdminReadRepository.cs:283` — `GetDeploymentAsync` coalesces `health.source_version` to `deployment.row_version` when health projection row is absent, presenting stale/unprojected component health as current.

## Coverage gaps
- `src/Modules/Commands/YO4X.Commands/TypedCommand.cs:360-365` — Compensation branch where a command has mixed dispatched and non-dispatched targets during partial dispatch failure is not covered by tests.
- `src/Modules/Outbox/YO4X.Outbox/OutboxSchemaVersion.cs:165-174` — Untested branch for non-numeric or missing `schemaVersion` on versioned `yo4x.*.v2` and `yo4x.*.v5` payloads when `required` is false.
- `src/Modules/Deployments/YO4X.Deployments/Deployment.cs:228-236` — Untested branch in `ConfirmFlatAndStopped` when called immediately following `EnterCloseOnly` or `StopAfterFlat` without intervening broker reconciliation.


--- agy: gemini-3.7-flash-high | effort=high | mode=plan (read-only) | 133.6s | 283433 tok | id=3aa2af75-0a19-45cb-ba74-7434ff423a0e
