---
agent_id: D12
lane: YO4X.RuntimeControl.Postgres
scope:
  - src/Infrastructure/YO4X.RuntimeControl.Postgres/ExecutionLeaseEnvelopeFactory.cs
  - src/Infrastructure/YO4X.RuntimeControl.Postgres/PostgresBrokerUserOperationResults.cs
  - src/Infrastructure/YO4X.RuntimeControl.Postgres/PostgresDeploymentUserOperationResults.cs
  - src/Infrastructure/YO4X.RuntimeControl.Postgres/PostgresRuntimeAssignments.cs
  - src/Infrastructure/YO4X.RuntimeControl.Postgres/PostgresRuntimeControlPlaneApplication.cs
  - src/Infrastructure/YO4X.RuntimeControl.Postgres/PostgresRuntimeEvents.cs
  - src/Infrastructure/YO4X.RuntimeControl.Postgres/PostgresRuntimeLeases.cs
  - src/Infrastructure/YO4X.RuntimeControl.Postgres/PostgresUserOperationCredentialBoundaryApplication.cs
  - src/Infrastructure/YO4X.RuntimeControl.Postgres/PostgresUserOperationGatewayApplication.cs
  - src/Infrastructure/YO4X.RuntimeControl.Postgres/PostgresUserOperationResultV5Application.cs
  - src/Infrastructure/YO4X.RuntimeControl.Postgres/PostgresUserOperationSupervisorDeliveryApplication.cs
  - src/Infrastructure/YO4X.RuntimeControl.Postgres/Properties/AssemblyInfo.cs
  - src/Infrastructure/YO4X.RuntimeControl.Postgres/RuntimeControlPostgresOptions.cs
  - src/Infrastructure/YO4X.RuntimeControl.Postgres/RuntimeEvidencePostgresDatabase.cs
  - src/Infrastructure/YO4X.RuntimeControl.Postgres/RuntimePostgresDatabase.cs
  - src/Infrastructure/YO4X.RuntimeControl.Postgres/RuntimeTargetTransition.cs
  - src/Infrastructure/YO4X.RuntimeControl.Postgres/UserOperationInvocationPostgresErrors.cs
  - src/Infrastructure/YO4X.RuntimeControl.Postgres/UserOperationInvocationPostgresOptions.cs
  - src/Infrastructure/YO4X.RuntimeControl.Postgres/UserOperationProtocolIdentity.cs
  - src/Infrastructure/YO4X.RuntimeControl.Postgres/UserOperationProtocolPostgresDatabases.cs
  - src/Infrastructure/YO4X.RuntimeControl.Postgres/UserOperationProtocolSingleFlight.cs
  - src/Infrastructure/YO4X.RuntimeControl.Postgres/UserOperationResultPostgresErrors.cs
  - src/Infrastructure/YO4X.RuntimeControl.Postgres/YO4X.RuntimeControl.Postgres.csproj
status: COMPLETE
generated: 2026-08-29T11:27:00Z
counts: { P0: 0, P1: 2, P2: 0, P3: 0 }
---

# D12 — YO4X.RuntimeControl.Postgres

## Scope audited

