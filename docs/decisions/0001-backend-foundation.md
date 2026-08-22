# ADR 0001: Backend foundation

- Status: Accepted for U0/A0–A3
- Date: 2026-08-22

## Decision

Use .NET 10 and ASP.NET Core for a modular monolith backed by PostgreSQL 18. Modules publish application/domain ports and do not reference Npgsql, HTTP, cloud SDKs, vault clients, or the vendor gateway. Direct Npgsql adapters and explicit checksum-verified SQL migrations live in `YO4X.Persistence.Postgres`.

Keep these as independently deployable processes:

- Control Plane API
- Admin BFF
- Emergency Safety API
- Secret Ingestion API
- Control Plane Workers
- Conversion Worker (boundary only while conversion remains deferred)
- Supervisor
- StrategyHost
- GatewayHost

The runtime trio are actual OS processes/containers. StrategyHost has no credential, network, native-library, or trading-adapter dependency. Only `YO4X.Trading.Mt5` may reference `mt5api.dll`, and only GatewayHost may reference that adapter.

## Persistence rules

- Server-generated UUIDv7 identifiers.
- UTC `timestamptz`, checked state values, foreign keys, and indexed tenant joins.
- Application repository tenant filtering plus PostgreSQL `FORCE ROW LEVEL SECURITY`.
- Tenant and actor context set with `SET LOCAL` inside every transaction.
- Sensitive mutation, audit intent, and outbox message commit atomically.
- Outbox consumers claim work with `FOR UPDATE SKIP LOCKED` and are idempotent.
- No runtime role owns schemas, bypasses RLS, performs DDL, or receives a broad cross-tenant switch.

## Consequences

This foundation stays deployable as a monolith without collapsing credential or failure-isolation boundaries. Provider-specific message bus, vault/KMS, staff IdP, cloud fencing, and immutable archive adapters remain ports until those providers are selected.
