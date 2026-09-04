---
agent_id: G01
lane: mt5-adapter
scope:
  - src/Runtime/YO4X.Trading.Mt5/Mt5ProofOnlyBrokerWorkerExecutor.cs
  - src/Runtime/YO4X.Trading.Mt5/Mt5ProofOnlyGateway.cs
  - src/Runtime/YO4X.Trading.Mt5/Mt5VendorArtifact.cs
  - src/Runtime/YO4X.Trading.Mt5/Mt5VendorReadOnlyMapper.cs
  - src/Runtime/YO4X.Trading.Mt5/YO4X.Trading.Mt5.csproj
status: COMPLETE
generated: 2026-08-29T11:30:00Z
counts: { P0: 0, P1: 0, P2: 0, P3: 0 }
---

# G01 — MT5 Adapter & Proof-Only Boundary

## Scope audited

The following files within `src/Runtime/YO4X.Trading.Mt5/**` were reviewed completely:

- `src/Runtime/YO4X.Trading.Mt5/Mt5ProofOnlyBrokerWorkerExecutor.cs` (42 lines)
- `src/Runtime/YO4X.Trading.Mt5/Mt5ProofOnlyGateway.cs` (105 lines)
- `src/Runtime/YO4X.Trading.Mt5/Mt5VendorArtifact.cs` (15 lines)
- `src/Runtime/YO4X.Trading.Mt5/Mt5VendorReadOnlyMapper.cs` (132 lines)
- `src/Runtime/YO4X.Trading.Mt5/YO4X.Trading.Mt5.csproj` (48 lines)

## Verdict

`YO4X.Trading.Mt5` is sound, robust, and cleanly architected as a strict U0 proof-only boundary. All order submission and reconciliation mutation pathways fail closed with `GatewayCommandDisposition.SubmissionDisabled` and explicit proof-only status codes, ensuring zero real-money order leakage or unintended trade execution from the long-lived gateway host. The vendor read-only mapping layer (`Mt5VendorReadOnlyMapper`) strictly sanitizes incoming vendor data, enforcing bid/ask positivity, spread sanity (`ask >= bid`), login masking, and invariant decimal conversion while deliberately rejecting unproven vendor server timezone inference.

## Findings

None.

The audited scope implements a deliberate compile-time barrier and proof-only execution surface (`Mt5ProofOnlyGateway`, `Mt5ProofOnlyBrokerWorkerExecutor`). The focus areas hold up under inspection:
- **Order Request Field Mapping & Dropped Fields:** Order submission is intentionally disabled in this assembly (`GatewayCommandDisposition.SubmissionDisabled`). No live broker order translation takes place here; execution is gated by separate worker process isolation architectures.
- **Volume & Price Normalisation:** In-memory quote ingestion in `Mt5VendorReadOnlyMapper.MapQuote` verifies decimal conversion, non-zero pricing, and positive spreads (`ask >= bid`).
- **Error / Retcode Translation:** Operations return explicit fail-closed results (`IsSuccess = false`, `Code = "mt5_gateway_u0_proof_only"`) and empty immutable collections.
- **Timeout & Idempotency Safety:** Mutation attempts are rejected pre-invocation (`PreInvocationNotSentProven = true`), preventing order duplication or orphaned in-flight state.
- **Vendor Assembly Isolation:** `YO4X.Trading.Mt5.csproj` pins the exact SHA-256 digest of `mt5api.dll` with `<Private>false</Private>`, preventing redistribution of vendor binaries in runtime output directories.

## Referrals

None.

## Coverage gaps

- `src/Runtime/YO4X.Trading.Mt5/Mt5ProofOnlyBrokerWorkerExecutor.cs:12-41`: Untested execution paths for `SendAsync` and `ReconcileAsync` confirming worker-side `SubmissionDisabled` disposition and `PreInvocationNotSentProven = true`.
- `src/Runtime/YO4X.Trading.Mt5/Mt5VendorReadOnlyMapper.cs:51-56`: Untested validation rejection branches when `Quote.Bid <= 0`, `Quote.Ask <= 0`, or inverted spread `Quote.Ask < Quote.Bid` throws `InvalidDataException`.
- `src/Runtime/YO4X.Trading.Mt5/Mt5VendorReadOnlyMapper.cs:67-86`: Untested mapping fallback branch where unrecognized `AccountMethod` strings return `BrokerAccountMode.Unknown`.
- `src/Runtime/YO4X.Trading.Mt5/Mt5VendorReadOnlyMapper.cs:88-98`: Untested login masking branch when account login length is less than or equal to 4 characters.
- `src/Runtime/YO4X.Trading.Mt5/Mt5ProofOnlyGateway.cs:66`: Untested deal query validation branch where `fromUtc > toUtc` throws `ArgumentOutOfRangeException`.


--- agy: gemini-3.7-flash-high | effort=high | mode=plan (read-only) | 92.7s | 203896 tok | id=a2f4d9c0-1404-4a99-a134-fd14fae38143
