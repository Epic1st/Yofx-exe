---
agent_id: I14
lane: sweep-idempotency
scope:
  - src/Application/YO4X.Trading.Application/BrokerCommandCoordinator.cs
  - src/Application/YO4X.Trading.Application/BrokerCommandCoordinatorOptions.cs
  - src/Application/YO4X.Trading.Application/BrokerCommandCoordinatorResults.cs
  - src/Application/YO4X.Trading.Application/BrokerCommandDispatchGuard.cs
  - src/Application/YO4X.Trading.Application/BrokerCommandLifecycleContracts.cs
  - src/Application/YO4X.Trading.Application/BrokerCommandLifecycleEvidence.cs
  - src/Application/YO4X.Trading.Application/BrokerCommandLifecycleReceiptValidator.cs
  - src/Application/YO4X.Trading.Application/BrokerCommandReconciliationGuard.cs
  - src/Application/YO4X.Trading.Application/BrokerCommandReconciliationValidator.cs
  - src/Application/YO4X.Runtime.Application/StrategyEventProcessingCoordinator.cs
  - src/Application/YO4X.ControlPlane.Application/UserOperationInvocationContracts.cs
  - src/Application/YO4X.ControlPlane.Application/UserOperationProviderInvokerContracts.cs
  - src/Apps/YO4X.ControlPlane.Workers/Outbox/OutboxDeliveryEnvelope.cs
  - src/Apps/YO4X.ControlPlane.Workers/Outbox/OutboxDispatchContracts.cs
  - src/Apps/YO4X.ControlPlane.Workers/Outbox/OutboxDispatchCoordinator.cs
  - src/Apps/YO4X.ControlPlane.Workers/Outbox/OutboxDispatchOptions.cs
  - src/Apps/YO4X.ControlPlane.Workers/Outbox/OutboxDispatcherBackgroundService.cs
  - src/Apps/YO4X.ControlPlane.Workers/Outbox/RetrySchedule.cs
  - src/Apps/YO4X.ControlPlane.Workers/Operations/PostgresUserOperationWorkStore.cs
  - src/Apps/YO4X.ControlPlane.Workers/Operations/UserOperationDispatchEnvelope.cs
  - src/BuildingBlocks/YO4X.Persistence.Postgres/PostgresOutboxRepository.cs
  - src/BuildingBlocks/YO4X.Persistence.Postgres/PostgresAuditOutboxWriter.cs
  - src/Infrastructure/YO4X.Trading.Postgres/PostgresBrokerCommandStore.cs
  - src/Infrastructure/YO4X.Trading.Postgres/PostgresBrokerCommandLifecycleStore.cs
  - src/Infrastructure/YO4X.Trading.Postgres/DurableBrokerCommandModels.cs
  - src/Runtime/YO4X.GatewayHost/BrokerCommandOneShotWorker.cs
  - src/Runtime/YO4X.Trading.ProcessIsolation/IsolatedMt5ProcessGateway.cs
  - src/Runtime/YO4X.Trading.ProcessIsolation/BrokerProcessClient.cs
  - src/Infrastructure/YO4X.Runtime.Postgres/PostgresStrategyEventTransactionStore.cs
  - src/Apps/YO4X.ControlPlane.Api/Program.cs
  - src/Apps/YO4X.ControlPlane.Api/FrontendProjectionEndpoints.cs
  - src/Apps/YO4X.Admin.Bff/AdminRoutes.cs
  - src/Apps/YO4X.EmergencySafety.Api/EmergencyRoutes.cs
  - src/BuildingBlocks/YO4X.Api/MutationPreconditionFilter.cs
  - src/BuildingBlocks/YO4X.Persistence.Postgres/PostgresIdempotencyRepository.cs
  - src/Infrastructure/YO4X.ControlPlane.Postgres/PostgresMutationSupport.cs
  - src/Infrastructure/YO4X.ControlPlane.Postgres/PostgresBrokerAccountMutations.cs
  - src/Infrastructure/YO4X.ControlPlane.Postgres/PostgresDeploymentMutations.cs
  - src/Infrastructure/YO4X.ControlPlane.Postgres/PostgresUserOperations.cs
  - src/Infrastructure/YO4X.Admin.Postgres/AdminIdempotency.cs
  - src/Infrastructure/YO4X.Admin.Postgres/AdminPostgresApplication.Commands.cs
  - src/Infrastructure/YO4X.Admin.Postgres/AdminMutationRepository.cs
  - src/BuildingBlocks/YO4X.Persistence.Postgres/Migrations/001_foundation.sql
  - src/BuildingBlocks/YO4X.Persistence.Postgres/Migrations/002_user_operation_invocation_protocol.sql
status: COMPLETE
generated: 2026-08-29T11:38:00Z
counts: { P0: 0, P1: 0, P2: 0, P3: 0 }
---

