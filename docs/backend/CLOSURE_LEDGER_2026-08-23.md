# YO4X Closure Ledger — 2026-08-23

Evidence-backed completion/blocker record for the U0 security-hardening and
verification cycle continued from the prior agent session. Every number below was
produced on this tree, this host, on 2026-08-23.

## Hard verification gates — ALL GREEN

| Gate | Result | Evidence |
|---|---|---|
| Full solution Release build | 0 warnings, 0 errors | `dotnet build YO4X.sln -c Release`, final tree |
| .NET unit suites (13 projects) | 1,101 passed / 0 failed | sequential `dotnet test -c Release --no-build` sweep |
| PostgreSQL integration (fresh database per run) | 100 passed / 0 failed | `scripts/Test-PostgresIntegration.ps1` (portable PG 18.6, pinned sha256, ephemeral creds) |
| Frontend typecheck + tests | tsc clean; 89/89 vitest | `npm run typecheck && npm run test:run` (Node v24.19.0) |
| Frontend production build | success | `npm run build` |
| Secret scan | CLEAN: 0 real findings; 6 false positives classified | gitleaks v8.30.1 `--no-git --redact` over worktree (allowlist limited to non-repo bulk dirs); FP set = correlation-id HTTP key constant, two idempotency-key UUID fixtures, three SQL role-name lists |
| Dependency audit | NuGet: 0 advisories (`NuGetAudit=true NuGetAuditMode=all`); npm prod: 0 vulnerabilities | restore + `npm audit --omit=dev` |
| MQL artifact reconciliation | complete, zero orphans | 198 canonical files (166 programs + 32 headers) = static manifest set; corpus sha256 `9a53e844…779dd47`; 15 non-canonical quarantined/classified in `mql5-quarantine-intake.v2.json`; compile-plan regenerated deterministically after planner hardening |

## Baseline pins refreshed this cycle

| Artifact | New value |
|---|---|
| `002_user_operation_invocation_protocol.sql` | `0cdf77558e519e9a1eedd3813d5c92a3d2d67b775a3b7d5829154c0ccb914f74` |
| Catalog semantic fingerprint (`PostgresCatalogSemanticFingerprint.ExpectedSha256`) | `216b6f8464d4842d8e6b3af5136e78353e674745228429a27aa18c4f5a1dbfe1` |
| `mq5-compile-package-plan.v2.json` (planSha256 / file sha256 / bytes) | `30ceaabef530b6e43522608658db718d466ba52cc5851ff6430f30d21116c80e` / `51e88beddabc6e2d11f00a6b8a2671a27642f58f2d302453f16199da368569e7` / 455,612 |

Re-pins were forced by reviewed semantic changes: a one-way
`ambiguous -> observed` terminal-reason refinement exemption in the invocation-attempt
guard (aligning the guard with the recorder's own designed behavior), and the
minimum-viable-structure planner gate (FIB 2.mq5 stub now `blockedInvalidSyntax`;
intrinsic candidates 13 -> 12).

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
- Hygiene: credential-bearing vendor `Examples.cs` deletion staged (cannot ship);
  `.gitattributes` gains `*.mq4 -text`; stray duplicate of release evidence removed
  from `.tmp` after hash-identity proof.

## Blockers (external decisions/actions required)

1. **MT5 provenance — BLOCKED**: vendor `mt5api.dll` unsigned, no licence/notice
   material; requires written vendor rights evidence. Historical credential-bearing
   blob recoverable from Git history until history rewrite.
2. **MT5 isolation — BLOCKED**: host has no container/VM/sandbox capability
   (Hyper-V/Containers/Sandbox disabled, docker/podman/VBoxManage absent, 0 WSL
   distros); `SafeToCompileUntrustedMqlOnHost=false`. Requires externally supervised,
   network-denied Windows runner with immutable image.
3. **MT5 compiler — BLOCKED**: approved immutable platform-library snapshot and
   backend-owned compile profile (runner image digest + MetaEditor hash + signing key)
   do not exist; 0/166 targets dispatch-ready (`approvedPlatformSnapshotUnavailable`).
4. **MT5 parity — BLOCKED**: 0 grammar/type-check/lowering/conversion/parity proofs;
   entire upstream chain must land first.
5. **MT5 demo-account — BLOCKED**: demo credentials shared in plaintext historically;
   rotation advised but not evidenced [INFERENCE: treat as compromised]; plaintext txt
   still on host disk (untracked, gitignored); production secret provider unwired.
6. **CVE currency** for .NET 10.0.x servicing packages and frontend deps needs one
   network-enabled audit pass; offline facts show no known-advisory exposure.
7. **Simple_Classic_Trailing.mq5 recovery**: canonical bytes are all-NUL while a
   quarantined source-like text candidate exists; needs owner decision to restore and
   re-run inventory/plan linkage.

## MT5 execution verdict

RED — execution not authorized and not possible on this host until blockers 1–5
resolve in order: provenance → isolation → compiler → parity → demo soak.
