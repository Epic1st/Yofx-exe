# YO4X Closure Ledger — 2026-08-23

Evidence-backed completion/blocker record for the U0 security-hardening and
verification cycle continued from the prior agent session. Verification-run results
below were finalized on this tree and host on 2026-08-24. Documentation and checked-in
deterministic evidence were reconciled without executing MetaEditor, MetaTrader,
supplied MQL, the vendor DLL, or a broker mutation.

## Local non-execution verification gates — ALL GREEN

| Gate | Result | Evidence |
|---|---|---|
| Full solution Release build | 0 warnings, 0 errors | `dotnet build YO4X.sln -c Release`, final tree |
| .NET unit suites (13 projects) | 1,102 passed / 0 failed / 0 skipped | sequential `dotnet test -c Release --no-restore -m:1 -nodeReuse:false` sweep |
| `YO4X.Domain.Tests` (included above) | 159 passed / 0 failed | domain suite includes MQL package-planning and semantic-equivalence proof boundaries |
| PostgreSQL integration (fresh database per run) | 106 passed / 0 failed / 0 skipped in 11m38s | `scripts/Test-PostgresIntegration.ps1` (portable PG 18.6, pinned sha256, ephemeral creds) |
| Frontend typecheck + tests | tsc clean; 89/89 vitest across 6 files | `npm run typecheck && npm run test:run` (Node v24.19.0) |
| Frontend production build | success | `npm run build` |
| Secret scan | CLEAN: 0 real findings; 6 false positives classified | gitleaks v8.30.1 `--no-git --redact` over worktree (allowlist limited to non-repo bulk dirs); FP set = correlation-id HTTP key constant, two idempotency-key UUID fixtures, three SQL role-name lists |
| Dependency audit | NuGet: 0 advisories (`NuGetAudit=true NuGetAuditMode=all`); npm prod: 0 vulnerabilities | restore + `npm audit --omit=dev` |
| MQL artifact reconciliation | complete, zero orphans | 198 canonical security-sanitized current files (166 programs + 32 headers), exactly 12,979,438 bytes, corpus sha256 `9a53e844cfd3ffe5dfcf28544bb4909ce69741ac6a373e80b139f8227779dd47`; all headers classified (17 awaiting type-check, 1 missing dependency, 13 unsupported semantics, 1 external snapshot); 12 intrinsic `.mq5` candidates; 15 non-canonical files quarantined/classified; a fresh non-executing regeneration reproduced all 7 static/conversion/plan/quarantine outputs byte-for-byte |

## Baseline pins refreshed this cycle

| Artifact | New value |
|---|---|
| `001_foundation.sql` | `1de1cad6257edbd1a2c9eacd969171222b950d38b8cfa2f09ea5525506279db6` |
| `002_user_operation_invocation_protocol.sql` | `827598ac1aa9924ca1cfe9df383599d608148a44ac4cc6989a78af38ca35a934` |
| `least_privilege_roles.sql` | `e5c2dd5464c1d2bd3a58f0887a76caa3fc94f4a8fa1c2d747f9669316d5fff8c` |
| Catalog semantic fingerprint (`PostgresCatalogSemanticFingerprint.ExpectedSha256`) | `3a0ff8ac15dcaa21234bb6d9f78818ab52af5a51a390cc02543c3a0be087d18e` |
| `mq5-compile-package-plan.v2.json` (planSha256 / file sha256 / bytes) | `30ceaabef530b6e43522608658db718d466ba52cc5851ff6430f30d21116c80e` / `51e88beddabc6e2d11f00a6b8a2671a27642f58f2d302453f16199da368569e7` / 455,612 |

