# Backend implementation status

## Active scope

The repository implements the buildable U0/A0-A3 safety foundation. Broader target architecture remains staged and is not silently represented as complete.

## Implemented foundation

- .NET 10 solution and module dependency boundaries.
- Strict API problem/correlation/idempotency/concurrency primitives.
- User and admin session domain foundations.
- Credential-ingestion grants and write-only secret material boundary.
- Exact-role credential reservation, release, completion, rotation, expiry, and recovery capabilities. Secret-ingestion and worker roles have execute-only capabilities and no raw broker-account or credential-grant mutation authority.
- Deployment, gateway, strategy, incident, support, and privacy domain foundations.
- Policy-vector, command, approval, runtime, tenancy, audit, outbox, and PostgreSQL workstreams with dedicated tests.
- Independent executable boundaries and health contracts.
- PostgreSQL adapters for user control-plane and admin workflows, with transactional idempotency, audit, and outbox boundaries.
- PostgreSQL-owned authority clocks for control, admin, runtime-control, idempotency, worker claims, freshness, lifecycle transitions, and their audit/outbox evidence. Injected application clocks are not trusted for those decisions.
- MFA-gated, one-time strategy-source import with digest-only capability persistence, immutable raw-source/static-evidence storage, database-derived authority, and no strategy promotion rights.
- Deterministic signed numeric risk-policy evaluation for the demo-only domain slice; runtime dispatch remains blocked until its documented provenance/persistence gates exist.
- Fail-closed isolated MQL5 compile orchestration with signed attestation validation; no local compiler or terminal execution.
- Reference-driven read-only operational frontend with typed API contracts and production fail-closed data loading.
- Architecture tests that pin the supplied vendor hash and enforce dependency/output isolation.

## Verified local evidence

- Deterministic non-executing inventory of all 198 exact supplied `.mq5`/`.mqh` files: 166 programs, 32 headers, and 13,100,995 bytes under corpus SHA-256 `8052d74d395516aef01f221bf1a663b775ed02ccccbfa0476704d52112ee43b6`.
- Per-file path, byte length, SHA-256, include inventory, feature counts, and findings are recorded in the machine-readable manifest. No source body or credential is written to the human-readable report.
- Static dispositions are 75 `NeedsSemanticValidation`, 1 `NeedsSource`, and 122 `Unsupported`; semantic conversion, MetaEditor compilation, MT5 parity, and demo runtime proof counts remain zero.
- The complete corpus was transactionally persisted through the production PostgreSQL adapter into a fresh PostgreSQL 18.6 instance with 198 source rows and exactly one corpus, audit event, and outbox event.
- Concurrent exact imports were tested: one transaction commits the corpus and its competitor receives the committed replay, without duplicate audit/outbox evidence.
- A fresh exact-role PostgreSQL 18.6 run passed 18/18 integration tests with zero skips or failures. It installed the production migration and least-privilege roles, exercised all five credential scenarios, worker cleanup/replay, broker delete/rotation projection, RLS, outbox concurrency, import concurrency, and the complete 198-file corpus. The disposable cluster was removed after the run.
- The Release solution build completed with zero warnings and zero errors. The repository contains 385 unique .NET test cases; 368 pass in the ordinary solution run while the 17 database-dependent cases skip without an integration connection, and all 18 tests in that integration project pass under the portable PostgreSQL harness. This proves all 385 cases in their required environments.
- Frontend verification covers 13 unit/component tests, a clean TypeScript build, a clean production build, zero reported npm audit findings, and desktop/mobile browser QA without console errors or horizontal overflow.
- The NuGet transitive vulnerability audit and full npm vulnerability audit both reported zero known vulnerabilities on 2026-08-22 UTC.
- Numeric-risk focused tests and isolated-compiler-attestation tests execute without loading MetaEditor, an MT5 terminal, supplied MQL, or the vendor DLL on the host.
- Secret-boundary tests prove the 4 KiB transport limit, chunked-body enforcement, proof/body buffer clearing, provider-specific opaque URI schemes, bounded provider receipts, signed-receipt verification, and redacted database/audit/outbox handling.

## Intentionally disabled or deferred

- Live trading and raw order endpoints.
- Public signup and general user onboarding.
- Local runtime mode.
- General semantic MQ5 conversion, private-source viewing, and any compile/runtime claim without isolated attestation.
- Billing and broad privacy automation.
- Production provider integrations until providers and policies are selected.

## Hard blockers before demo MT5 execution

- No trusted isolated Windows runner and no approved platform snapshot/signing key are available on this machine.
- Written commercial/provenance approval for the exact unsigned vendor DLL is not present.
- A production write-only secret provider and receipt-verification implementation have not been selected or configured.
- Durable runtime risk inputs, exposure reservation, atomic decision/command persistence, and broker reconciliation are not yet wired end to end.
- Strategy-specific semantic mappings, compiler attestations, reference parity, account-mode assumptions, and safe order behavior have not been proven.

The demo credential file is never copied into source, configuration, logs, test output, reports, or browser code. Backend broker access remains disabled until every applicable gate is satisfied.

The current MT5 adapter is deliberately proof-only and returns `SubmissionDisabled`; the supplied DLL is hash-pinned as a compile-time inspection reference, is excluded from application output, and was not loaded during verification. Consequently, no demo login, order, modification, or broker reconciliation is claimed in this status.

See [`PHASE_U0_EXECUTION_PLAN.md`](../PHASE_U0_EXECUTION_PLAN.md) for external evidence gates that code cannot satisfy.
