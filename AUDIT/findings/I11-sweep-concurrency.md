---
agent_id: I11
lane: Cross-cutting concurrency sweep (workers, stores, hosts, shared state)
scope:
  - src/Infrastructure/YO4X.RuntimeControl.Postgres/UserOperationProtocolSingleFlight.cs
  - src/Apps/YO4X.ControlPlane.Workers/WorkerReadiness.cs
  - src/Modules/RuntimeOperations/YO4X.RuntimeOperations/WorkerOwnership.cs
  - src/BuildingBlocks/YO4X.BuildingBlocks/BoundedBooleanProbe.cs
  - src/Infrastructure/YO4X.ControlPlane.Postgres/ProofKeyRings.cs
  - src/Infrastructure/YO4X.ControlPlane.Postgres/PolicySignatureTrustStore.cs
  - src/Apps/YO4X.ControlPlane.Api/LocalBrokerCredentialVault.cs
  - src/Apps/YO4X.ControlPlane.Workers/Outbox/OutboxDispatchCoordinator.cs
  - src/Apps/YO4X.ControlPlane.Workers/WorkerOperationBoundary.cs
  - src/Apps/YO4X.ControlPlane.Workers/Operations/WorkerTenantScanCoordinator.cs
  - src/Apps/YO4X.ControlPlane.Workers/Operations/ControlWorkBackgroundService.cs
  - src/Application/YO4X.Trading.Application/BrokerCommandCoordinator.cs
  - src/Application/YO4X.Runtime.Application/StrategyEventProcessingCoordinator.cs
  - src/Runtime/YO4X.GatewayHost/BrokerCommandOneShotWorker.cs
  - src/Runtime/YO4X.Trading.ProcessIsolation/BrokerProcessClient.cs
  - src/Runtime/YO4X.Trading.ProcessIsolation/IsolatedMt5ProcessGateway.cs
  - src/Runtime/YO4X.Mql5.Live/LiveStrategyRunner.cs
  - src/Runtime/YO4X.Mql5.Live/LiveBrokerContext.cs
  - src/Runtime/YO4X.Mql5.Live/LiveBarSeries.cs
  - src/Runtime/YO4X.Mt5.ConnectionProbe.Windows/Mt5NetApiDemoTradeClient.cs
  - src/Runtime/YO4X.Mql5.Runtime/Mql5Runtime.Globals.cs
  - src/Runtime/YO4X.Mql5.Runtime/Mql5ChartObjectStore.cs
  - src/Runtime/YO4X.Mql5.Compilation/RoslynMql5CompilationHost.cs
  - src/Infrastructure/YO4X.Trading.Postgres/PostgresBrokerCommandStore.cs
  - src/Infrastructure/YO4X.Trading.Postgres/PostgresBrokerCommandLifecycleStore.cs
  - src/Apps/YO4X.Conversion.Worker/PostgresMql5CorpusStore.cs
  - src/Infrastructure/YO4X.LocalSecrets.Windows/LocalMt5Credential.cs
  - src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5IsolatedCompileOrchestrator.cs
  - src/Apps/YO4X.ControlPlane.Api/Program.cs
status: COMPLETE
generated: 2026-08-29T11:33:00Z
counts: { P0: 0, P1: 0, P2: 0, P3: 0 }
---

# I11 — Cross-cutting concurrency sweep (workers, stores, hosts, shared state)

## Scope audited
Every file in scope was opened, fully read, and examined for concurrency defects across the entire repository tree:

