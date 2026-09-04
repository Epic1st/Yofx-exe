---
agent_id: E18
lane: strategy-version
scope:
  - src/Modules/StrategyGovernance/YO4X.StrategyGovernance/StrategyVersion.cs
  - src/Modules/StrategyGovernance/YO4X.StrategyGovernance/AssemblyInfo.cs
status: COMPLETE
generated: 2026-08-29T08:40:00Z
counts: { P0: 0, P1: 0, P2: 2, P3: 1 }
---

# E18 — strategy-version

## Scope audited
- `src/Modules/StrategyGovernance/YO4X.StrategyGovernance/StrategyVersion.cs` (164 lines)
- `src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Properties/AssemblyInfo.cs` (4 lines, referenced in scope as `AssemblyInfo.cs`)

## Verdict
The `StrategyVersion` aggregate root cleanly encapsulates core domain invariants for version identity, transition safeguards (such as immutability of revoked states and requirement of manual review prior to demo eligibility), and SHA-256 digest validation. Version identity is modeled via an aggregate UUIDv7 identifier paired with an integer version counter rather than being computed directly from content digests, leaving de-duplication across identical digests to persistence unique constraints. Two robustness gaps were identified: `ApproveForDemo` preserves un-normalized digest casing and leaves `RuntimeVersion` unvalidated, and `RecordManualReview` retains stale `ValidationEvidence` across re-reviews.

## Findings

### [P2] ApproveForDemo skips digest lowercasing and leaves RuntimeVersion unvalidated
- **Where:** `src/Modules/StrategyGovernance/YO4X.StrategyGovernance/StrategyVersion.cs:121`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
  ValidateDigest(evidence.EvidenceDigest, nameof(evidence));
  ValidateDigest(evidence.DatasetDigest, nameof(evidence));
  if (evidence.TrustLabel == EvidenceTrustLabel.Unavailable)
  {
      throw new DomainException("STRATEGY_VALIDATION_EVIDENCE_REQUIRED", "Unavailable evidence cannot establish demo eligibility.");
  }

  ValidationEvidence = evidence;
  ```
- **Failure:** `CreateManualU0Candidate` and `RecordManualReview` normalize all digests to lowercase via `.ToLowerInvariant()`. In contrast, `ApproveForDemo` validates `evidence.EvidenceDigest` and `evidence.DatasetDigest` against `^[A-Fa-f0-9]{64}$` (which accepts uppercase hex) but assigns `evidence` directly without normalizing its digest fields to lowercase, and without validating `evidence.RuntimeVersion` for null or whitespace. If evidence generated with standard uppercase hex format (e.g. .NET `Convert.ToHexString`) is supplied, `ValidationEvidence.EvidenceDigest` retains uppercase characters, causing downstream case-sensitive comparisons and database check constraints (`check (package_sha256 ~ '^[0-9a-f]{64}$')`) to fail.
- **Fix:** Normalize `EvidenceDigest` and `DatasetDigest` to lowercase, validate that `evidence.RuntimeVersion` is non-empty, and store the normalized record in `ValidationEvidence`.

### [P2] RecordManualReview preserves stale ValidationEvidence on re-review of demo-eligible versions
- **Where:** `src/Modules/StrategyGovernance/YO4X.StrategyGovernance/StrategyVersion.cs:97`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
  public void RecordManualReview(string reviewEvidenceDigest, DateTimeOffset occurredAt)
  {
      EnsureNotRevoked();
      ValidateDigest(reviewEvidenceDigest, nameof(reviewEvidenceDigest));
      ReviewEvidenceDigest = reviewEvidenceDigest.ToLowerInvariant();
      State = StrategyVersionState.ManuallyReviewed;
      RecordChange(occurredAt);
  }
  ```
- **Failure:** When an active `DemoEligible` strategy version undergoes a manual re-review, `RecordManualReview` updates `ReviewEvidenceDigest` and sets `State` back to `ManuallyReviewed`. However, it does not clear `ValidationEvidence`. The aggregate remains in the `ManuallyReviewed` state with stale validation evidence from the previous review cycle still attached, presenting inconsistent evidence state to consumers and audit logs before new validation evidence is approved.
- **Fix:** Reset `ValidationEvidence = null;` inside `RecordManualReview` whenever a new manual review is recorded.

### [P3] ValidateDigest in ApproveForDemo reports parameter name as 'evidence' rather than specific digest property
- **Where:** `src/Modules/StrategyGovernance/YO4X.StrategyGovernance/StrategyVersion.cs:114`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
  ValidateDigest(evidence.EvidenceDigest, nameof(evidence));
  ValidateDigest(evidence.DatasetDigest, nameof(evidence));
  ```
- **Failure:** When `evidence.EvidenceDigest` or `evidence.DatasetDigest` is invalid, `ValidateDigest` throws `ArgumentException` with `paramName` set to `"evidence"`. Callers receiving the exception cannot determine whether `EvidenceDigest` or `DatasetDigest` failed validation.
- **Fix:** Pass `"evidence.EvidenceDigest"` and `"evidence.DatasetDigest"` (or `nameof(evidence.EvidenceDigest)`) as the parameter names to `ValidateDigest`.

## Referrals
- `src/Infrastructure/YO4X.ControlPlane.Postgres/PostgresDeploymentValidation.cs:285` — Validates state against string literal `"demo_approved"` whereas domain aggregate enum member is `StrategyVersionState.DemoEligible`.
- `src/BuildingBlocks/YO4X.Persistence.Postgres/Migrations/001_foundation.sql:1688` — Database check constraint allows 12 distinct state strings while domain enum `StrategyVersionState` defines only 5 lifecycle states.

## Coverage gaps
- `StrategyVersion.RecordManualReview` (`StrategyVersion.cs:97`): Untested branch where a `DemoEligible` version is re-reviewed, leaving previous `ValidationEvidence` attached while in `ManuallyReviewed` state.
- `StrategyVersion.ApproveForDemo` (`StrategyVersion.cs:106`): Untested branch handling uppercase hex strings in `StrategyValidationEvidence.EvidenceDigest` and `DatasetDigest`.
- `StrategyVersion.ApproveForDemo` (`StrategyVersion.cs:106`): Untested behavior when `StrategyValidationEvidence.RuntimeVersion` is empty or null.


--- agy: gemini-3.7-flash-high | effort=high | mode=plan (read-only) | 52.9s | 136518 tok | id=f7458608-67c7-43e3-9312-fb1037602fce
