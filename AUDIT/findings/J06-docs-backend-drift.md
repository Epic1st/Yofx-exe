---
agent_id: J06
lane: backend-docs-drift
scope:
  - docs/backend/API_SECURITY_BUILD_SPEC.md
  - docs/backend/CLOSURE_LEDGER_2026-08-23.md
  - docs/backend/DURABLE_BROKER_COMMAND_PIPELINE.md
  - docs/backend/IMPLEMENTATION_STATUS.md
  - docs/backend/LOCAL_MT5_CREDENTIAL_BOUNDARY.md
  - docs/backend/MQ5_COMPATIBILITY_REPORT.md
  - docs/backend/MQL5_ISOLATED_COMPILE_ORCHESTRATION.md
  - docs/backend/MQL5_NONCANONICAL_INTAKE_REPORT.md
  - docs/backend/MQL5_SEMANTIC_EQUIVALENCE_EVIDENCE.md
  - docs/backend/MT5_VENDOR_ARTIFACT_U0.md
  - docs/backend/NUMERIC_RISK_ENGINE.md
  - docs/backend/POSTGRESQL_BASELINE_POLICY.md
  - docs/backend/POSTGRES_TENANT_CONTEXT_CAPABILITY.md
  - docs/backend/PROOF_KEY_ROTATION.md
  - docs/backend/STRATEGY_SOURCE_IMPORT.md
  - docs/backend/USER_OPERATION_INVOCATION_PROTOCOL.md
status: COMPLETE
generated: 2026-08-29T11:37:00Z
counts: { P0: 0, P1: 1, P2: 1, P3: 0 }
---

# J06 — backend-docs-drift

## Scope audited

The entire set of 16 backend design, status, protocol, and boundary specification documents was reviewed and cross-referenced against the actual C# and SQL implementation:

- `docs/backend/API_SECURITY_BUILD_SPEC.md` (680 lines)
- `docs/backend/CLOSURE_LEDGER_2026-08-23.md` (124 lines)
- `docs/backend/DURABLE_BROKER_COMMAND_PIPELINE.md` (103 lines)
- `docs/backend/IMPLEMENTATION_STATUS.md` (94 lines)
- `docs/backend/LOCAL_MT5_CREDENTIAL_BOUNDARY.md` (249 lines)
- `docs/backend/MQ5_COMPATIBILITY_REPORT.md` (313 lines)
- `docs/backend/MQL5_ISOLATED_COMPILE_ORCHESTRATION.md` (159 lines)
- `docs/backend/MQL5_NONCANONICAL_INTAKE_REPORT.md` (61 lines)
- `docs/backend/MQL5_SEMANTIC_EQUIVALENCE_EVIDENCE.md` (21 lines)
- `docs/backend/MT5_VENDOR_ARTIFACT_U0.md` (125 lines)
- `docs/backend/NUMERIC_RISK_ENGINE.md` (43 lines)
- `docs/backend/POSTGRESQL_BASELINE_POLICY.md` (95 lines)
- `docs/backend/POSTGRES_TENANT_CONTEXT_CAPABILITY.md` (132 lines)
- `docs/backend/PROOF_KEY_ROTATION.md` (92 lines)
- `docs/backend/STRATEGY_SOURCE_IMPORT.md` (59 lines)
- `docs/backend/USER_OPERATION_INVOCATION_PROTOCOL.md` (709 lines)

## Verdict

The backend documentation across `docs/backend/**` is exceptionally rigorous and largely aligns with the codebase: security invariants (write-only credential boundaries, fail-closed broker execution disabled by design, isolated compile orchestration, and two-phase proof-key rotation) are faithfully implemented in the C# and SQL codebases. Two substantive discrepancies were identified where documentation claims contradict the code or create operational failure modes: (1) `CLOSURE_LEDGER_2026-08-23.md` records stale/diverged SHA-256 baseline hashes for `least_privilege_roles.sql` and the PostgreSQL catalog semantic fingerprint (`ExpectedSha256`), conflicting with both `POSTGRESQL_BASELINE_POLICY.md` and the compiled production constants; and (2) `IMPLEMENTATION_STATUS.md` claims a 5-second deadline for the anonymous readiness single-flight probe, whereas `ControlPlaneReadinessProbe` requires a 10-second budget to survive multi-role cold-start catalog re-attestations.

## Findings

