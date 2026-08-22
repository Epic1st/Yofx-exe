# ADR 0002: U0 safety defaults

- Status: Accepted for implementation; external release gates remain open
- Date: 2026-08-22

## Decision

U0 is restricted to one allowlisted broker/server, one exact approved gateway digest, one region, one manually reviewed strategy package, and one dedicated hedging demo account with broker-hosted stop-loss and take-profit protection.

The backend fails closed when any required provider, policy, evidence, or reconciliation state is absent. It never substitutes mock business data, returns an early success for asynchronous propagation, retries an unknown broker send, automatically releases containment on expiry, or issues generation G+1 while G remains valid.

## External blockers

The following cannot be resolved in code and remain release blockers:

- Written gateway cloud/SaaS/redistribution rights and confirmation that the exact artifact is production-approved.
- The representative MQ5/MQH/indicator/SET intake bundle and an approved manual translation.
- Broker capability evidence and numeric risk/freshness policy values.
- Cloud, PostgreSQL, message transport, vault/KMS, immutable archive, and staff identity providers.
- Legal retention, privacy jurisdiction, residency, and source-viewing controls.

Until those are approved, GatewayHost is proof-only and must not place an order.
