# YO4X Phase U0 Execution Plan

**Status:** Active architecture/technical-proof phase only  
**Release authority:** No V1A, live trading, local mode, or general MQ5 converter is approved  
**Architecture:** [User side](./USER_SIDE_ARCHITECTURE.md) and [Admin side](./ADMIN_SIDE_ARCHITECTURE.md)
## 1. U0 objective

Prove the smallest safe vertical path before building the full product:

```text
One manually reviewed strategy
  -> synchronous StrategyHost event
  -> atomic state/action commit
  -> Supervisor-derived risk decision
  -> durable normalized command
  -> isolated GatewayHost
  -> one approved MT5 demo broker
  -> broker reconciliation
```

U0 is a technical proof, not a customer release.

## 2. Current evidence

| Evidence | Current state | Decision |
|---|---|---|
| Vendor DLL is present | PASS | Exact artifact exists in workspace |
| Artifact hash recorded | PASS | Supplied DLL SHA-256: `EB238C958A4D9F80C8A3EEACA07636AE53BC5A78A093BC3FE63923FA50A309C6` |
| Managed assembly inspection | PASS | Managed assembly; unsigned vendor artifact; provenance requires approval |
| One demo authentication test | NOT PROVEN FOR SUPPLIED ARTIFACT | The supplied DLL was not loaded or connected by this implementation work |
| Repeat connection/restart/reconciliation | NOT PROVEN | One successful login is not a soak or recovery proof |
| Commercial local/cloud/SaaS rights | BLOCKED | Written vendor rights are not in the workspace |
| Full-production vs trial artifact | BLOCKED | Must be proven by vendor/licence evidence and network testing |
| Supplied MQ5/MQH corpus | STATIC INTAKE COMPLETE | 198 exact files inventoried and persisted; corpus SHA-256 `8052d74d395516aef01f221bf1a663b775ed02ccccbfa0476704d52112ee43b6` |
| Deterministic semantic strategy translation | BLOCKED | Static inventory is not semantic conversion; per-strategy mapping and reference evidence remain required |
| Three-process runtime proof | BUILDABLE BOUNDARIES; END-TO-END PROOF OPEN | Supervisor, StrategyHost, and GatewayHost are isolated executables; broker/demo soak evidence remains required |
| Crash/unknown-command reconciliation proof | NOT STARTED | Required before V1A |
| Formal first risk-policy values | BLOCKED | Requires broker/strategy/account assumptions |
| Minimum admin containment controls | FOUNDATION IMPLEMENTED | Database-authoritative admin sessions, grants, step-up checks, command/approval boundaries, least-privilege roles, and audit/outbox tests are green; production identity/provider and operational evidence remain open |

The previously shared demo password is not repeated in this document or logs. Rotate it before future collaborative testing.

## 3. Fixed U0 constraints

- Demo account only; no live account or real-money order.
- One broker/server and one exact gateway artifact.
- One dedicated hedging demo account.
- One manually reviewed strategy and one small signal path.
- Broker-hosted SL/TP mandatory for every test entry.
- No netting, virtual stops, manual/external trades, or multiple strategies.
- Cloud-style controlled runtime proof first.
- No public registration, billing, marketplace, or broad desktop application.
- No local live execution.
- No general MQ5 parser/converter/IR platform.
- No third-party precompiled strategy DLL.
- No credential in source, configuration, command line, log, test artifact, or support export.

## 4. Workstreams and gates

### G0 — Gateway commercial and artifact gate

Deliver:

- Written rights for cloud/SaaS use, local redistribution if later needed, account limits, updates, support, and security fixes.
- Vendor proof that the exact artifact is full production or replacement with the approved production artifact.
- Artifact identity, version, hash, signature status, SBOM, source/escrow decision, and licence evidence.
- Network capture proving expected broker/control destinations and no undisclosed credential relay.
- Supported .NET/OS/platform matrix and vendor update/rollback procedure.

Pass:

- Legal/business owner and technical/security reviewer approve the exact artifact and intended cloud model.

Fail/stop:

- Rights are missing, trial checks remain in the production path, credentials are relayed unexpectedly, or update support is unacceptable.

### G1 — One-broker demo gateway proof

Build only:

- Minimal `IMt5Gateway` contract.
- Isolated GatewayHost that alone loads the vendor DLL.
- Broker discovery/resolution for the one allowlisted server.
- Connect/disconnect, quotes, account, positions, orders, deals/history, error normalization, and reconciliation.
- Fake-gateway contract harness for deterministic tests.

Test:

- Valid and invalid password.
- Wrong server and investor/read-only password.
- Connect/disconnect loop.
- Network loss and reconnect.
- Restart with no orders.
- Market closed and symbol unavailable.
- Capability and account-mode report.
- Credential/log/support-bundle redaction.
- Unexpected network destination block.

Pass:

