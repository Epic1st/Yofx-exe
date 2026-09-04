---
agent_id: G05
lane: runtime-contracts
scope:
  - src/Runtime/YO4X.Runtime.Contracts/**
status: COMPLETE
generated: 2026-08-29T11:27:36Z
counts: { P0: 0, P1: 0, P2: 0, P3: 0 }
---

# G05 — runtime-contracts

## Scope audited

The following 9 files in `src/Runtime/YO4X.Runtime.Contracts/**` were completely reviewed:

- `src/Runtime/YO4X.Runtime.Contracts/YO4X.Runtime.Contracts.csproj` (10 lines)
- `src/Runtime/YO4X.Runtime.Contracts/RuntimeProtocol.cs` (50 lines)
- `src/Runtime/YO4X.Runtime.Contracts/RuntimeHealthContracts.cs` (45 lines)
- `src/Runtime/YO4X.Runtime.Contracts/ExecutionLeaseContracts.cs` (247 lines)
- `src/Runtime/YO4X.Runtime.Contracts/UserOperationProtocolPrimitives.cs` (438 lines)
- `src/Runtime/YO4X.Runtime.Contracts/UserOperationDeliveryRequestedV4.cs` (310 lines)
- `src/Runtime/YO4X.Runtime.Contracts/UserOperationReconciliationRequestedV3.cs` (335 lines)
- `src/Runtime/YO4X.Runtime.Contracts/UserOperationResultV5.cs` (593 lines)
- `src/Runtime/YO4X.Runtime.Contracts/UserOperationTargetObservation.cs` (464 lines)

## Verdict

The `YO4X.Runtime.Contracts` codebase is sound and implements rigorous fail-closed serialization, strict schema versioning, and deterministic canonicalization. All contracts enforce explicit contract version constants, reject missing or unknown properties, prohibit non-canonical property ordering and whitespace via round-trip byte equality checks, validate microsecond UTC timestamp precision (`Ticks % 10 == 0`), and zero sensitive bearer token buffers upon decoding. Polymorphic dispatch is restricted to a closed hierarchy (`private protected` constructors) parameterized by an explicit discriminator with zero open-ended type-name deserialization.

## Findings

None.

The contracts were systematically evaluated across all focus areas:
1. **Field compatibility & unknown properties:** All `UserOperation*` envelopes (`DeliveryRequestedV4`, `ReconciliationRequestedV3`, `GatewayResultV5`, `ReconciliationResultV5`, `UserOperationTargetObservation`) validate incoming JSON against hardcoded property lists (`CanonicalProperties`) via `RequireExactProperties` for wire protocols and `RequireExactPropertySet` for database evidence. Any missing, extra, duplicate, or reordered fields fail closed.
2. **Round-trip integrity:** `ParseCanonical` methods enforce character-for-character round-trip verification (`RequireCanonicalRoundTrip`) between supplied wire JSON and canonical serialization, preventing deserializer ambiguity, parser divergence, or silent payload drift.
3. **Defaults and missing fields:** No defaulting is permitted; all identifiers, generations, versions, states, and capabilities are validated via `RequireIdentifier`, `RequireVersion`, `RequireCanonicalState`, and `RequireBearer`.
4. **Enum and ordinal handling:** Critical outcomes (`UserOperationObservationOutcome`) are serialized as explicit strings (`"succeeded"`, `"diverged"`) rather than ordinal integers. Lease action classes use explicit bitwise flags (`LeaseActionClass`).
5. **Polymorphic type safety:** `UserOperationTargetObservation` uses a `private protected` constructor to seal inheritance to `UserOperationBrokerTargetObservation` and `UserOperationDeploymentTargetObservation`, resolving subtypes strictly through the `targetType` discriminator without reflection or dynamic type loading.
6. **Numeric and timestamp precision:** All counters and sequence numbers use exact integer types (`long`/`int`); timestamps enforce ISO 8601 UTC microsecond formatting with fractional tick validation.

## Referrals

None.

## Coverage gaps

None.


--- agy: gemini-3.7-flash-high | effort=high | mode=plan (read-only) | 101.5s | 289699 tok | id=8c4dfa19-226e-4b93-b2da-07c51a65ca49
