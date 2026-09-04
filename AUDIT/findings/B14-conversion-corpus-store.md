---
agent_id: B14
lane: Conversion Worker & Corpus Persistence
scope:
  - src/Apps/YO4X.Conversion.Worker/ConversionInventoryCommand.cs
  - src/Apps/YO4X.Conversion.Worker/ConversionWorkerStatus.cs
  - src/Apps/YO4X.Conversion.Worker/Mql5ArtifactOutputGuard.cs
  - src/Apps/YO4X.Conversion.Worker/Mql5CorpusInventoryJob.cs
  - src/Apps/YO4X.Conversion.Worker/Mql5QuarantineIntakeCommand.cs
  - src/Apps/YO4X.Conversion.Worker/Mql5QuarantineIntakeEvidence.cs
  - src/Apps/YO4X.Conversion.Worker/PostgresMql5CorpusStore.cs
  - src/Apps/YO4X.Conversion.Worker/Program.cs
  - src/Apps/YO4X.Conversion.Worker/Properties/AssemblyInfo.cs
  - src/Apps/YO4X.Conversion.Worker/Properties/launchSettings.json
  - src/Apps/YO4X.Conversion.Worker/YO4X.Conversion.Worker.csproj
  - src/Apps/YO4X.Conversion.Worker/appsettings.Development.json
  - src/Apps/YO4X.Conversion.Worker/appsettings.json
status: COMPLETE
generated: 2026-08-29T11:27:00Z
counts: { P0: 0, P1: 0, P2: 0, P3: 0 }
---

# B14 — Conversion Worker & Corpus Persistence

## Scope audited
Opened and thoroughly audited all 13 files in scope (with `Mql5QuarantineIntakeJob.cs` excluded per brief):
- `src/Apps/YO4X.Conversion.Worker/ConversionInventoryCommand.cs` (404 lines)
- `src/Apps/YO4X.Conversion.Worker/ConversionWorkerStatus.cs` (43 lines)
- `src/Apps/YO4X.Conversion.Worker/Mql5ArtifactOutputGuard.cs` (296 lines)
- `src/Apps/YO4X.Conversion.Worker/Mql5CorpusInventoryJob.cs` (457 lines)
- `src/Apps/YO4X.Conversion.Worker/Mql5QuarantineIntakeCommand.cs` (111 lines)
- `src/Apps/YO4X.Conversion.Worker/Mql5QuarantineIntakeEvidence.cs` (331 lines)
- `src/Apps/YO4X.Conversion.Worker/PostgresMql5CorpusStore.cs` (1,044 lines)
- `src/Apps/YO4X.Conversion.Worker/Program.cs` (35 lines)
- `src/Apps/YO4X.Conversion.Worker/Properties/AssemblyInfo.cs` (4 lines)
- `src/Apps/YO4X.Conversion.Worker/Properties/launchSettings.json` (24 lines)
- `src/Apps/YO4X.Conversion.Worker/YO4X.Conversion.Worker.csproj` (21 lines)
- `src/Apps/YO4X.Conversion.Worker/appsettings.Development.json` (9 lines)
- `src/Apps/YO4X.Conversion.Worker/appsettings.json` (10 lines)

## Verdict
The audited corpus persistence and conversion worker pipeline is exceptionally sound and secure. Content hashing operates strictly over raw byte arrays with verification mirrored by PostgreSQL database-level check constraints (`pg_catalog.sha256`), preventing source transcoding or encoding corruption. Multi-row writes, capability authentication, persistence locks, classification evidence, and import job completion execute atomically within a single `TenantPostgresTransaction`, with full tenant isolation and zero SQL parameterisation flaws.

## Findings
None.

The persistence and worker subsystems meet all safety invariants:
1. **Encoding Round-Trip Fidelity & Hashing**: Source files are ingested and persisted directly as byte arrays (`bytea`) with SHA-256 hashes computed on the raw bytes. Database check constraints on `governance.strategy_source_corpora` and `governance.strategy_source_files` guarantee that stored manifests, reports, and source files match their SHA-256 digests and JSON representations.
2. **Transaction Scope & Atomicity**: All operations in `PersistCoreAsync`—from capability exchange via `control.acquire_strategy_import_job`, persistence lock acquisition, corpus insertion, iterative file insertion, classification persistence, to job completion—are bound to a single transaction (`TenantPostgresTransaction`). Rollback occurs cleanly on any failure without leaving partial state.
3. **Tenant & Authority Scoping**: Import authority is strictly derived from the database-verified reservation (`strategy_import_jobs`), rejecting self-asserted tenant/user parameters from CLI arguments. Tenant RLS context is activated and verified before data writes.
4. **Idempotency & Deduplication**: Re-imports for consumed jobs are validated against immutable evidence digests and replayed safely; distinct import jobs allocate unique `import_job_id` / `corpus_id` records while indexing on `(tenant_id, corpus_sha256)` for deduplication queries.
5. **Memory & Resource Safety**: Strict limits (4 MB per file, 256 MB per corpus, 10,000 files max) are enforced in C# and PostgreSQL, with sensitive buffers and capabilities zeroed via `CryptographicOperations.ZeroMemory`. Reparse points, ADS, DOS 8.3 aliases, and path traversal are rigorously rejected by `Mql5ArtifactOutputGuard`.

## Referrals
- `src/Apps/YO4X.Conversion.Worker/Mql5QuarantineIntakeJob.cs` — Excluded from B14 scope; verify archive decompression limits, CRC/SHA verification streaming, and file system safety when processing non-canonical ZIP archives.

## Coverage gaps
None. (Persistence snapshotting, secret scanning, tampering rejection, replaying, atomic transactions, and RLS guards are comprehensively covered in `YO4X.Worker.Tests.Mql5StaticInventoryTests` and `YO4X.Postgres.IntegrationTests.StrategyImportPostgresTests`.)


--- agy: gemini-3.7-flash-high | effort=high | mode=plan (read-only) | 113.9s | 247175 tok | id=7f43dcde-c2d2-4ac5-b0fb-e50d3af81dc3