- Repeatable connection and reconciliation with no order placement and no secret leakage.

### R0 — Deterministic event contract

Specify and freeze version 1 of:

- Typed Initialize, NewTick, BarClosed, Timer, Execution, AccountChanged, and Stop events.
- Receipt ordering, event IDs, per-generation sequence, UTC/broker timestamps, and deduplication.
- Tick/timer coalescing and durable execution events.
- Synchronous StrategyHost Handle contract.
- Fixed snapshot contents and deterministic clock.
- State, instruction, CPU, memory, and wall-time budgets.
- OrderSend/CTrade unsupported immediate-result patterns.

Pass:

- The same event sequence, snapshot hashes, inputs, runtime version, and starting state produce byte-identical committed state/action hashes.

### R1 — Runtime isolation proof

Build the minimum three-process/container proof:

```text
Supervisor
  - lease/generation
  - event transaction
  - formal risk policy
  - durable journal/outbox

StrategyHost
  - reviewed source-built strategy
  - no credential
  - no raw gateway
  - no unrestricted network/filesystem/native/process access

GatewayHost
  - vendor DLL
  - temporary vault credential access
  - broker-only egress
  - normalized commands/events
```

Pass:

- Security tests prove StrategyHost cannot read credentials, load the vendor gateway, reach broker/control networks, create unrestricted child processes, or submit a raw broker command.
- Killing any one component does not release a duplicate or partial strategy action.

### S0 — Representative strategy intake

Available input and still-required owner evidence:

- The supplied directory contains 166 exact `.mq5` files and 32 exact `.mqh` files; their deterministic metadata inventory is complete.
- Required local or standard-library include resolution is recorded per file; missing/custom dependencies remain explicit findings.
- Source for required custom indicators.
- SET files.
- Symbols, timeframes, broker/account-mode assumptions, and expected behavior.
- DLL, WebRequest, file, global-variable, or external-data dependencies.
- Strategy Tester configuration/report/trace if available.
- Ownership/commercial-use confirmation.

Produce:

- Dependency graph.
- Inputs and SET mapping.
- Event/state/order behavior inventory.
- Unsupported/ambiguous constructs.
- Immediate-result-dependent OrderSend/CTrade findings.
- Indicator and history requirements.
- Hedging/netting/manual-position assumptions.
- One small manually translated signal path.

Pass:

- No unidentified dependency in the chosen path and no guessed semantic mapping.

### T0 — Atomic state/action transaction

Implement:

```text
Read event N and state V
  -> pin normalized snapshot
  -> evaluate StrategyHost
  -> buffer state V+1 and requested actions
  -> atomically commit:
       consumed event N
       state V+1
       requested actions
       execution outbox
  -> process persisted actions
```

Required failure tests:

- Crash before strategy evaluation.
- Crash during evaluation.
- Budget exhaustion after attempted state changes.
- Crash before commit.
- Crash immediately after commit.
- Duplicate event delivery.
- Outbox consumer restart.

Pass:

- Before-commit failures replay against V; after-commit failures continue persisted actions without rerunning event N; no partial state/action commit exists.

### T1 — Broker command and reconciliation proof

Implement:

```text
Persist BrokerCommand READY_TO_SEND
  -> commit
  -> GatewayHost send
  -> acknowledgement/result
  -> broker history/state reconciliation

Timeout/crash after send
  -> UNKNOWN
  -> block blind retry
  -> reconcile before state transition
```

Required failure tests:

- Failure before send.
- Failure during send.
- GatewayHost crash immediately after send.
- Lost acknowledgement.
- Duplicate/reordered broker event.
- Partial fill.
- Rejection and market closure.
- Supervisor restart with UNKNOWN command.

Pass:

- Every outcome is broker-reconciled or remains visibly UNKNOWN; no blind retry occurs.

### K0 — Ownership and cooperative fencing

Implement:

- Linearizable ownership record keyed by deployment/account.
- Generation, holder, issue/not-before/expiry, and acknowledged release.
- No generation G+1 while G is valid.
- Replacement starts reconciliation-only.
- Network/workload identity of an old cloud worker is removed where possible.
- UI wording for observed, unobserved, expired, and reconciled states.

Pass:

- Cloud replacement never creates new exposure before the old generation is invalid and broker state is reconciled.

Limitation:

- This proves official cloud-worker ownership. It does not make YO4X tokens broker-enforced against a modified future local client.

### P0 — Formal first risk policy

Freeze an immutable first policy containing:

- Risk-day timezone and boundary.
- Equity-based daily-loss formula and cash-flow adjustment.
- Durable adjusted equity high-water and drawdown formula.
- Account-wide and deployment volume/margin/position/order limits.
- Full pending-order reservation.
- Partial-fill residual behavior.
- Broker-hosted SL/TP and no-widen/no-remove rule.
- Spread, deviation, order-rate, and market-session limits.
- Numeric quote/account/position/order/symbol/conversion freshness.
- Dedicated-account unexpected-activity behavior.
- Effective exposure derivation for entry, reduce, close, reversal, pending modification, cancel/replace, protection, and partial fill.

