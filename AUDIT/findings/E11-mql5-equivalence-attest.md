---
agent_id: E11
lane: Strategy Governance Semantic Equivalence and Attestation
scope:
  - src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5SemanticEquivalenceContracts.cs
  - src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5SemanticEquivalenceVerifier.cs
  - src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5RunnerAttestationVerifier.cs
status: COMPLETE
generated: 2026-08-29T11:28:00Z
counts: { P0: 0, P1: 2, P2: 2, P3: 1 }
---

# E11 — Strategy Governance Semantic Equivalence and Attestation

## Scope audited
- `src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5SemanticEquivalenceContracts.cs` (281 lines)
- `src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5SemanticEquivalenceVerifier.cs` (668 lines)
- `src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5RunnerAttestationVerifier.cs` (119 lines)

## Verdict
The cryptographic verification and fail-closed state transitions in this lane are implemented with high precision: request-to-attestation bindings are verified using constant-time hash comparisons, P-256 signatures are strictly validated against approved key lists, and inconclusive runs (timeouts, failures, unsupported statuses) fail closed to `Blocked` or `Failed`. However, the module fundamentally conflates finite sample differential trace testing with formal semantic equivalence proof, and the dual floating-point tolerance check suffers from mathematical flaws near zero that trigger false alarms.

## Findings

### [P1] Semantic Equivalence Verifier Claims Parity "Proven" on Finite Non-Adversarial Sample Traces
- **Where:** `src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5SemanticEquivalenceVerifier.cs:356`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
          return (Mql5SemanticParityState.Proven, "SEMANTIC_PARITY_PROVEN_BY_ATTESTED_TRACE_COMPARISON");
      }
  ```
- **Failure:** A transpiled MQL5-to-C# strategy containing serious divergence in unexercised trading branches (such as stop-loss/take-profit triggers, margin call liquidation, multi-currency data synchronization, or bar boundary rollover) is tested against a small or non-adversarial input trace (`InputEventCount` can be as low as 1 event per `ValidateRequest:384`). Because the sampled events happen to match, `Verify` returns `Mql5SemanticParityState.Proven` and sets `SemanticParityProven = true`. Downstream governance treats the strategy as formally proven equivalent, allowing diverge-prone trading logic into live execution.
- **Fix:** Rename the proven state and reason code to represent trace match rather than formal semantic equivalence (e.g., `Mql5SemanticParityState.TraceParityMatched`), and require minimum sample size and trace diversity thresholds before certification.

### [P1] Dual-Limit Numeric Tolerance Comparison Causes False Parity Rejections Near Zero
- **Where:** `src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5SemanticEquivalenceVerifier.cs:303-306`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
              toleranceExceeded |= item.MaximumAbsoluteError
                      > request.TolerancePolicy.MaximumAbsoluteError
                  || item.MaximumRelativeError
                      > request.TolerancePolicy.MaximumRelativeError;
  ```
- **Failure:** When comparing small numeric quantities near zero (such as floating-point indicator deltas `0.0` vs `0.00001` or minimal profit adjustments), the absolute error is `0.00001` (well below a typical `MaximumAbsoluteError` of `0.001`), but the relative error is `1.0` (100%). Because the verifier uses logical OR (`||`), `toleranceExceeded` evaluates to `true`, rejecting legitimate floating-point approximations as `SEMANTIC_TRACE_NUMERIC_TOLERANCE_EXCEEDED` unless relative tolerance is configured to `>= 1.0` (which eliminates relative error protection for large balances/volumes).
- **Fix:** Implement standard numerical closeness evaluation where an event is accepted if error is within absolute tolerance OR relative tolerance (`error <= MaximumAbsoluteError || error <= MaximumRelativeError * Math.Abs(referenceValue)`), rather than requiring simultaneous satisfaction of both thresholds for near-zero values.