Re-pins were forced by reviewed semantic changes: a one-way
`ambiguous -> observed` terminal-reason refinement exemption in the invocation-attempt
guard (aligning the guard with the recorder's own designed behavior); exact fixed-
worker-actor and operation-correlation enforcement before reconciliation locks or
evidence branches; and the minimum-viable-structure planner gate (FIB 2.mq5 stub now
`blockedInvalidSyntax`; intrinsic candidates 13 -> 12).

## Security posture changes landed

- Fail-closed Postgres composition verified end-to-end: every runtime connection
  composes through `PostgresRuntimeConnectionPolicy`; zero production bypass paths;
  loopback escape robust (trim, bracket-strip, strict parse).
- Fence redesign ratified by tests: direct EXECUTE on legacy recorders revoked;
  recording flows through `control.record_user_operation_result_v5` capability path;
  no grant broadened beyond pre-revocation baseline.
- Worker readiness transport probe honors the documented dev-loopback posture only
  (`host(inet_client_addr())` TLS-or-loopback); non-loopback plaintext still fails closed.
- Work-store snapshot SQL dropped PG18 `FOR SHARE OF` clauses that required UPDATE
  privilege the worker must never hold; serialization remains on
  `control.acquire_u0_authority_lock()` plus server-side claim revalidation.
- Requested-v4/result-v5 and reconciliation-challenge database/C# scaffolding,
  including production `PostgresUserOperationWorkStore` scheduling, is implemented
  and PG18-tested with a deterministic non-broker provider. This is not real broker
  transport or reconciliation evidence.
- The reconciliation `SECURITY DEFINER` boundary validates the fixed worker actor
  before inputs and binds correlation in a non-locking preflight plus the locked
  lookup. Eight-branch negative facts prove wrong actor/correlation cannot reveal
  evidence, mutate state, or retain the operation row lock.
- Hygiene: credential-bearing vendor `Examples.cs` deletion staged (cannot ship);
  `.gitattributes` gains `*.mq4 -text`; stray duplicate of release evidence removed
  from `.tmp` after hash-identity proof.

## Open blockers (repository implementation and external actions required)

This list mirrors `openBlockers` in
`artifacts/verification/completion-blocker-ledger.v1.json` by stable identifier.

1. **`OUTBOX-TRANSPORT` — BLOCKED**: authenticated outbox consumption,
   Supervisor/Gateway/credential-boundary transport and return channels, the
   production secret provider and provider/observer invocation, and restart
   coordination are not wired. Database/C# attempt and challenge scaffolding exists,
   but no broker-account/deployment operation can reach a real executor.
2. **`TRUSTED-RISK-AUTHORITY` — BLOCKED**: no trusted component authenticates
   immutable broker observations, derives broker-dependent exposure/risk inputs,
   binds current policy and rate/risk-day state, and authorizes production broker
   commands.
3. **`AUTHENTICATED-RECONCILIATION-PROOF` — BLOCKED**: the production work store
   schedules ambiguity and reconciliation challenges, but authenticated
   transport/provider/restart wiring has not proven an unknown outcome against a real
   broker. Unknown remains fail-closed and no blind retry is authorized.
4. **`V2-ENVELOPE-DEADLINES` — BLOCKED**: the legacy v2 mutation envelope does not
   carry its database-owned capability expiry, assignment lease, fence, or a bounded
   execution deadline. An execution-enabled receiver must use the requested-v4
   claim/attempt boundary and must never consume stale v2 delivery.
5. **`MT5-PROVENANCE` — BLOCKED**: the exact vendor `mt5api.dll` is unsigned and no
   licence or notice material grants commercial/local/cloud/SaaS use. Written vendor
   rights evidence is required.
6. **`GIT-HISTORY-CREDENTIALS` — BLOCKED**: reachable history retains
   `Testing/Mq5.zip` in three commits, the credential-bearing vendor `Examples.cs` in
   two commits, and one historical Telegram token (redacted in current evidence).
   Affected credentials require revocation/rotation plus coordinated remote history,
   cache, and fork remediation; no secret values are reproduced here.
7. **`MT5-ISOLATION` — BLOCKED**: this host has no approved container/VM/sandbox
   capability (Hyper-V/Containers/Sandbox disabled, docker/podman/VBoxManage absent,
   0 WSL distributions); `SafeToCompileUntrustedMqlOnHost=false`. An externally
   supervised, network-denied Windows runner with an immutable image is required.
8. **`PLATFORM-SNAPSHOT` — BLOCKED**: an approved immutable platform-library snapshot
   and backend-owned compile profile (runner image digest, MetaEditor hash, and signing
   key) do not exist; 0/166 targets are dispatch-ready and all record
   `approvedPlatformSnapshotUnavailable`.
9. **`MT5-PARITY` — BLOCKED**: grammar/type-check/lowering/conversion/compiler,
   reference-parity, and runtime proof counts are all zero; no strategy may be
   represented as converted or runnable until the complete evidence chain exists.
10. **`MT5-DEMO-CREDENTIAL-REMEDIATION` — BLOCKED**: the plaintext demo credential
    file remains host-local, untracked, and ignored. It was not read by this audit;
    rotation/removal is not evidenced, and the production secret provider remains
    unwired even though local DPAPI import evidence exists.
11. **`MQL-ALL-NUL-RECOVERY` — BLOCKED**: canonical
    `Simple_Classic_Trailing.mq5` bytes are all-NUL while a quarantined source-like
    candidate exists. Restoring different bytes requires an owner provenance decision
    followed by a complete inventory, dependency, plan, and persistence re-run.

Dependency scanning is green for this verification pass (NuGet and npm both report
zero current advisories); currency remains a continuous maintenance obligation rather
than an open closure blocker.

## MT5 execution verdict

RED — execution remains unauthorized. Repository transport/provider/restart and
trusted-risk-authority work, authenticated broker reconciliation evidence, and the
external provenance/isolation/compiler/parity/demo gates above all remain open.