Pass:

- Property/replay tests reproduce every decision from the stored policy/input hashes.

### O0 — Minimum operator controls

Before any allowlisted external demo user, provide audited internal commands to:

- Block new deployments.
- Block new exposure globally or by broker, region, strategy, gateway, user, or deployment.
- Move a deployment to close-only.
- Revoke a package, gateway assignment, worker generation, or execution lease.
- Inspect stale workers, unknown commands, position/order mismatches, and broker reconciliation.
- Quarantine a strategy/gateway/runtime artifact.
- Rotate keys and disable a compromised artifact.
- Contact affected users.

Pass:

- Impact preview, authorization, reason, idempotency, audit, propagation state, automatic expiry, and reconciliation are tested.

## 5. Required evidence package

Store immutable U0 evidence under controlled object storage and reference it from the architecture decision records:

```text
U0/
├── gateway/
│   ├── rights/
│   ├── artifact-manifest/
│   ├── sbom/
│   ├── network-behavior/
│   └── compatibility-tests/
├── strategy/
│   ├── intake-manifest/
│   ├── dependency-map/
│   ├── manual-translation/
│   └── reference-evidence/
├── runtime/
│   ├── event-semantics-v1/
│   ├── process-isolation/
│   ├── transaction-tests/
│   └── fencing-tests/
├── risk/
│   ├── policy-v1/
│   └── replay-property-tests/
├── broker/
│   ├── capability-profile/
│   ├── demo-tests/
│   └── reconciliation-tests/
└── operations/
    ├── containment-tests/
    ├── runbooks/
    └── U0-decision-record/
```

Never store plaintext broker credentials in this evidence package.

## 6. U0 execution order

1. **Gateway decision first:** Complete G0 enough to know whether the dependency path is commercially viable.
2. **Strategy intake:** Receive and inspect the representative MQ5 package.
3. **Freeze semantics:** Complete R0 and the chosen manual translation specification.
4. **Gateway/runtime skeleton:** Complete G1 and R1 without order placement.
5. **Transactions and reconciliation:** Complete T0/T1 with a fake gateway, then demo broker.
6. **Risk and ownership:** Complete K0/P0 with replay/property tests.
7. **Minimum operations:** Complete O0.
8. **Demo soak:** Run only the approved strategy/account/gateway combination.
9. **Independent review:** Record GO, CONDITIONAL GO, or NO-GO for V1A.

G0 and strategy intake can progress in parallel, but the wider product cannot begin while either is blocked.

## 7. U0 final decision

### GO to V1A

Allowed only when every U0 pass condition has immutable evidence, the gateway is commercially approved, and no open P0/P1 safety issue remains.

### CONDITIONAL GO

May extend U0 demo testing only. It does not authorize external demo users, public product work, live trading, or local execution.

### NO-GO

Stop or replace the gateway/strategy approach when rights, credential path, deterministic mapping, runtime isolation, risk semantics, or reconciliation cannot be proven.

## 8. Immediate blocker

The supplied MQL5 intake is now statically inventoried and transactionally persisted, but that does not establish semantic conversion, compiler success, strategy parity, or safe runtime behavior. The next external gates are written gateway licence/production-artifact evidence, a trusted isolated Windows runner, a production write-only secret provider, and strategy-specific reference expectations. Until those arrive, U0 can strengthen deterministic contracts and test harnesses but cannot truthfully execute the vendor path, complete strategy mapping, place a demo order, or approve V1A.

## 9. Review remediation map

| Review finding | Corrected location |
|---|---|
| Local host is untrusted | User architecture 4.4 and V1C rules |
| Interface is not security isolation | User architecture 6.2–6.5 and U0 R1 |
| Async/non-deterministic strategy semantics | User architecture 7.2–7.3 and U0 R0 |
| OrderSend immediate-result mismatch | User architecture OrderSend compatibility rule |
| Atomic event/state/intent boundary missing | User architecture 7.3, 16.6, 18.3 and U0 T0 |
| Fencing is cooperative | User architecture 4.1, 16.7, 18.3 and U0 K0 |
| Exposure class must be risk-derived | User architecture 9.1–9.2.1 and U0 P0 |
| User reference evidence is not authoritative | User architecture 8.8 and validation model |
| Original V1 was too large | User architecture 25–26 staged V1A–V1D |
| Control-plane durability/DR incomplete | User architecture 18.5 and U0 T0/T1 |
| Local/cloud credential APIs ambiguous | User architecture 12.2 and 14.5 |
| Risk formulas underspecified | User architecture 9.2.1 and U0 P0 |
| Operational controls cannot wait | Admin architecture 9–10/31 and U0 O0 |
