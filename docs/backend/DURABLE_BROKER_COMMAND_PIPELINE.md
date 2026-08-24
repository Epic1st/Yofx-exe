# Durable broker-command pipeline

## Status

The repository contains a durable, fail-closed lifecycle proof for normalized broker commands. It does **not** contain a production authorization authority or a mutation-capable MT5 adapter. GatewayHost seals submission off, the supplied vendor DLL is never loaded by this path, and production PostgreSQL does not trust a conclusive terminal broker outcome.

This distinction is intentional: durable state transitions and the fail-stop child transport can be tested without representing synthetic risk inputs, unverified broker observations, or a vendor call as production authority.

## Authority and process boundaries

| Boundary | Current capability | Deliberate denial |
|---|---|---|
| Numeric risk engine | Deterministic evaluation of signed policies over a supplied immutable input | Does not authenticate the input's broker provenance or authorize a command |
| `PostgresBrokerCommandStore` | Claims, submission recording, recovery, and reconciliation lifecycle calls | Public `AuthorizeAsync` returns `BROKER_COMMAND_RISK_AUTHORITY_UNAVAILABLE` |
| `yo4x_trade_authorizer` | No broker-command SQL capability while trusted authority is absent | No `EXECUTE` on `control.authorize_broker_command`; no gateway lifecycle capability |
| `yo4x_gateway_runtime` | Execute-only access to the exact claim/record/recover/reconcile functions | No authorization function and no raw authority/evidence table privileges |
| GatewayHost | One exact command reference, one gateway-runtime connection, lifecycle coordinator, fixed health/status surface | No order endpoint, no raw store registration, no authorizer connection, no configurable submission enablement |
| Broker child-process boundary | Fresh one-request worker; local-fixed-volume exhaustive SHA-pinned launch manifest; direction-bound HMAC frames; strict bounded JSON; one pre-verification deadline budget; best-effort tree-kill request and bounded root-handle cleanup wait | No hard wall-clock bound or descendant-termination proof; same-host fail-closed only; no sandbox, privilege isolation, network policy, or isolated-runner attestation |
| `YO4X.Mt5.WorkerHost` | Registers only `Mt5ProofOnlyBrokerWorkerExecutor` | No client construction, login, network access, order, modification, cancel, close, or vendor-DLL loading |

The store retains an internal integration-only authorization seam so a disposable PostgreSQL fixture can prove the downstream lifecycle. The fixture must explicitly grant the exact function, and the production role script is reapplied to prove the grant is removed. This seam is not a supported runtime entry point.

## Frozen command evidence

An authorized fixture command binds the normalized command and provenance, exposure snapshot, numeric risk input and decision, execution-safety overlay, signed execution lease, and reconciliation commitment through canonical content and SHA-256 digests. Canonical lifecycle JSON is produced by the application layer, with UTC timestamps normalized to whole microseconds before hashing so PostgreSQL precision cannot silently change the evidence. The persistence adapter stores that exact JSON rather than rebuilding it, and receipt validation requires the expected state, exact `version + 1`, canonical content, and fixed-time digest matches. PostgreSQL also stores the exact signed execution-lease envelope in `signed_execution_lease_content` on the command row.

Dispatch and reconciliation claims return and rehydrate those frozen bytes. They do not reconstruct the signed envelope from the current mutable lease row. A later lease renewal therefore cannot silently replace the signature, payload, or claims that authorized the command. Dispatch checks the current lease state, action, workload ownership, generation, and expiry before any gateway entry. Reconciliation retains the immutable envelope and gateway-workload identity bindings but may continue after mutation authority expires or is revoked, because resolving an already-ambiguous outcome is safer than abandoning it.

All lifecycle functions acquire the common U0 authority lock before their ordered row locks. Database `clock_timestamp()` is the authority for claims, freshness, deadlines, recovery, audit timestamps, and state transitions.

## Dispatch lifecycle

The coordinator follows this sequence:

1. Recover a stale lifecycle if eligible. An expired ambiguous send can move only to `unknown`, never back to `authorized`.
2. Claim the exact command by command ID, authorization digest, signed-lease envelope digest, tenant, actor/workload, and correlation ID.
3. Rehydrate and validate the immutable command, authorization, frozen signed lease, deployment/generation, worker assignment, broker account, gateway artifact, exposure, risk decision, and demo-only safety bindings.
4. Start from PostgreSQL `AuthorityNowUtc` and add only monotonic elapsed time. Application wall-clock time is not promoted to authority.
5. Immediately before any possible gateway entry, refresh the conservative time and require enough authority for the complete gateway window plus safety margin. Defaults are a 500 ms send timeout, 100 ms margin, and 600 ms minimum window.
6. Persist a result through the gateway-runtime lifecycle function and validate the returned receipt.

Recording a new submission result also requires database authority to remain strictly before `dispatch_claim_expires_at`. Exact idempotent replay is accepted only when the original `send_completed_at` was inside that claim. Once the claim has expired, neither a new `Accepted` nor a new `Unknown` result can be written through the submission function; the recovery function exclusively changes an eligible ambiguous send to durable `unknown`.

`SubmissionEnabled` defaults to false. GatewayHost constructs the coordinator with it sealed false and ignores any attempted configuration value. Its ordinary proof-only result is therefore recorded before gateway entry as:

- disposition `submission_disabled`;
- `PreInvocationNotSentProven = true`;
- no broker request, order, or deal identifier; and
- `GatewayInvoked = false`.

PostgreSQL accepts that not-sent proof only in this exact shape. `submission_disabled` is terminal for this proof path and is not later reclassified as a stale ambiguous send.

After the gateway method is entered, `PreInvocationNotSentProven` is always false. Only `Accepted` or `Unknown` can cross the durable submission boundary, and both require reconciliation. A gateway-returned `Rejected` or `SubmissionDisabled` after entry is not sufficient negative proof and is normalized to `Unknown`. Exceptions, cancellation, timeout, invalid results, late returns, and an unconfirmed durable-write acknowledgement also produce or recover toward `Unknown`; the command is never blindly resent.

## Reconciliation lifecycle

Reconciliation claims are bound to the original command, authorization digest, scope digest, submission time, database-authority window, workload, artifact, account/deployment/generation, and frozen signed lease. The original gateway artifact digest is looked up and its signature state, approved lifecycle state, provenance, licence evidence, and network evidence are checked when reconciliation begins and again immediately before completion. Revocation or evidence loss blocks the transition.

The application validator requires a complete atomic snapshot, a strictly newer source sequence, bounded collections, consistent UTC windows, exact account/deployment/generation/artifact bindings, unique broker identifiers, and one exact command result.

Its narrow synthetic `Place` correlation check additionally requires a non-null broker order ID that was persisted from the submission result. That ID must exactly match the command result and a single order snapshot whose symbol, side, order type, volume, requested price, SL/TP, and ownership tag match the normalized command. Deal volume and broker status must agree with the reported shape. This prevents a same-shaped pre-existing order from being mistaken for the command, but it still does not authenticate the observation.

Even an exactly correlated `Place` snapshot returns `Inconclusive` with `broker_reconciliation_terminal_authority_unavailable`. `ModifyProtection`, `Cancel`, and `Close` also always validate as inconclusive because the current snapshot contract cannot bind their post-state to the exact broker request strongly enough.

Production SQL independently enforces the same authority boundary: until snapshots carry authenticated broker-observation provenance, it accepts only an inconclusive result and leaves the command `unknown`. No lifecycle-store implementation can receive terminal evidence from the application validator, and a caller holding the gateway database role cannot bypass that rule through the SQL function. No terminal broker outcome is currently trusted or persisted as reconciled.

## Recovery and replay rules

