---
agent_id: D08
lane: Control Plane Postgres Write Paths
scope:
  - src/Infrastructure/YO4X.ControlPlane.Postgres/PostgresBrokerAccountMutations.cs
  - src/Infrastructure/YO4X.ControlPlane.Postgres/PostgresBrokerServerDirectoryMutations.cs
  - src/Infrastructure/YO4X.ControlPlane.Postgres/PostgresCredentialMutations.cs
  - src/Infrastructure/YO4X.ControlPlane.Postgres/PostgresDeploymentMutations.cs
  - src/Infrastructure/YO4X.ControlPlane.Postgres/PostgresSessionMutations.cs
  - src/Infrastructure/YO4X.ControlPlane.Postgres/PostgresStrategyImportMutations.cs
  - src/Infrastructure/YO4X.ControlPlane.Postgres/PostgresMutationSupport.cs
status: COMPLETE
generated: 2026-08-29T11:26:00Z
counts: { P0: 0, P1: 0, P2: 0, P3: 0 }
---

# D08 — Control Plane Postgres Write Paths

## Scope audited
- `src/Infrastructure/YO4X.ControlPlane.Postgres/PostgresBrokerAccountMutations.cs` (261 lines)
- `src/Infrastructure/YO4X.ControlPlane.Postgres/PostgresBrokerServerDirectoryMutations.cs` (133 lines)
- `src/Infrastructure/YO4X.ControlPlane.Postgres/PostgresCredentialMutations.cs` (456 lines)
- `src/Infrastructure/YO4X.ControlPlane.Postgres/PostgresDeploymentMutations.cs` (230 lines)
- `src/Infrastructure/YO4X.ControlPlane.Postgres/PostgresSessionMutations.cs` (125 lines)
- `src/Infrastructure/YO4X.ControlPlane.Postgres/PostgresStrategyImportMutations.cs` (300 lines)
- `src/Infrastructure/YO4X.ControlPlane.Postgres/PostgresMutationSupport.cs` (231 lines)

## Verdict
The control-plane PostgreSQL mutation write paths are rigorously implemented and sound. Every mutation executes within an explicit `TenantPostgresTransaction` that binds authority locks, enforces tenant and user predicates on all queries, parameterises all SQL parameters with explicit database types, checks preconditions using optimistic row versions or `FOR UPDATE` row locks, and transactionalises state changes with matching audit events, outbox messages, and idempotency lease completions. No tenant isolation leaks, unparameterised SQL fragments, unhandled partial writes, or lost-update race conditions were detected.

## Findings
None.

The audited mutation pipeline consistently satisfies all safety invariants:
1. **Transaction atomicity**: All multi-statement mutations (business updates, audit records, outbox messages, and idempotency completion) execute within the caller's `TenantPostgresTransaction` and commit atomically at the end of the operation scope.
2. **Tenant isolation**: Every `SELECT`, `UPDATE`, `DELETE`, and `INSERT` statement includes strict `tenant_id = @tenant_id` filtering, and mutations operating on user-owned assets additionally enforce `user_id = @user_id`.
3. **Concurrency guards**: Read-modify-write workflows take pessimistic `FOR UPDATE` row locks (or `FOR UPDATE OF account`) and guard state transitions with optimistic `row_version = @expected_version` checks using PostgreSQL `RETURNING` clauses.
4. **Idempotency & Replay**: Mutations integrate with `PostgresIdempotencyRepository`, verifying request SHA-256 matching before leasing and issuing deterministic replays without persisting raw secrets.

## Referrals
None.

## Coverage gaps
- `src/Infrastructure/YO4X.ControlPlane.Postgres/PostgresCredentialMutations.cs:144-178`: The grant recovery branch where an expired grant is cleared and the broker account's `credential_state` transitions from `ingestion_pending`/`rotation_pending` back to `absent`/`ready` during ingestion initiation.
- `src/Infrastructure/YO4X.ControlPlane.Postgres/PostgresSessionMutations.cs:64-96`: Revocation requests against a session family that is already in a non-`active` terminal state (`revoked` or `expired`), where session update is skipped while still completing the idempotent mutation evidence.
- `src/Infrastructure/YO4X.ControlPlane.Postgres/PostgresStrategyImportMutations.cs:45-57`: Idempotent replay branch re-deriving the deterministic capability token for an existing strategy import session from stored key metadata.


--- agy: gemini-3.7-flash-high | effort=high | mode=plan (read-only) | 79.3s | 203204 tok | id=e193a764-c093-4b66-bfa4-be66041166be
