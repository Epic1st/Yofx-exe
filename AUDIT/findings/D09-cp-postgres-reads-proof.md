---
agent_id: D09
lane: Control Plane Postgres & Proof Subsystems
scope:
  - src/Infrastructure/YO4X.ControlPlane.Postgres/ControlPlanePostgresOptions.cs
  - src/Infrastructure/YO4X.ControlPlane.Postgres/CredentialIngestionProofIssuer.cs
  - src/Infrastructure/YO4X.ControlPlane.Postgres/PolicySignatureTrustStore.cs
  - src/Infrastructure/YO4X.ControlPlane.Postgres/PostgresBrokerAccountDiscoveryReads.cs
  - src/Infrastructure/YO4X.ControlPlane.Postgres/PostgresControlPlaneApplication.cs
  - src/Infrastructure/YO4X.ControlPlane.Postgres/PostgresControlPlaneReads.cs
  - src/Infrastructure/YO4X.ControlPlane.Postgres/PostgresDeploymentPolicyEvaluation.cs
  - src/Infrastructure/YO4X.ControlPlane.Postgres/PostgresDeploymentValidation.cs
  - src/Infrastructure/YO4X.ControlPlane.Postgres/PostgresStrategyCompatibilityReads.cs
  - src/Infrastructure/YO4X.ControlPlane.Postgres/PostgresUserOperations.cs
  - src/Infrastructure/YO4X.ControlPlane.Postgres/ProofKeyRings.cs
  - src/Infrastructure/YO4X.ControlPlane.Postgres/StrategyImportProofIssuer.cs
status: COMPLETE
generated: 2026-08-29T11:25:30Z
counts: { P0: 0, P1: 0, P2: 1, P3: 0 }
---

# D09 — Control Plane Postgres & Proof Subsystems

## Scope audited
- `src/Infrastructure/YO4X.ControlPlane.Postgres/ControlPlanePostgresOptions.cs` (137 lines)
- `src/Infrastructure/YO4X.ControlPlane.Postgres/CredentialIngestionProofIssuer.cs` (139 lines)
- `src/Infrastructure/YO4X.ControlPlane.Postgres/PolicySignatureTrustStore.cs` (181 lines)
- `src/Infrastructure/YO4X.ControlPlane.Postgres/PostgresBrokerAccountDiscoveryReads.cs` (238 lines)
- `src/Infrastructure/YO4X.ControlPlane.Postgres/PostgresControlPlaneApplication.cs` (195 lines)
- `src/Infrastructure/YO4X.ControlPlane.Postgres/PostgresControlPlaneReads.cs` (491 lines)
- `src/Infrastructure/YO4X.ControlPlane.Postgres/PostgresDeploymentPolicyEvaluation.cs` (729 lines)
- `src/Infrastructure/YO4X.ControlPlane.Postgres/PostgresDeploymentValidation.cs` (439 lines)
- `src/Infrastructure/YO4X.ControlPlane.Postgres/PostgresStrategyCompatibilityReads.cs` (216 lines)
- `src/Infrastructure/YO4X.ControlPlane.Postgres/PostgresUserOperations.cs` (657 lines)
- `src/Infrastructure/YO4X.ControlPlane.Postgres/ProofKeyRings.cs` (272 lines)
- `src/Infrastructure/YO4X.ControlPlane.Postgres/StrategyImportProofIssuer.cs` (146 lines)

## Verdict
The control plane persistence, read authorization, and cryptographic proof subsystems are exceptionally well-engineered and rigorously fail-closed. Cryptographic key rings zero key material on disposal, mandate constant-time comparisons, and cleanly enforce retirement deadlines without leaking secret material to logs. Policy evaluation strictly denies execution when baseline or overlay policies fail signature verification or digest checks, and queries consistently enforce tenant and user boundaries with parameterized SQL. One minor robustness defect was identified where the strategy source corpora query lacks an explicit result-set limit.

## Findings

### [P2] Unbounded result set in strategy source corpora listing
- **Where:** `src/Infrastructure/YO4X.ControlPlane.Postgres/PostgresStrategyCompatibilityReads.cs:28-48`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
              await using NpgsqlCommand command = transaction.CreateCommand(
                  """
                  select
                      corpus.id,
                      corpus.source_label,
                      corpus.file_count,
                      corpus.total_bytes,
                      corpus.created_at,
                      count(source_file.id)
                  from governance.strategy_source_corpora as corpus
                  left join governance.strategy_source_files as source_file
                    on source_file.tenant_id = corpus.tenant_id
                   and source_file.corpus_id = corpus.id
                   and source_file.user_id = corpus.user_id
                  where corpus.tenant_id = @tenant_id
                    and corpus.user_id = @user_id
                    and corpus.state = 'static_analyzed'
                  group by corpus.id, corpus.source_label, corpus.file_count,
                           corpus.total_bytes, corpus.created_at
                  order by corpus.created_at desc, corpus.id desc
                  """);
  ```
- **Failure:** A user who imports numerous strategy corpora over time causes `GetStrategySourceCorporaAsync` to execute an unpaged, unbounded table scan and aggregation across all historical corpora and associated files for that tenant/user, streaming an arbitrarily large result set directly into application memory without a pagination or limit ceiling.
- **Fix:** Add a bounded `limit @limit` clause (or pagination parameters) matching the conventions applied across other control plane discovery endpoints.

## Referrals
None.

## Coverage gaps
- `src/Infrastructure/YO4X.ControlPlane.Postgres/ProofKeyRings.cs:113-117` — Unit tests should explicitly assert the boundary tick where `now >= retainUntil` transitions `TryComputeSha256` from true to false for retired secondary key slots.
- `src/Infrastructure/YO4X.ControlPlane.Postgres/PostgresDeploymentPolicyEvaluation.cs:144-150` — Integration tests should verify that corrupting an execution safety policy digest or signature in PostgreSQL immediately forces the effective lattice meet to `FullyRestrictedPolicy` and blocks deployment dispatch.


--- agy: gemini-3.7-flash-high | effort=high | mode=plan (read-only) | 64.3s | 170378 tok | id=232d084a-8986-45e5-9d2b-d4cebbb2a584