- A lost dispatch-claim acknowledgement leaves a durable `send_in_progress` marker; expiry changes it to `unknown`.
- An expired dispatch claim cannot record a new `Accepted` or `Unknown` result; only an exact result persisted within the original claim may replay after expiry.
- A recorded `Accepted` or `Unknown` result requires reconciliation.
- A pre-entry `submission_disabled` proof is not an ambiguous external mutation and is not selected by lifecycle recovery.
- An expired reconciliation claim, missed completion deadline, or missed begin deadline moves eligible ambiguous state to `unknown`.
- Idempotent replay is accepted only when command, claim, disposition/evidence digest, and persisted state match exactly.
- Reusing an evidence identifier with different content fails rather than overwriting evidence.
- Receipt validation treats a missing, inconsistent, or unconfirmed database result as durable recovery required.

## GatewayHost posture

GatewayHost is a one-shot proof host, not an order API. When configured with an exact tenant/workload/command reference and trusted P-256 lease verification keys, it can drive only the gateway-runtime lifecycle surface. The same least-privilege database instance is supplied to the store's legacy constructor slots; the raw store and database object remain local to an owner and are not registered for dependency resolution.

Startup health may report HTTP 200 only after the configured one-shot work reaches a proven terminal outcome; that is process-startup evidence, not mutation readiness. The separate readiness endpoint always reports HTTP 503 with the fixed code `gateway_host_proof_only_not_mutation_ready`. Status output is fixed and does not expose connection strings, command contents, exceptions, credentials, or broker identifiers.

## Process fail-stop boundary and remaining isolation gap

The coordinator detects a send that returns a `Task` too late and persists `Unknown`; it also observes eventual task faults. An in-process vendor adapter would still be unsafe because a synchronous library can block before returning a `Task`, ignore cancellation, retain thread-affine native state, or continue after the caller's timeout.

GatewayHost now delegates each potential send or reconciliation to a fresh child through authenticated, versioned IPC. The parent accepts only a dedicated local fixed-volume deployment directory, verifies an exhaustive SHA-pinned manifest, holds every declared file read-only for the child lifetime, applies one deadline budget before verification through IPC, enforces strict request/response limits, and discards child diagnostics. Synchronous filesystem calls and `Process.Start` cannot be preempted, so this is not a hard wall-clock bound. Forced cleanup is a best-effort `Kill(entireProcessTree: true)` request followed by a bounded wait on the root handle; root exit is not descendant proof and every forced-kill outcome uses the fixed `mt5_process_termination_unconfirmed` code. Any post-start ambiguity becomes `Unknown`. The registered child executor remains proof-only and never touches the vendor DLL.

This is not the isolated-runner gate. The same-host best-effort tree-kill request is not a Windows job-object, sandbox, privilege boundary, filesystem boundary, constrained network policy, container, or VM, and it does not defend against a privileged host actor or prove descendant exit. A mutation-capable deployment still needs an approved isolated image/runner, constrained egress, immutable deployment authority, and attested containment and restart behavior. The detailed contract is recorded in `src/Runtime/YO4X.Trading.ProcessIsolation/SECURITY.md`.

## Gates before any demo mutation

1. Written licence/provenance approval and a supported, publisher-authenticated vendor artifact.
2. A configured isolated Windows runner/adapter process with an approved image, network policy, IPC identity, and immutable attestation.
3. A production write-only secret provider. The local DPAPI vault is a maintenance boundary and is not wired to GatewayHost.
4. A trusted risk-authority factory that authenticates a signed gateway snapshot, derives broker-dependent risk values, binds durable exposure/risk-day/order-rate state, verifies signed policy, and atomically reserves exposure.
5. Authenticated broker-observation provenance that production SQL can verify independently before accepting any terminal reconciliation.
6. Complete strategy-specific dependency, grammar, type, lowering, semantic, compile, parity, account-mode, and safe-order evidence.
7. Broker capability, ownership, partial-fill, restart, soak, and emergency-containment evidence for the exact demo account and server.

Until all relevant gates are closed, no demo login, strategy run, order, modification, cancellation, close, or broker reconciliation is claimed.