Opened and audited all 23 files in the scope:
- `src/Infrastructure/YO4X.RuntimeControl.Postgres/ExecutionLeaseEnvelopeFactory.cs` (57 lines)
- `src/Infrastructure/YO4X.RuntimeControl.Postgres/PostgresBrokerUserOperationResults.cs` (335 lines)
- `src/Infrastructure/YO4X.RuntimeControl.Postgres/PostgresDeploymentUserOperationResults.cs` (351 lines)
- `src/Infrastructure/YO4X.RuntimeControl.Postgres/PostgresRuntimeAssignments.cs` (411 lines)
- `src/Infrastructure/YO4X.RuntimeControl.Postgres/PostgresRuntimeControlPlaneApplication.cs` (431 lines)
- `src/Infrastructure/YO4X.RuntimeControl.Postgres/PostgresRuntimeEvents.cs` (551 lines)
- `src/Infrastructure/YO4X.RuntimeControl.Postgres/PostgresRuntimeLeases.cs` (520 lines)
- `src/Infrastructure/YO4X.RuntimeControl.Postgres/PostgresUserOperationCredentialBoundaryApplication.cs` (579 lines)
- `src/Infrastructure/YO4X.RuntimeControl.Postgres/PostgresUserOperationGatewayApplication.cs` (418 lines)
- `src/Infrastructure/YO4X.RuntimeControl.Postgres/PostgresUserOperationResultV5Application.cs` (356 lines)
- `src/Infrastructure/YO4X.RuntimeControl.Postgres/PostgresUserOperationSupervisorDeliveryApplication.cs` (279 lines)
- `src/Infrastructure/YO4X.RuntimeControl.Postgres/Properties/AssemblyInfo.cs` (4 lines)
- `src/Infrastructure/YO4X.RuntimeControl.Postgres/RuntimeControlPostgresOptions.cs` (50 lines)
- `src/Infrastructure/YO4X.RuntimeControl.Postgres/RuntimeEvidencePostgresDatabase.cs` (182 lines)
- `src/Infrastructure/YO4X.RuntimeControl.Postgres/RuntimePostgresDatabase.cs` (43 lines)
- `src/Infrastructure/YO4X.RuntimeControl.Postgres/RuntimeTargetTransition.cs` (85 lines)
- `src/Infrastructure/YO4X.RuntimeControl.Postgres/UserOperationInvocationPostgresErrors.cs` (32 lines)
- `src/Infrastructure/YO4X.RuntimeControl.Postgres/UserOperationInvocationPostgresOptions.cs` (42 lines)
- `src/Infrastructure/YO4X.RuntimeControl.Postgres/UserOperationProtocolIdentity.cs` (205 lines)
- `src/Infrastructure/YO4X.RuntimeControl.Postgres/UserOperationProtocolPostgresDatabases.cs` (234 lines)
- `src/Infrastructure/YO4X.RuntimeControl.Postgres/UserOperationProtocolSingleFlight.cs` (111 lines)
- `src/Infrastructure/YO4X.RuntimeControl.Postgres/UserOperationResultPostgresErrors.cs` (42 lines)
- `src/Infrastructure/YO4X.RuntimeControl.Postgres/YO4X.RuntimeControl.Postgres.csproj` (19 lines)

## Verdict

The module's core lifecycle protocols (worker assignment binding, execution lease signing and renewal under U0 authority locking, and user operation gateway / credential boundary invocations) are solidly structured with strong tenant isolation and row-level concurrency control. However, two serious defects exist in runtime event processing and operation result ingress: control command delivery failure events are unconditionally recorded in the event inbox, audited, and returned to callers as `"applied"`, and legacy broker/deployment user operation result recording attempts to acquire U0 locks and execute recorder functions against `RuntimeEvidencePostgresDatabase` (`yo4x_runtime_evidence`), which lacks those privileges and fails unconditionally with SQLSTATE `42501`.

## Findings