# I14 — sweep-idempotency

## Scope audited
- `src/Application/YO4X.Trading.Application/BrokerCommandCoordinator.cs` (816 lines)
- `src/Application/YO4X.Trading.Application/BrokerCommandCoordinatorOptions.cs` (79 lines)
- `src/Application/YO4X.Trading.Application/BrokerCommandCoordinatorResults.cs` (87 lines)
- `src/Application/YO4X.Trading.Application/BrokerCommandDispatchGuard.cs` (180 lines)
- `src/Application/YO4X.Trading.Application/BrokerCommandLifecycleContracts.cs` (132 lines)
- `src/Application/YO4X.Trading.Application/BrokerCommandLifecycleEvidence.cs` (336 lines)
- `src/Application/YO4X.Trading.Application/BrokerCommandLifecycleReceiptValidator.cs` (97 lines)
- `src/Application/YO4X.Trading.Application/BrokerCommandReconciliationGuard.cs` (131 lines)
- `src/Application/YO4X.Trading.Application/BrokerCommandReconciliationValidator.cs` (447 lines)
- `src/Application/YO4X.Runtime.Application/StrategyEventProcessingCoordinator.cs` (465 lines)
- `src/Application/YO4X.ControlPlane.Application/UserOperationInvocationContracts.cs` (946 lines)
- `src/Application/YO4X.ControlPlane.Application/UserOperationProviderInvokerContracts.cs` (244 lines)
- `src/Apps/YO4X.ControlPlane.Workers/Outbox/OutboxDeliveryEnvelope.cs` (114 lines)
- `src/Apps/YO4X.ControlPlane.Workers/Outbox/OutboxDispatchContracts.cs` (199 lines)
- `src/Apps/YO4X.ControlPlane.Workers/Outbox/OutboxDispatchCoordinator.cs` (445 lines)
- `src/Apps/YO4X.ControlPlane.Workers/Outbox/OutboxDispatchOptions.cs` (80 lines)
- `src/Apps/YO4X.ControlPlane.Workers/Outbox/OutboxDispatcherBackgroundService.cs` (78 lines)
- `src/Apps/YO4X.ControlPlane.Workers/Outbox/RetrySchedule.cs` (53 lines)
- `src/Apps/YO4X.ControlPlane.Workers/Operations/PostgresUserOperationWorkStore.cs` (3,831 lines)
- `src/Apps/YO4X.ControlPlane.Workers/Operations/UserOperationDispatchEnvelope.cs` (429 lines)
- `src/BuildingBlocks/YO4X.Persistence.Postgres/PostgresOutboxRepository.cs` (198 lines)
- `src/BuildingBlocks/YO4X.Persistence.Postgres/PostgresAuditOutboxWriter.cs` (217 lines)
- `src/Infrastructure/YO4X.Trading.Postgres/PostgresBrokerCommandStore.cs` (1,158 lines)
- `src/Infrastructure/YO4X.Trading.Postgres/PostgresBrokerCommandLifecycleStore.cs` (196 lines)
- `src/Infrastructure/YO4X.Trading.Postgres/DurableBrokerCommandModels.cs` (139 lines)
- `src/Runtime/YO4X.GatewayHost/BrokerCommandOneShotWorker.cs` (233 lines)
- `src/Runtime/YO4X.Trading.ProcessIsolation/IsolatedMt5ProcessGateway.cs` (227 lines)
- `src/Runtime/YO4X.Trading.ProcessIsolation/BrokerProcessClient.cs` (423 lines)
- `src/Infrastructure/YO4X.Runtime.Postgres/PostgresStrategyEventTransactionStore.cs` (663 lines)
- `src/Apps/YO4X.ControlPlane.Api/Program.cs` (617 lines)
- `src/Apps/YO4X.ControlPlane.Api/FrontendProjectionEndpoints.cs` (308 lines)
- `src/Apps/YO4X.Admin.Bff/AdminRoutes.cs` (505 lines)
- `src/Apps/YO4X.EmergencySafety.Api/EmergencyRoutes.cs` (356 lines)
- `src/BuildingBlocks/YO4X.Api/MutationPreconditionFilter.cs` (72 lines)
- `src/BuildingBlocks/YO4X.Persistence.Postgres/PostgresIdempotencyRepository.cs` (178 lines)
- `src/Infrastructure/YO4X.ControlPlane.Postgres/PostgresMutationSupport.cs` (231 lines)
- `src/Infrastructure/YO4X.ControlPlane.Postgres/PostgresBrokerAccountMutations.cs` (261 lines)
- `src/Infrastructure/YO4X.ControlPlane.Postgres/PostgresDeploymentMutations.cs` (230 lines)
- `src/Infrastructure/YO4X.ControlPlane.Postgres/PostgresUserOperations.cs` (657 lines)
- `src/Infrastructure/YO4X.Admin.Postgres/AdminIdempotency.cs` (99 lines)
- `src/Infrastructure/YO4X.Admin.Postgres/AdminPostgresApplication.Commands.cs` (734 lines)
- `src/Infrastructure/YO4X.Admin.Postgres/AdminMutationRepository.cs` (813 lines)
- `src/BuildingBlocks/YO4X.Persistence.Postgres/Migrations/001_foundation.sql` (18,915 lines)
- `src/BuildingBlocks/YO4X.Persistence.Postgres/Migrations/002_user_operation_invocation_protocol.sql` (6,746 lines)