- [UserOperationProtocolSingleFlight.cs](file:///C:/Users/Dev23/Desktop/yo4x/src/Infrastructure/YO4X.RuntimeControl.Postgres/UserOperationProtocolSingleFlight.cs) (111 lines) — Concurrent execution deduplication and in-flight promise management.
- [WorkerReadiness.cs](file:///C:/Users/Dev23/Desktop/yo4x/src/Apps/YO4X.ControlPlane.Workers/WorkerReadiness.cs) (298 lines) — Worker subsystem health aggregation, lock-guarded status transitions, and probe publication.
- [WorkerOwnership.cs](file:///C:/Users/Dev23/Desktop/yo4x/src/Modules/RuntimeOperations/YO4X.RuntimeOperations/WorkerOwnership.cs) (271 lines) — In-memory state machine for worker heartbeat, acquisition, and release transitions.
- [BoundedBooleanProbe.cs](file:///C:/Users/Dev23/Desktop/yo4x/src/BuildingBlocks/YO4X.BuildingBlocks/BoundedBooleanProbe.cs) (157 lines) — Time-bounded boolean health probe coalescing and asynchronous execution boundary.
- [ProofKeyRings.cs](file:///C:/Users/Dev23/Desktop/yo4x/src/Infrastructure/YO4X.ControlPlane.Postgres/ProofKeyRings.cs) (272 lines) — Read/write locking over ECDSA/Ed25519 cryptographic key rings and lifecycle disposals.
- [PolicySignatureTrustStore.cs](file:///C:/Users/Dev23/Desktop/yo4x/src/Infrastructure/YO4X.ControlPlane.Postgres/PolicySignatureTrustStore.cs) (181 lines) — Lock-synchronized trust store for policy verification public keys.
- [LocalBrokerCredentialVault.cs](file:///C:/Users/Dev23/Desktop/yo4x/src/Apps/YO4X.ControlPlane.Api/LocalBrokerCredentialVault.cs) (486 lines) — `SemaphoreSlim`-guarded encrypted credential vault mutations, memory scrubbing, and sub-process execution.
- [OutboxDispatchCoordinator.cs](file:///C:/Users/Dev23/Desktop/yo4x/src/Apps/YO4X.ControlPlane.Workers/Outbox/OutboxDispatchCoordinator.cs) (445 lines) — Transactional outbox polling loop, lease settlement, and cancellation boundaries.
- [WorkerOperationBoundary.cs](file:///C:/Users/Dev23/Desktop/yo4x/src/Apps/YO4X.ControlPlane.Workers/WorkerOperationBoundary.cs) (169 lines) — Asynchronous boundary wrapper guarding worker operations against unobserved task cancellations.
- [WorkerTenantScanCoordinator.cs](file:///C:/Users/Dev23/Desktop/yo4x/src/Apps/YO4X.ControlPlane.Workers/Operations/WorkerTenantScanCoordinator.cs) (191 lines) — Workstream partition semaphore serialization, cursor tracking, and worker tenant batch processing.
- [ControlWorkBackgroundService.cs](file:///C:/Users/Dev23/Desktop/yo4x/src/Apps/YO4X.ControlPlane.Workers/Operations/ControlWorkBackgroundService.cs) (267 lines) — Background service orchestrating cyclical tenant work sweeps and error back-offs.
- [BrokerCommandCoordinator.cs](file:///C:/Users/Dev23/Desktop/yo4x/src/Application/YO4X.Trading.Application/BrokerCommandCoordinator.cs) (816 lines) — Authority window deadlines, submission dispatch, post-submission recovery, and reconciliation.
- [StrategyEventProcessingCoordinator.cs](file:///C:/Users/Dev23/Desktop/yo4x/src/Application/YO4X.Runtime.Application/StrategyEventProcessingCoordinator.cs) (465 lines) — Atomic concurrency throttle using `Interlocked`, unobserved task continuation handling, and event dispatching.
- [BrokerCommandOneShotWorker.cs](file:///C:/Users/Dev23/Desktop/yo4x/src/Runtime/YO4X.GatewayHost/BrokerCommandOneShotWorker.cs) (233 lines) — Gateway one-shot execution lifecycle and polling coordination.
- [BrokerProcessClient.cs](file:///C:/Users/Dev23/Desktop/yo4x/src/Runtime/YO4X.Trading.ProcessIsolation/BrokerProcessClient.cs) (423 lines) — Process boundary execution, concurrent stdout/stderr draining, and process tree termination.
- [IsolatedMt5ProcessGateway.cs](file:///C:/Users/Dev23/Desktop/yo4x/src/Runtime/YO4X.Trading.ProcessIsolation/IsolatedMt5ProcessGateway.cs) (227 lines) — Gateway-host execution wrapper, timeout-bound operation requests, and fail-closed error translation.
- [LiveStrategyRunner.cs](file:///C:/Users/Dev23/Desktop/yo4x/src/Runtime/YO4X.Mql5.Live/LiveStrategyRunner.cs) (262 lines) — Live strategy execution loop, `ConcurrentQueue` quote buffering, and dynamic assembly loading.
- [LiveBrokerContext.cs](file:///C:/Users/Dev23/Desktop/yo4x/src/Runtime/YO4X.Mql5.Live/LiveBrokerContext.cs) (517 lines) — Single-threaded runtime strategy context and synchronous broker order invocation.
- [LiveBarSeries.cs](file:///C:/Users/Dev23/Desktop/yo4x/src/Runtime/YO4X.Mql5.Live/LiveBarSeries.cs) (279 lines) — Bar formation, history trimming, and indicator buffer caching.
- [Mt5NetApiDemoTradeClient.cs](file:///C:/Users/Dev23/Desktop/yo4x/src/Runtime/YO4X.Mt5.ConnectionProbe.Windows/Mt5NetApiDemoTradeClient.cs) (774 lines) — Demo trade client, atomic quote publishing via `volatile` pair exchange, and reflection method caching.
- [Mql5Runtime.Globals.cs](file:///C:/Users/Dev23/Desktop/yo4x/src/Runtime/YO4X.Mql5.Runtime/Mql5Runtime.Globals.cs) (354 lines) — Per-run isolated terminal global variable state.
- [Mql5ChartObjectStore.cs](file:///C:/Users/Dev23/Desktop/yo4x/src/Runtime/YO4X.Mql5.Runtime/Mql5ChartObjectStore.cs) (360 lines) — Per-run visual chart object recording store.
- [RoslynMql5CompilationHost.cs](file:///C:/Users/Dev23/Desktop/yo4x/src/Runtime/YO4X.Mql5.Compilation/RoslynMql5CompilationHost.cs) (179 lines) — Thread-safe immutable Roslyn metadata compilation host.
- [PostgresBrokerCommandStore.cs](file:///C:/Users/Dev23/Desktop/yo4x/src/Infrastructure/YO4X.Trading.Postgres/PostgresBrokerCommandStore.cs) (1158 lines) — Durable PostgreSQL broker command authorization, claim dispatch, submission, and reconciliation persistence.
- [PostgresBrokerCommandLifecycleStore.cs](file:///C:/Users/Dev23/Desktop/yo4x/src/Infrastructure/YO4X.Trading.Postgres/PostgresBrokerCommandLifecycleStore.cs) (196 lines) — Port adapter mapping application lifecycle claims to durable PostgreSQL transactions.
- [PostgresMql5CorpusStore.cs](file:///C:/Users/Dev23/Desktop/yo4x/src/Apps/YO4X.Conversion.Worker/PostgresMql5CorpusStore.cs) (1044 lines) — Strategy import job transactions, persistence advisory lock acquisition, and replay verification.
- [LocalMt5Credential.cs](file:///C:/Users/Dev23/Desktop/yo4x/src/Infrastructure/YO4X.LocalSecrets.Windows/LocalMt5Credential.cs) (262 lines) — Lock-synchronized memory management for ephemeral MT5 credentials.
- [Mql5IsolatedCompileOrchestrator.cs](file:///C:/Users/Dev23/Desktop/yo4x/src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5IsolatedCompileOrchestrator.cs) (1337 lines) — Atomic single-capacity lease coordinator with asynchronous continuation cleanup.
- [Program.cs](file:///C:/Users/Dev23/Desktop/yo4x/src/Apps/YO4X.ControlPlane.Api/Program.cs) (617 lines) — Dependency Injection composition root across services and endpoints.

## Verdict
The audited concurrency model across workers, stores, hosts, and shared state is sound and rigorously implemented. Synchronization primitives (`lock`, `SemaphoreSlim`, `ReaderWriterLockSlim`, and `Interlocked`) are used correctly throughout, with locks never held across `await` or asynchronous I/O points, and all background continuations explicitly observing task faults. Dependency injection registrations cleanly separate singletons from scoped dependencies, and stateful components utilize atomic check-and-acquire mechanisms or database-level row and advisory locking.

## Findings
None. The cross-cutting sweep confirmed that the architecture enforces strict thread-safety invariants across all tiers:
- Deduplication and single-flight mechanisms ([`UserOperationProtocolSingleFlight<T>`](file:///C:/Users/Dev23/Desktop/yo4x/src/Infrastructure/YO4X.RuntimeControl.Postgres/UserOperationProtocolSingleFlight.cs#L11-L110)) safely coalesce concurrent callers via `Lazy<Task<T>>` with deterministic cleanup in `finally` blocks.
- Probe coalescing ([`BoundedBooleanProbe`](file:///C:/Users/Dev23/Desktop/yo4x/src/BuildingBlocks/YO4X.BuildingBlocks/BoundedBooleanProbe.cs#L8-L156)) protects in-flight task completion sources under a dedicated synchronization object without blocking caller threads during I/O.
- Sub-process and trade execution boundaries ([`BrokerProcessClient`](file:///C:/Users/Dev23/Desktop/yo4x/src/Runtime/YO4X.Trading.ProcessIsolation/BrokerProcessClient.cs#L34-L422), [`BrokerCommandCoordinator`](file:///C:/Users/Dev23/Desktop/yo4x/src/Application/YO4X.Trading.Application/BrokerCommandCoordinator.cs#L7-L800)) guarantee fail-closed state transitions with unlinked cancellation tokens on critical durable writes, preventing lost updates or ghost execution.
- Key rings and credential vaults ([`ProofKeyRing`](file:///C:/Users/Dev23/Desktop/yo4x/src/Infrastructure/YO4X.ControlPlane.Postgres/ProofKeyRings.cs#L5-L197), [`LocalMt5Credential`](file:///C:/Users/Dev23/Desktop/yo4x/src/Infrastructure/YO4X.LocalSecrets.Windows/LocalMt5Credential.cs#L12-L216)) guard sensitive byte buffers and enforce synchronous reader scopes under monitor locks, ensuring secure zeroing on disposal.
- Worker coordinator loops ([`WorkerTenantScanCoordinator`](file:///C:/Users/Dev23/Desktop/yo4x/src/Apps/YO4X.ControlPlane.Workers/Operations/WorkerTenantScanCoordinator.cs#L33-L190), [`ControlWorkBackgroundService`](file:///C:/Users/Dev23/Desktop/yo4x/src/Apps/YO4X.ControlPlane.Workers/Operations/ControlWorkBackgroundService.cs#L13-L266)) serialize per-consumer execution through dedicated semaphores and handle cancellation exceptions without leaving orphaned tasks.

## Referrals
None.

## Coverage gaps
None.


--- agy: gemini-3.7-flash-high | effort=high | mode=plan (read-only) | 240.5s | 565422 tok | id=0f84caa3-8e29-4ddf-89b7-0ca0139e307e