### [P2] Inconsistent Evidence Check Conflates Non-Numeric Divergence with Evidence Corruption
- **Where:** `src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5SemanticEquivalenceVerifier.cs:307-315`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
              evidenceInconsistent |= item.NumericDivergenceCount == 0
                      && (item.MaximumAbsoluteError != 0
                          || item.MaximumRelativeError != 0
                          || !eventOutputsExact)
                  || item.NumericDivergenceCount > 0
                      && (eventOutputsExact
                          || item.MaximumAbsoluteError == 0
                              && item.MaximumRelativeError == 0);
  ```
- **Failure:** If an event has differing output payloads (`!eventOutputsExact`) caused by string, enum, or formatting differences rather than numeric drift, `item.NumericDivergenceCount == 0` is true, which inadvertently trips `item.NumericDivergenceCount == 0 && !eventOutputsExact` to true. If structural mismatch counters are not incremented by the runner, the verifier fails with `SEMANTIC_TRACE_DIVERGENCE_EVIDENCE_INVALID` (reporting runner evidence corruption) rather than reporting a payload mismatch.
- **Fix:** Restrict the `!eventOutputsExact` branch of `evidenceInconsistent` to only trigger when all non-numeric and structural mismatch counters are zero (`item.NonNumericMismatchCount == 0 && item.MissingReferenceFieldCount == 0 && item.MissingLoweredFieldCount == 0 && !eventOutputsExact`).

### [P2] Runner Public Keys Lack Lifecycle Validity Windows and Expiry Enforcement
- **Where:** `src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5RunnerAttestationVerifier.cs:28-35`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
              foreach ((string keyId, byte[] encodedKey) in subjectPublicKeys)
              {
                  if (!Mql5CompileValidation.IsSafeToken(keyId)
                      || encodedKey is null
                      || encodedKey.Length is < 64 or > 1024)
                  {
                      throw new ArgumentException("An isolated-runner trust key is invalid.", nameof(subjectPublicKeys));
                  }
  ```
- **Failure:** Isolated runner trust keys are registered as static SubjectPublicKeyInfo byte arrays without `NotBefore` / `NotAfter` expiration timestamps or revocation markers. If a runner signing key is decommissioned, rotated, or compromised, `EcdsaP256Mql5RunnerAttestationVerifier` will continue accepting signatures generated by that key for any request within the 15-minute sliding window until the host process configuration is replaced.
- **Fix:** Associate validity time intervals (`ValidFromUtc`, `ValidUntilUtc`) with runner public keys and verify that `descriptor.CompletedAtUtc` falls within the key's active validity window.

### [P3] Attestation Constructor Silently Truncates Oversized Signatures Instead of Rejecting Them
- **Where:** `src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5SemanticEquivalenceContracts.cs:231-237`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
          const int maximumRetainedSignatureBytes = 257;
          int retainedSignatureLength = Math.Min(
              signature?.Length ?? 0,
              maximumRetainedSignatureBytes);
          this.signature = signature is null
              ? []
              : signature.AsSpan(0, retainedSignatureLength).ToArray();
  ```
- **Failure:** Passing a signature exceeding 257 bytes silently truncates the retained byte array rather than throwing `ArgumentOutOfRangeException` (unlike `Mql5RunnerAttestation` in `Mql5CompileContracts.cs:246`). The resulting object holds a corrupted signature prefix whose digest fails `SignatureSha256` matching down the pipeline instead of failing fast at instantiation.
- **Fix:** Validate `signature.Length <= Mql5CompileValidation.MaximumAttestationSignatureBytes` (256 bytes) and throw `ArgumentOutOfRangeException` on oversized inputs.

## Referrals
- `src/BuildingBlocks/YO4X.BuildingBlocks/CanonicalJson.cs` — `CanonicalJson.Serialize` relies on default `decimal` formatting which preserves trailing zeroes (`1.0m` vs `1.00m`), creating potential digest divergence across heterogeneous runtimes.

## Coverage gaps
- `src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5SemanticEquivalenceVerifier.cs:307-315` — Untested path where `NonNumericMismatchCount == 0` but `ReferenceOutputEventSha256 != LoweredOutputEventSha256` and `NumericDivergenceCount == 0` causes `SEMANTIC_TRACE_DIVERGENCE_EVIDENCE_INVALID`.
- `src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5SemanticEquivalenceVerifier.cs:204-206` — Untested branch where run execution duration exceeds `WallClockTimeoutMilliseconds` causing `SEMANTIC_RUNNER_ATTESTATION_TIME_INVALID`.


--- agy: gemini-3.7-flash-high | effort=high | mode=plan (read-only) | 82.7s | 162929 tok | id=374f1591-6413-49b1-860b-4e65594f64b7