### [P1] Failed or non-applied control command delivery events are unconditionally recorded and reported as applied
- **Where:** `src/Infrastructure/YO4X.RuntimeControl.Postgres/PostgresRuntimeEvents.cs:188`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
  Guid inboxId = Guid.CreateVersion7();
  string processingState = target is null ? "accepted" : "applied";
  await InsertInboxAsync(
      transaction,
      actor,
      targetId,
  ```
- **Failure:** When a supervisor reports a failed or non-applied control command delivery via `RecordTargetDeliveryAsync` (for example, `TargetDeliveryInput` with `State = "failed"`, `ErrorCode = "broker_order_rejected"`, `ObservedResult = "Order failed to place"`), `AcceptEventAsync` hardcodes `processingState` to `"applied"` whenever `target` is not null. Although `ApplyTargetTransitionAsync` updates `control.command_targets.state` to `'failed'`, `InsertInboxAsync` inserts `processing_state = 'applied'` into `operations.runtime_event_inbox`, `AppendEvidenceAsync` emits an audit outbox event with action `runtime.target_delivery_applied`, and the method returns `RuntimeAcceptance(eventId, "applied", ...)`. Callers, inbox readers, and audit logs are falsely told that a failed command was successfully applied.
- **Fix:** Derive `processingState` and audit action name from the resolved `RuntimeTargetTransition.State` (e.g., preserving `"applied"` only when the transition state is `"applied"`, and recording `"rejected"` / failed status with `transition.LastErrorCode` on failure) so non-applied outcomes are truthfully recorded and returned.

### [P1] Broker and deployment user operation result ingress executes U0 authority lock and recorder queries on restricted evidence database
- **Where:** `src/Infrastructure/YO4X.RuntimeControl.Postgres/PostgresBrokerUserOperationResults.cs:186`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
  try
  {
      await using NpgsqlCommand command = transaction.CreateCommand(
          "select control.acquire_u0_authority_lock()");
      await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
      return transaction;
  }
  ```
- **Failure:** `BeginBrokerEvidenceAsync` (line 179) and `BeginDeploymentEvidenceAsync` (`PostgresDeploymentUserOperationResults.cs:197`) open transactions via `evidenceDatabase` (`RuntimeEvidencePostgresDatabase`), which connects as the role `yo4x_runtime_evidence`. Under PostgreSQL role definitions and `RuntimeEvidencePostgresDatabase.AssertCapabilitiesSql`, `yo4x_runtime_evidence` has execute privileges revoked on `control.acquire_u0_authority_lock()`, `control.record_broker_user_operation_result`, and `control.record_deployment_user_operation_result`, and has all privileges revoked on `audit.audit_events` and `messaging.outbox_messages`. Calling `RecordBrokerUserOperationResultAsync` or `RecordDeploymentUserOperationResultAsync` immediately throws an unmapped `PostgresException` (SQLSTATE `42501` `insufficient_privilege`) when attempting `select control.acquire_u0_authority_lock()`, preventing any broker or deployment user operation results from being recorded.
- **Fix:** Route `RecordBrokerUserOperationResultAsync` and `RecordDeploymentUserOperationResultAsync` through `database` (`RuntimePostgresDatabase`) using the supervisor runtime role rather than `evidenceDatabase` (which is restricted exclusively to `control.record_user_operation_result_v5`).

## Referrals

- `src/Apps/YO4X.ControlPlane.Api/RuntimeControlPostgresRegistration.cs` — registers `RuntimeEvidencePostgresDatabase` for `PostgresRuntimeControlPlaneApplication` even though `PostgresBrokerUserOperationResults` and `PostgresDeploymentUserOperationResults` cannot execute under that role contract.

## Coverage gaps

- `src/Infrastructure/YO4X.RuntimeControl.Postgres/PostgresRuntimeEvents.cs:188-265` — `AcceptEventAsync` branch handling `TargetDeliveryInput` with `State = "failed"` or `"unreachable"` is untested for verifying that inbox `processing_state` and `RuntimeAcceptance.Status` accurately reflect non-applied transition states.
- `src/Infrastructure/YO4X.RuntimeControl.Postgres/PostgresBrokerUserOperationResults.cs:23-45` and `src/Infrastructure/YO4X.RuntimeControl.Postgres/PostgresDeploymentUserOperationResults.cs:23-45` — live execution against an authenticated PostgreSQL connection is untested and failed to detect the `42501` permission denial on `control.acquire_u0_authority_lock()`.


--- agy: gemini-3.7-flash-high | effort=high | mode=plan (read-only) | 143.3s | 306027 tok | id=8d1f4ed1-53c0-4d35-94a8-a2517302078b