## Verdict
The command, outbox, broker, and API mutation pipelines across YO4X are exceptionally sound, resilient, and rigorously designed against duplicate execution and non-idempotent retries. Every API mutation endpoint mandates high-entropy idempotency keys enforced by partial unique indexes in PostgreSQL (`control.idempotency_records`), returning deterministic replay responses or concurrency conflict errors on duplicate requests. Broker command lifecycle transitions enforce fail-closed two-phase commitment (`authorized` -> `send_in_progress` -> `acknowledged`/`unknown` -> `reconciling` -> `reconciled`): once an external broker boundary is entered, timeouts and transport faults are unconditionally classified as `Unknown` rather than failed, forcing deterministic reconciliation against broker state rather than blind resubmission. Outbox and event dispatch subsystems bundle domain mutations, outbox messages, and audit evidence into atomic PostgreSQL transactions with strictly deterministic, content-derived idempotency keys (`yo4x-outbox-v1/{TenantId:N}/{MessageId:N}`).

## Findings
None. The cross-cutting sweep verified end-to-end idempotency guarantees across all reachable execution hops:
- **Broker Command Dispatch & Gateway Safety (`BrokerCommandCoordinator.cs:287-302`, `001_foundation.sql:11010-11350`)**: Orders cannot be duplicated by retries. The broker dispatch flow commits a durable `send_in_progress` marker prior to invoking `IMt5Gateway.SendAsync`. Any timeout, cancellation, or exception during or after gateway entry is caught and treated as `GatewayCommandDisposition.Unknown`, advancing the lifecycle to `unknown` and requiring reconciliation (`ReconcileAsync`). The coordinator explicitly disallows re-sending a command whose outcome is ambiguous or replayed (`claim.Replayed` returns `"broker_command_dispatch_claim_replayed"` in `BrokerCommandDispatchGuard.cs:46-49`).
- **Crash Recovery State Machine (`001_foundation.sql:11738-11900`, `BrokerCommandCoordinator.cs:44-82`)**: If a process crashes while `send_in_progress` is held, lease expiry allows only a one-way transition to `unknown` via `control.recover_expired_broker_command_lifecycle`. The command can never revert to `authorized` or be re-dispatched to the gateway; it is strictly routed to reconciliation.
- **Idempotency Key Enforcement in PostgreSQL (`001_foundation.sql:3966-4056`, `PostgresIdempotencyRepository.cs:11-124`)**: Compound unique constraint `(tenant_id, actor_id, operation, idempotency_key)` backed by immutable triggers prevents duplicate execution of API and worker mutations. Concurrent duplicate calls receive `409 ResourceConflictException("REQUEST_IN_PROGRESS")`, identical completed calls return stored cached responses, and payload mismatches throw `409 ResourceConflictException("IDEMPOTENCY_KEY_REUSED")`.
- **Transactional Outbox Deduplication (`OutboxDispatchCoordinator.cs:174-185`, `OutboxDeliveryEnvelope.cs:35-47`)**: Messages are enqueued inside the originating business transaction (`PostgresAuditOutboxWriter.cs:163-174`). Delivery envelopes generate stable, deterministic keys (`$"yo4x-outbox-v1/{item.TenantId:N}/{item.MessageId:N}"`), and downstream `Duplicate` delivery results are recognized as successful completions (`SettlePublishedAsync`).
- **Runtime Strategy Events Atomicity (`StrategyEventProcessingCoordinator.cs:305-350`, `PostgresStrategyEventTransactionStore.cs:208-228`)**: Strategy evaluation state transitions and requested order action outbox messages commit in a single PostgreSQL function (`control.commit_strategy_event`). Replay attempts detect prior commits on `(tenant_id, deployment_id, generation, event_sequence)` and return the persisted receipt without re-emitting side effects.

## Referrals
None.

## Coverage gaps
None.


--- agy: gemini-3.7-flash-high | effort=high | mode=plan (read-only) | 496.9s | 497995 tok | id=d6b4bcee-bf61-4615-908e-1601cc411a08
