# PostgreSQL baseline and compatibility policy

Status date: 2026-08-23 UTC

This repository is explicitly pre-release and greenfield. No durable deployed
YO4X database or legacy proof-key inventory is available. The current
`001_foundation.sql` is therefore the first supported database baseline, not an
in-place upgrade from an earlier working-tree version.

The canonical SQL release inputs are UTF-8 with LF line endings. Their SHA-256
digests are:

- `001_foundation.sql`: `1de1cad6257edbd1a2c9eacd969171222b950d38b8cfa2f09ea5525506279db6`
- `002_user_operation_invocation_protocol.sql`: `0cdf77558e519e9a1eedd3813d5c92a3d2d67b775a3b7d5829154c0ccb914f74`
- `least_privilege_roles.sql`: `292286093807f76a4a09bf7535736cdb4d006c6e9ae6accf12cd389d07eefa35`

`.gitattributes` pins every SQL file to LF so a clean checkout cannot change an
embedded migration checksum. Once this baseline is released or applied to a
durable environment, `001_foundation.sql` is immutable. Every later schema
change must use a new, ordered additive migration with an explicit data,
compatibility, privilege, and rollback/forward-fix review.

The catalog semantic baseline is currently proven on PostgreSQL 18 using the
Windows EDB 18.6-1 test runtime. The pinned Testcontainers fallback is
`postgres:18.6-alpine3.23@sha256:697c180dbf244d3ce4a8f4cbc0156cde840af055c1bf8b76aebe422a4822086f`,
but that container image has not been executed in the local Windows evidence
run. Every test/deployment database must be created from `template0` with
`ENCODING 'UTF8'`, `LOCALE_PROVIDER libc`, `LC_COLLATE 'C'`, and `LC_CTYPE 'C'`.
The semantic readiness fingerprint also pins the effective PostgreSQL major,
encoding, locale provider, collation/ctype, and database posture and therefore
fails closed on an unqualified target rather than assuming portability.

## Fail-closed checksum behavior

The migrator stores each exact embedded SQL checksum in
`control.schema_migrations`. It serializes migration attempts with a PostgreSQL
advisory transaction lock. A known migration identifier with any other checksum
aborts the transaction before later migration SQL runs. Operators must never
edit `control.schema_migrations`, bypass the mismatch, or replay the monolithic
foundation over an existing schema.

## Older 001 databases

An existing database carrying any older `001_foundation` checksum is not a
supported automatic upgrade target. Startup migration must remain failed and
the application must remain unavailable. The operator must first take and
verify a restorable backup, retain the old database without mutation, and choose
one reviewed path:

1. For disposable pre-release data only, provision a new empty database, apply
   the canonical baseline, verify catalog/ACL/readiness fingerprints, and cut
   over explicitly. Deleting the old database is a separate operator action
   after backup verification and acceptance; the application performs no reset.
2. For any data that must survive, commission a staged additive upgrade. It must
   inventory the actually stored checksum and historical proof keys, bridge
   legacy/current proof formats, backfill only cryptographically verified rows,
   preserve immutable idempotency/audit references, wait through replay and
   lease windows, remove obsolete function overloads and grants, and prove that
   upgraded and fresh catalogs/ACLs are equivalent.

Unknown proof keys, incompatible legacy proof records, partial backfills, or an
unverified backup block migration. A checksum rewrite or destructive schema
rebuild is never treated as an upgrade.

## Release checks

- The LF migration and role-script checksum test must match the values above.
- Applying the baseline to a fresh PostgreSQL database and applying it again
  must produce one recorded migration and no domain seed rows.
- A tampered recorded checksum must fail without changing the recorded value or
  applying schema work.
- Runtime readiness must verify the current schema and exact role capabilities.
- Future migrations must have fresh-database, previous-release upgrade,
  concurrent-migrator, data-preservation, and catalog/ACL equivalence tests.