### [P1] Baseline catalog fingerprint and least-privilege role script SHA-256 drift in closure ledger
- **Where:** `docs/backend/CLOSURE_LEDGER_2026-08-23.md:29-30`
- **Confidence:** CONFIRMED
- **Code:**
  ```markdown
  | `least_privilege_roles.sql` | `e5c2dd5464c1d2bd3a58f0887a76caa3fc94f4a8fa1c2d747f9669316d5fff8c` |
  | Catalog semantic fingerprint (`PostgresCatalogSemanticFingerprint.ExpectedSha256`) | `3a0ff8ac15dcaa21234bb6d9f78818ab52af5a51a390cc02543c3a0be087d18e` |
  ```
  Contradicted by `src/BuildingBlocks/YO4X.Persistence.Postgres/PostgresRoleCapabilityFingerprint.cs:519-520`:
  ```csharp
  public const string ExpectedSha256 =
      "8772e5e7b8044ef68e185772d569128e771a11fb4b6f06dca7df1260b3822eba";
  ```
  `tests/YO4X.ControlPlane.Postgres.Tests/PostgresBaselinePolicyTests.cs:38-39`:
  ```csharp
  private const string ExpectedRoleScriptSha256 =
      "17de46699761981c7747be190d8b91f178ade24662ad25bfd2774b13a7bc8c1d";
  ```
  and `docs/backend/POSTGRESQL_BASELINE_POLICY.md:23,26`:
  ```markdown
  - `least_privilege_roles.sql`: `17de46699761981c7747be190d8b91f178ade24662ad25bfd2774b13a7bc8c1d`
  The expected catalog semantic fingerprint is
  `8772e5e7b8044ef68e185772d569128e771a11fb4b6f06dca7df1260b3822eba`.
  ```
- **Failure:** A deployment engineer, auditor, or automated verification harness verifying database baseline integrity against `CLOSURE_LEDGER_2026-08-23.md` will expect `3a0ff8ac...` for the catalog semantic fingerprint and `e5c2dd54...` for `least_privilege_roles.sql`. When matching against the real code and database, the actual verified fingerprint is `8772e5e7...` and the role script is `17de4669...`, causing release validation tooling or deployment verification scripts to fail closed or misdiagnose an authentic database as tampered/corrupt.
- **Fix:** Update `docs/backend/CLOSURE_LEDGER_2026-08-23.md` lines 29-30 to match the canonical baseline values `17de46699761981c7747be190d8b91f178ade24662ad25bfd2774b13a7bc8c1d` and `8772e5e7b8044ef68e185772d569128e771a11fb4b6f06dca7df1260b3822eba`.

---

### [P2] Anonymous readiness single-flight timeout mismatch between status doc and probe implementation
- **Where:** `docs/backend/IMPLEMENTATION_STATUS.md:17`
- **Confidence:** CONFIRMED
- **Code:**
  ```markdown
  - Anonymous startup/readiness endpoints use one fixed-key, process-local single-flight snapshot. Concurrent callers share one dependency probe, caller cancellation cannot cancel the shared probe, the probe has an independent five-second deadline, and a completed result is reused for at most one second.
  ```
  Contradicted by `src/Apps/YO4X.ControlPlane.Api/ControlPlaneReadinessProbe.cs:23,319`:
  ```csharp
  private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(10);
  ...
  using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
  timeout.CancelAfter(ProbeTimeout);
  ```
  and `src/BuildingBlocks/YO4X.Api/ApiFoundation.cs:32,108`:
  ```csharp
  public TimeSpan ProbeTimeout { get; set; } = TimeSpan.FromSeconds(5);
  ...
  var readinessProbe = new BoundedBooleanProbe(
      ready,
      options.SnapshotLifetime,
      options.ProbeTimeout);
  ```
- **Failure:** `ControlPlaneReadinessProbe` explicitly documents that a cold pass re-attesting 4 separate least-privilege logins across 12,000+ catalog rows takes ~3.8s and therefore configures a 10-second internal timeout. However, `ApiFoundation.MapYo4xHealthEndpoints` maps the readiness probe using `ApiHealthOptions.ProbeTimeout` (default 5s, which matches the claim in `IMPLEMENTATION_STATUS.md`). If a cold-start probe or database load pushes the readiness query between 5.0s and 10.0s, `BoundedBooleanProbe` cancels execution at 5 seconds and caches `unhealthy` for 1 second, causing Kestrel readiness to report HTTP 503 and triggering premature container restarts during cold start.
- **Fix:** Update `IMPLEMENTATION_STATUS.md` to reflect the 10-second deadline, and ensure `ApiHealthOptions.ProbeTimeout` in `YO4X.ControlPlane.Api` is configured to match the 10-second requirement of `ControlPlaneReadinessProbe`.

## Referrals

- `src/BuildingBlocks/YO4X.Api/ApiFoundation.cs:32` — `ApiHealthOptions.ProbeTimeout` defaults to 5 seconds, which cuts off `ControlPlaneReadinessProbe` (configured for 10 seconds) during cold database passes.

## Coverage gaps

- Automated CI validation verifying that `docs/backend/CLOSURE_LEDGER_2026-08-23.md`, `docs/backend/POSTGRESQL_BASELINE_POLICY.md`, `PostgresRoleCapabilityFingerprint.ExpectedSha256`, and `PostgresBaselinePolicyTests.ExpectedRoleScriptSha256` are strictly synchronized in a single invariant test.


--- agy: gemini-3.7-flash-high | effort=high | mode=plan (read-only) | 433.8s | 581911 tok | id=0e783475-7594-4e86-9e8a-bc6edb28bf52
